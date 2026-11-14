$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$sonarVersion = '26.10.0.133078'
$sonarHostUrl = 'http://127.0.0.1:9000'
$projectKey = 'PlexLyricSync'
$projectName = 'PlexLyricSync'
$runnerTemp = $env:RUNNER_TEMP
$archivePath = Join-Path $runnerTemp "sonarqube-$sonarVersion.zip"
$extractPath = Join-Path $runnerTemp 'sonarqube'
$serverHome = Join-Path $extractPath "sonarqube-$sonarVersion"
$startScript = Join-Path $serverHome 'bin\windows-x86-64\StartSonar.bat'
$serverProcess = $null

function New-BasicAuthHeader {
    param(
        [Parameter(Mandatory)] [string] $Username,
        [Parameter(Mandatory)] [string] $Password
    )

    $bytes = [Text.Encoding]::ASCII.GetBytes("${Username}:${Password}")
    return @{ Authorization = "Basic $([Convert]::ToBase64String($bytes))" }
}

function Invoke-SonarApi {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [ValidateSet('Get', 'Post')] [string] $Method = 'Get',
        [hashtable] $Parameters = @{},
        [hashtable] $Headers = @{}
    )

    $uri = "$script:SonarHostUrl$Path"
    if ($Method -eq 'Get' -and $Parameters.Count -gt 0) {
        $query = foreach ($key in $Parameters.Keys) {
            '{0}={1}' -f [uri]::EscapeDataString([string]$key), [uri]::EscapeDataString([string]$Parameters[$key])
        }
        $uri += '?' + ($query -join '&')
    }

    if ($Method -eq 'Post') {
        return Invoke-RestMethod -Method Post -Uri $uri -Headers $Headers -Body $Parameters `
            -ContentType 'application/x-www-form-urlencoded' -TimeoutSec 30
    }

    return Invoke-RestMethod -Method Get -Uri $uri -Headers $Headers -TimeoutSec 30
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ScannerCommandWithoutGitHubContext {
    param([Parameter(Mandatory)] [string[]] $ArgumentList)

    $githubVariables = @(
        'CI', 'GITHUB_ACTIONS', 'GITHUB_EVENT_NAME', 'GITHUB_EVENT_PATH',
        'GITHUB_BASE_REF', 'GITHUB_HEAD_REF', 'GITHUB_REF', 'GITHUB_REF_NAME',
        'GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_WORKFLOW'
    )
    $savedVariables = @{}
    foreach ($name in $githubVariables) {
        $savedVariables[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    try {
        Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList $ArgumentList
    }
    finally {
        foreach ($name in $githubVariables) {
            [Environment]::SetEnvironmentVariable($name, $savedVariables[$name], 'Process')
        }
    }
}

function Write-SonarLogs {
    $logsPath = Join-Path $script:ServerHome 'logs'
    foreach ($name in @('sonar.log', 'web.log', 'es.log')) {
        $path = Join-Path $logsPath $name
        if (Test-Path -LiteralPath $path) {
            Write-Host "--- Last lines of $name ---"
            Get-Content -LiteralPath $path -Tail 50
        }
    }
}

function Get-MetricValue {
    param(
        [Parameter(Mandatory)] [hashtable] $Measures,
        [Parameter(Mandatory)] [string] $Key,
        [string] $Default = '0'
    )

    if ($Measures.ContainsKey($Key)) {
        return [string]$Measures[$Key]
    }
    return $Default
}

function ConvertTo-MarkdownCell {
    param([AllowNull()] [string] $Value)

    if ($null -eq $Value) {
        return '—'
    }

    $escaped = $Value -replace '[\r\n\t]+', ' '
    $escaped = $escaped.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    $escaped = $escaped.Replace('|', '&#124;').Replace('[', '&#91;').Replace(']', '&#93;')
    $escaped = $escaped.Replace('`', '&#96;').Replace('*', '&#42;').Replace('_', '&#95;').Replace('~', '&#126;')
    return $escaped
}

function Write-IssueTable {
    param([object[]] $IssueList)

    foreach ($issue in $IssueList) {
        $cells = @(
            $issue.severity,
            $issue.software_quality,
            $issue.code_attribute,
            $issue.details,
            $issue.reference,
            $issue.rule
        ) | ForEach-Object { ConvertTo-MarkdownCell ([string]$_) }
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value ('| ' + ($cells -join ' | ') + ' |')
    }
}

