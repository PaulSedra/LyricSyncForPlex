# Contributing

Thanks for contributing to PlexLyricSync. Keep changes focused, describe their
user impact, and include tests for behavior changes.

## Development setup

Build and test the Windows app from a Windows machine with the .NET 8 SDK.
From the repository root, restore dependencies and run the solution tests:

```powershell
dotnet restore PlexLyricSync.sln
dotnet test PlexLyricSync.sln --configuration Debug --no-restore
```

The test command builds the test projects and runs the unit and integration
suites. GitHub Actions runs the same test command on Windows for pull requests
to `main` and pushes to `main`.

## Pull requests

- Target `main` and keep each pull request focused on one change.
- Add or update tests when changing behavior.
- Update the README when setup steps, features, or user-facing configuration
  change.
- Describe user-visible changes and the checks you ran. Include screenshots
  when they help explain a UI change.
- Resolve failed Windows CI checks before merging.

## Sensitive files

Do not commit Plex tokens, `appsettings.config.yaml`, or private signing
certificates (`.pfx` files). The app stores its Plex token in the local config
file under the Windows Music folder.
