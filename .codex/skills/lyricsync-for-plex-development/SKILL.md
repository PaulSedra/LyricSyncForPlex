---
name: lyricsync-for-plex-development
description: Develop LyricSync for Plex features and fixes in the local Windows WinUI/.NET repo. Use when Codex changes C#, XAML, config, Plex API integration, lyrics behavior, player controls, settings UI, tests, or app architecture in C:\Users\woomb\Documents\GitHub\PlexLyricSync.
---

# LyricSync for Plex Development

## Overview

Use this workflow for code changes in LyricSync for Plex. Keep edits close to the existing WinUI/.NET patterns, document new or materially changed methods, and validate with the repo testing workflow.

## Workflow

1. Read the live files before editing. Prefer `rg` for search and inspect the current implementation around the requested behavior.
2. Keep feature code in the layer that already owns the behavior:
   - `Core` project (namespace `LyricSyncForPlex.Core`) for models, parsing, lyrics utilities, path providers, HTTP clients, and Plex/LRCLIB API logic.
   - `App/Views` for WinUI pages, XAML controls, player controls, lyrics rendering, and window lifecycle.
   - `App/Utils` for app-side config loading and persistence.
   - `Tests` or `IntegrationTests` for focused regression coverage.
3. Preserve user changes in the worktree. Do not revert unrelated dirty files.
4. Prefer small, explicit changes over new abstraction unless shared behavior is genuinely emerging.
5. Use the existing app config pattern for settings: `Config`, `ConfigLoader`, and `SettingsPage`.
6. For Plex playback commands, use `PlexApiClient` and the existing `ClientUrl`/`ClientId` flow in `MainWindow`.
7. After code changes, use `$lyricsync-for-plex-testing` to run solution tests and, when relevant, the x64 WinUI build or packaged launch workflow.

## Method Documentation

Add or update method documentation whenever adding a method or materially changing an existing method's contract.

- For C# public, internal, protected, or private helper methods with non-trivial behavior, add XML documentation with `<summary>` and meaningful `<param>` / `<returns>` tags when applicable.
- For event handlers and tiny self-explanatory UI callbacks, documentation is optional unless the behavior is subtle or the method coordinates app state.
- For async methods, document side effects such as network calls, config writes, UI dispatching, cancellation behavior, or state cache updates.
- Do not add empty comments that merely restate the method name.

Example:

```csharp
/// <summary>
/// Applies the configured playback speed to the active Plexamp client.
/// </summary>
/// <param name="ct">Cancellation token for the Plex companion request.</param>
internal async Task ApplyPlaybackSpeedAsync(CancellationToken ct)
```

## Validation Notes

Use `$lyricsync-for-plex-testing` after edits. The usual minimum verification is:

```powershell
dotnet test LyricSyncForPlex.sln --configuration Debug --no-restore
```

If XAML, WinUI code-behind, app startup, package settings, or native app behavior changed, also run:

```powershell
dotnet build App\App.csproj --configuration Debug --no-restore -p:Platform=x64
```

Report pass/fail, relevant warnings, and anything not visually verified.