function Add-SummaryLines {
    param([string[]] $Lines)
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value $Lines
}

function Get-ChangedLinesByPath {
    param(
        [Parameter(Mandatory)] [string] $BaseSha,
        [Parameter(Mandatory)] [string] $HeadSha
    )

    $diffLines = & git -c core.quotepath=false diff --no-ext-diff --no-renames --unified=0 `
        "$BaseSha...$HeadSha" --
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the pull request diff from git.'
    }

    $changedLines = @{}
    $currentPath = $null
    foreach ($line in $diffLines) {
        if ($line.StartsWith('+++ b/')) {
            $currentPath = $line.Substring(6).Replace('\', '/')
            if (-not $changedLines.ContainsKey($currentPath)) {
                $changedLines[$currentPath] = [Collections.Generic.HashSet[int]]::new()
            }
            continue
        }

        $match = [regex]::Match($line, '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@')
        if (-not $match.Success -or $null -eq $currentPath) {
            continue
        }

        $firstLine = [int]$match.Groups[1].Value
        $lineCount = 1
        if ($match.Groups[2].Success) {
            $lineCount = [int]$match.Groups[2].Value
        }
        for ($offset = 0; $offset -lt $lineCount; $offset++) {
            [void]$changedLines[$currentPath].Add($firstLine + $offset)
        }
    }

    return $changedLines
}

function Test-IssueInChangedLines {
    param(
        [Parameter(Mandatory)] [object] $Issue,
        [Parameter(Mandatory)] [hashtable] $ChangedLines
    )

    $startLine = $Issue.textRange.startLine
    if ($null -eq $startLine) {
        $startLine = $Issue.line
    }
    if ($null -eq $startLine) {
        return $false
    }

    $endLine = $Issue.textRange.endLine
    if ($null -eq $endLine) {
        $endLine = $startLine
    }

    $component = [string]$Issue.component
    if ($component.StartsWith("${script:ProjectKey}:")) {
        $path = $component.Substring($script:ProjectKey.Length + 1)
    }
    else {
        $path = $component
    }
    $path = $path.Replace('\', '/')

    if (-not $ChangedLines.ContainsKey($path)) {
        return $false
    }

    for ($line = [int]$startLine; $line -le [int]$endLine; $line++) {
        if ($ChangedLines[$path].Contains($line)) {
            return $true
        }
    }
    return $false
}

function Get-IssueSummary {
    param(
        [Parameter(Mandatory)] [object] $Issue,
        [Parameter(Mandatory)] [bool] $CheckPullRequestDiff,
        [Parameter(Mandatory)] [hashtable] $ChangedLines
    )

    $matchedCategories = [Collections.Generic.List[string]]::new()
    $impacts = @($Issue.impacts)
    foreach ($impact in $impacts) {
        if ($impact.softwareQuality -eq 'SECURITY' -and -not $matchedCategories.Contains('Security')) {
            $matchedCategories.Add('Security')
        }
        if ($impact.softwareQuality -eq 'RELIABILITY' -and -not $matchedCategories.Contains('Reliability')) {
            $matchedCategories.Add('Reliability')
        }
        if ($impact.severity -eq 'HIGH' -and -not $matchedCategories.Contains('High')) {
            $matchedCategories.Add('High')
        }
    }
    if ($Issue.severity -eq 'HIGH' -and -not $matchedCategories.Contains('High')) {
        $matchedCategories.Add('High')
    }

    $codeAttribute = '—'
    switch ([string]$Issue.cleanCodeAttributeCategory) {
        'CONSISTENT' {
            $codeAttribute = 'Consistency'
            $matchedCategories.Add('Consistency')
        }
        'INTENTIONAL' { $codeAttribute = 'Intentionality' }
        'ADAPTABLE' {
            $codeAttribute = 'Adaptability'
            $matchedCategories.Add('Adaptability')
        }
        'RESPONSIBLE' {
            $codeAttribute = 'Responsibility'
            $matchedCategories.Add('Responsibility')
        }
    }

    $severities = @($impacts | ForEach-Object {
        switch ([string]$_.severity) {
            'HIGH' { 'High' }
            'MEDIUM' { 'Medium' }
            'LOW' { 'Low' }
            default { $null }
        }
    } | Where-Object { $null -ne $_ } | Sort-Object -Unique)
    $severity = if ($severities.Count -gt 0) { $severities -join ', ' } elseif ($Issue.severity) { [string]$Issue.severity } else { '—' }

    $qualities = @($impacts | ForEach-Object {
        switch ([string]$_.softwareQuality) {
            'SECURITY' { 'Security' }
            'RELIABILITY' { 'Reliability' }
            'MAINTAINABILITY' { 'Maintainability' }
            default { $null }
        }
    } | Where-Object { $null -ne $_ } | Sort-Object -Unique)
    $softwareQuality = if ($qualities.Count -gt 0) { $qualities -join ', ' } else { '—' }

    $issueLine = $Issue.textRange.startLine
    if ($null -eq $issueLine) {
        $issueLine = $Issue.line
    }
    $component = [string]$Issue.component
    if ($component.StartsWith("${script:ProjectKey}:")) {
        $component = $component.Substring($script:ProjectKey.Length + 1)
    }
    $reference = $component + ':' + $(if ($null -eq $issueLine) { 'file-level' } else { [string]$issueLine })

    return [pscustomobject]@{
        key = [string]$Issue.key
        severity = $severity
        software_quality = $softwareQuality
        code_attribute = $codeAttribute
        details = [string]$Issue.message
        reference = $reference
        rule = [string]$Issue.rule
        matched_categories = @($matchedCategories | Sort-Object -Unique)
        in_pr_diff = ($CheckPullRequestDiff -and (Test-IssueInChangedLines -Issue $Issue -ChangedLines $ChangedLines))
    }
}

function Write-GitHubOutput {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Value
    )

    Add-Content -Path $env:GITHUB_OUTPUT -Encoding utf8 -Value "$Name=$Value"
}

try {
    $env:SONAR_ES_BOOTSTRAP_CHECKS_DISABLE = 'true'
    $env:SONAR_HOST_URL = $sonarHostUrl

    Write-Host "Downloading SonarQube Community Build $sonarVersion."
    Invoke-WebRequest -Uri "https://binaries.sonarsource.com/Distribution/sonarqube/sonarqube-$sonarVersion.zip" `
        -OutFile $archivePath -TimeoutSec 120
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
    if (-not (Test-Path -LiteralPath $startScript)) {
        throw "SonarQube startup script was not found at $startScript."
    }

    $serverProcess = Start-Process -FilePath $env:ComSpec -ArgumentList "/c `"$startScript`"" `
        -WorkingDirectory $serverHome -WindowStyle Hidden -PassThru

    $sonarReady = $false
    for ($attempt = 1; $attempt -le 90; $attempt++) {
        if ($serverProcess.HasExited) {
            Write-SonarLogs
            throw "Temporary SonarQube exited with code $($serverProcess.ExitCode)."
        }

        try {
            $status = Invoke-RestMethod -Method Get -Uri "$sonarHostUrl/api/system/status" -TimeoutSec 2
            if ($status.status -eq 'UP') {
                $sonarReady = $true
                break
            }
        }
        catch {
            # The server is still starting.
        }
        Start-Sleep -Seconds 4
    }

    if (-not $sonarReady) {
        Write-SonarLogs
        throw 'Temporary SonarQube did not become ready within six minutes.'
    }
    Write-Host 'Temporary SonarQube is ready.'

    $adminPassword = 'Aa9!' + [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
    $defaultAdminHeaders = New-BasicAuthHeader -Username 'admin' -Password 'admin'
    Invoke-SonarApi -Path '/api/users/change_password' -Method Post -Headers $defaultAdminHeaders -Parameters @{
        login = 'admin'
        previousPassword = 'admin'
        password = $adminPassword
    } | Out-Null

    $adminHeaders = New-BasicAuthHeader -Username 'admin' -Password $adminPassword
    Invoke-SonarApi -Path '/api/projects/create' -Method Post -Headers $adminHeaders -Parameters @{
        project = $projectKey
        name = $projectName
    } | Out-Null

    $profiles = Invoke-SonarApi -Path '/api/qualityprofiles/search' -Headers $adminHeaders -Parameters @{ ps = 500 }
    $comprehensiveLanguages = @($profiles.profiles |
        Where-Object { $_.name -eq 'Sonar way comprehensive' } |
        Select-Object -ExpandProperty language -Unique)
    if ($comprehensiveLanguages.Count -eq 0) {
        throw 'Sonar way comprehensive profiles were not found in the temporary SonarQube instance.'
    }
    foreach ($language in $comprehensiveLanguages) {
        Invoke-SonarApi -Path '/api/qualityprofiles/add_project' -Method Post -Headers $adminHeaders -Parameters @{
            project = $projectKey
            language = $language
            qualityProfile = 'Sonar way comprehensive'
        } | Out-Null
    }

    $tokenResponse = Invoke-SonarApi -Path '/api/user_tokens/generate' -Method Post -Headers $adminHeaders -Parameters @{
        name = "github-actions-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
        type = 'USER_TOKEN'
    }
    $sonarToken = [string]$tokenResponse.token
    if ([string]::IsNullOrWhiteSpace($sonarToken)) {
        throw 'SonarQube did not return a temporary analysis token.'
    }
    Write-Output "::add-mask::$sonarToken"
    $env:SONAR_TOKEN = $sonarToken

    $javaExe = (Get-Command java -ErrorAction Stop).Source
    $beginArguments = @(
        'sonarscanner', 'begin',
        "/k:$projectKey",
        "/n:$projectName",
        "/d:sonar.host.url=$sonarHostUrl",
        "/d:sonar.token=$sonarToken",
        '/d:sonar.scanner.skipJreProvisioning=true',
        "/d:sonar.scanner.javaExePath=$javaExe",
        '/d:sonar.cs.opencover.reportsPaths=**/coverage.opencover.xml'
    )

    Invoke-ScannerCommandWithoutGitHubContext -ArgumentList $beginArguments
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @(
        'test', 'PlexLyricSync.sln', '--configuration', 'Debug', '--no-restore',
        '--collect', 'XPlat Code Coverage', '--settings', '.github/sonar.runsettings'
    )
    Invoke-ScannerCommandWithoutGitHubContext -ArgumentList @(
        'sonarscanner', 'end', "/d:sonar.token=$sonarToken"
    )

    $reportTaskPath = Join-Path $PWD '.sonarqube\out\.sonar\report-task.txt'
    if (-not (Test-Path -LiteralPath $reportTaskPath)) {
        throw 'SonarScanner did not write a compute-engine task report.'
    }
    $taskLine = Get-Content -LiteralPath $reportTaskPath | Where-Object { $_ -like 'ceTaskId=*' } | Select-Object -First 1
    $taskId = if ($taskLine) { $taskLine.Substring('ceTaskId='.Length) } else { '' }
    if ([string]::IsNullOrWhiteSpace($taskId)) {
        throw 'SonarScanner did not write a compute-engine task ID.'
    }

    $sonarHeaders = @{ Authorization = "Bearer $sonarToken" }
    $taskStatus = ''
    for ($attempt = 1; $attempt -le 120; $attempt++) {
        $taskResponse = Invoke-SonarApi -Path '/api/ce/task' -Headers $sonarHeaders -Parameters @{ id = $taskId }
        $taskStatus = [string]$taskResponse.task.status
        if ($taskStatus -eq 'SUCCESS') {
            break
        }
        if ($taskStatus -in @('ERROR', 'CANCELED')) {
            throw "SonarQube analysis failed: $($taskResponse.task.errorMessage)"
        }
        Start-Sleep -Seconds 2
    }
    if ($taskStatus -ne 'SUCCESS') {
        throw 'SonarQube did not finish analysis within four minutes.'
    }

    $metricKeys = @(
        'violations', 'coverage', 'ncloc', 'software_quality_security_issues',
        'software_quality_reliability_issues', 'software_quality_maintainability_issues',
        'software_quality_blocker_issues', 'software_quality_high_issues',
        'software_quality_medium_issues', 'software_quality_low_issues', 'software_quality_info_issues'
    ) -join ','
    $measureResponse = Invoke-SonarApi -Path '/api/measures/component' -Headers $sonarHeaders -Parameters @{
        component = $projectKey
        metricKeys = $metricKeys
    }
    $measures = @{}
    foreach ($measure in @($measureResponse.component.measures)) {
        $measures[[string]$measure.metric] = [string]$measure.value
    }

    $coverageText = Get-MetricValue -Measures $measures -Key 'coverage'
    $linesOfCodeText = Get-MetricValue -Measures $measures -Key 'ncloc'
    $coverage = [double]::Parse($coverageText, [Globalization.CultureInfo]::InvariantCulture)
    $linesOfCode = [int64]::Parse($linesOfCodeText, [Globalization.CultureInfo]::InvariantCulture)

    $changedLines = @{}
    $checkPullRequestDiff = $env:PR_DIFF_CHECK_ENABLED -eq 'true'
    if ($checkPullRequestDiff) {
        if ([string]::IsNullOrWhiteSpace($env:PR_BASE_SHA) -or [string]::IsNullOrWhiteSpace($env:PR_HEAD_SHA)) {
            throw 'Pull request base or head SHA is missing.'
        }
        $changedLines = Get-ChangedLinesByPath -BaseSha $env:PR_BASE_SHA -HeadSha $env:PR_HEAD_SHA
    }

    $issues = [Collections.Generic.List[object]]::new()
    $pageSize = 500
    $page = 1
    $issueTotal = 0
    do {
        $issueResponse = Invoke-SonarApi -Path '/api/issues/search' -Headers $sonarHeaders -Parameters @{
            componentKeys = $projectKey
            ps = $pageSize
            p = $page
        }
        $issueTotal = [int]$issueResponse.paging.total
        foreach ($issue in @($issueResponse.issues)) {
            $issues.Add((Get-IssueSummary -Issue $issue -CheckPullRequestDiff $checkPullRequestDiff -ChangedLines $changedLines))
        }
        $page++
    } while ((($page - 1) * $pageSize) -lt $issueTotal)

    $securityIssues = Get-MetricValue -Measures $measures -Key 'software_quality_security_issues'
    $reliabilityIssues = Get-MetricValue -Measures $measures -Key 'software_quality_reliability_issues'
    $maintainabilityIssues = Get-MetricValue -Measures $measures -Key 'software_quality_maintainability_issues'
    $blockerIssues = Get-MetricValue -Measures $measures -Key 'software_quality_blocker_issues'
    $highIssues = Get-MetricValue -Measures $measures -Key 'software_quality_high_issues'
    $mediumIssues = Get-MetricValue -Measures $measures -Key 'software_quality_medium_issues'
    $lowIssues = Get-MetricValue -Measures $measures -Key 'software_quality_low_issues'
    $infoIssues = Get-MetricValue -Measures $measures -Key 'software_quality_info_issues'

    $consistencyIssues = @($issues | Where-Object { $_.code_attribute -eq 'Consistency' }).Count
    $intentionalityIssues = @($issues | Where-Object { $_.code_attribute -eq 'Intentionality' }).Count
    $adaptabilityIssues = @($issues | Where-Object { $_.code_attribute -eq 'Adaptability' }).Count
    $responsibilityIssues = @($issues | Where-Object { $_.code_attribute -eq 'Responsibility' }).Count
    $unclassifiedIssues = @($issues | Where-Object { $_.code_attribute -eq '—' }).Count
    $selectedIssues = @($issues | Where-Object { $_.matched_categories.Count -gt 0 })
    $prIssues = @($issues | Where-Object { $_.in_pr_diff })
    $blockingPrIssues = @($prIssues | Where-Object { $_.matched_categories.Count -gt 0 })
    $blockingIssueCount = if ($checkPullRequestDiff) { $blockingPrIssues.Count } else { 0 }
    $coverageBelow60 = $coverage -lt 60

    $coverageDisplay = $coverage.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
    $formattedLinesOfCode = $linesOfCode.ToString('N0', [Globalization.CultureInfo]::InvariantCulture)
    New-Item -Path 'coverage-badge' -ItemType Directory -Force | Out-Null
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    $coverageBadge = [ordered]@{
        schemaVersion = 1
        label = 'coverage'
        message = "$coverageDisplay%"
        color = 'yellowgreen'
    } | ConvertTo-Json -Compress
    $linesBadge = [ordered]@{
        schemaVersion = 1
        label = 'Lines of code'
        message = [string]$linesOfCode
        color = 'blue'
    } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path 'coverage-badge' 'coverage.json'), $coverageBadge, $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path 'coverage-badge' 'lines-of-code.json'), $linesBadge, $utf8NoBom)

    $summaryLines = [Collections.Generic.List[string]]::new()
    $summaryLines.Add('## SonarQube Summary')
    $summaryLines.Add('')
    if ($checkPullRequestDiff) {
        $summaryLines.Add('The SonarQube check fails if code in this PR introduces new blocking issues or brings coverage below 60%.')
        $summaryLines.Add('If blocking issues are found, their details are listed below.')
    }
    else {
        $summaryLines.Add('The SonarQube check lists all detected issues and fails if coverage is below 60%.')
    }
    $summaryLines.Add('')
    $summaryLines.Add('| Software Quality | Issues |')
    $summaryLines.Add('| --- | ---: |')
    $summaryLines.Add("| Security | $securityIssues |")
    $summaryLines.Add("| Reliability | $reliabilityIssues |")
    $summaryLines.Add("| Maintainability | $maintainabilityIssues |")
    $summaryLines.Add('')
    $summaryLines.Add('| Severity | Issues |')
    $summaryLines.Add('| --- | ---: |')
    $summaryLines.Add("| Blocker | $blockerIssues |")
    $summaryLines.Add("| High | $highIssues |")
    $summaryLines.Add("| Medium | $mediumIssues |")
    $summaryLines.Add("| Low | $lowIssues |")
    $summaryLines.Add("| Info | $infoIssues |")
    $summaryLines.Add('')
    $summaryLines.Add('| Code Attribute | Issues |')
    $summaryLines.Add('| --- | ---: |')
    $summaryLines.Add("| Consistency | $consistencyIssues |")
    $summaryLines.Add("| Intentionality | $intentionalityIssues |")
    $summaryLines.Add("| Adaptability | $adaptabilityIssues |")
    $summaryLines.Add("| Responsibility | $responsibilityIssues |")
    if ($unclassifiedIssues -gt 0) {
        $summaryLines.Add("| Unclassified | $unclassifiedIssues |")
    }
    $summaryLines.Add('')
    $summaryLines.Add("Total issues across source files: **$issueTotal**")
    $summaryLines.Add('')
    $summaryLines.Add("Code coverage: ${coverageDisplay}%")
    $summaryLines.Add('')
    $summaryLines.Add("Lines of code: $formattedLinesOfCode")
    $summaryLines.Add('')
    if ($checkPullRequestDiff) {
        $summaryLines.Add("## Issues Created In This PR ($($prIssues.Count))")
        $summaryLines.Add('')
        if ($prIssues.Count -gt 0) {
            $summaryLines.Add('| Severity | Software Quality | Code Attribute | Issue Details | Reference | Rule |')
            $summaryLines.Add('| --- | --- | --- | --- | --- | --- |')
            Add-SummaryLines -Lines $summaryLines.ToArray()
            Write-IssueTable -IssueList $prIssues
            $summaryLines.Clear()
            $summaryLines.Add('')
        }
        else {
            $summaryLines.Add('Hooray! Sonar did not detect any issues in this PR.')
            $summaryLines.Add('')
        }
        $summaryLines.Add("## Blocking Issues ($blockingIssueCount)")
        $summaryLines.Add('')
        if ($blockingIssueCount -gt 0) {
            $summaryLines.Add('| Severity | Software Quality | Code Attribute | Issue Details | Reference | Rule |')
            $summaryLines.Add('| --- | --- | --- | --- | --- | --- |')
            Add-SummaryLines -Lines $summaryLines.ToArray()
            Write-IssueTable -IssueList $blockingPrIssues
            $summaryLines.Clear()
            $summaryLines.Add('')
        }
        else {
            $summaryLines.Add('Sonar did not detect any blocking issues in this PR.')
        }
    }
    else {
        $summaryLines.Add('## Blocking Issues')
        $summaryLines.Add('')
        if ($selectedIssues.Count -gt 0) {
            $summaryLines.Add('| Severity | Software Quality | Code Attribute | Issue Details | Reference | Rule |')
            $summaryLines.Add('| --- | --- | --- | --- | --- | --- |')
            Add-SummaryLines -Lines $summaryLines.ToArray()
            Write-IssueTable -IssueList $selectedIssues
            $summaryLines.Clear()
        }
        else {
            $summaryLines.Add('Sonar did not detect any blocking issues.')
        }
    }
    if ($summaryLines.Count -gt 0) {
        Add-SummaryLines -Lines $summaryLines.ToArray()
    }

    Write-GitHubOutput -Name 'blocking_issue_count' -Value ([string]$blockingIssueCount)
    Write-GitHubOutput -Name 'coverage_below_60' -Value ([string]$coverageBelow60).ToLowerInvariant()
    Write-GitHubOutput -Name 'coverage' -Value $coverageDisplay
}
finally {
    if (Test-Path Env:\SONAR_TOKEN) {
        Remove-Item Env:\SONAR_TOKEN -ErrorAction SilentlyContinue
    }
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        & "$env:SystemRoot\System32\taskkill.exe" /PID $serverProcess.Id /T /F *> $null
    }
}
