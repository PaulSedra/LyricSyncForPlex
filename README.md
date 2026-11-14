# LyricSync for Plex

[![Windows CI](https://github.com/PaulSedra/PlexLyricSync/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/PaulSedra/PlexLyricSync/actions/workflows/ci.yml)

LyricSync for Plex is a Windows desktop companion for Plexamp. It follows the
active Plexamp session, displays lyrics in time with playback, and lets you
control playback from your desktop.

[Features](#features) · [Quick start](#quick-start) ·
[Development](#development) · [Project layout](#project-layout)

## Features

- Display synced or plain lyrics for the active Plexamp track.
- Read local `.lrc` and `.txt` files and look up missing lyrics through
  [LRCLIB](https://lrclib.net/).
- Play, pause, seek, skip tracks, and set playback speed from the app.
- Show optional Japanese romanization using Kawazu.
- Adjust how many synced lyric lines appear and whether Japanese romanization
  is shown.

Lyrics are stored in the Windows Music folder under
  `PlexLyricSync/lyrics/<Artist>/<Album>/`

## Quick start

### Install

Check the [GitHub Releases page](https://github.com/PaulSedra/PlexLyricSync/releases)
for published MSIX packages.

### Configure

1. Launch LyricSync for Plex. On first launch, enter your Plex server URL and
   Plex token. You can update these later in **Settings → Plex**.
2. Use a server URL in the form `http://<server-ip>:<port>`. Check your Plex
   server's Remote Access settings for its port.
3. Start playing music in Plexamp. LyricSync for Plex finds the active session
   and displays available lyrics.
4. Adjust playback speed and lyric display options in **Settings**.

LyricSync for Plex saves settings to
  `<Music folder>/PlexLyricSync/appsettings.config.yaml`. The file includes your
Plex token in plain text, so keep it private. Lyrics and romanized lyrics are
saved alongside one another under the `lyrics` folder.

## Requirements

- Windows 10 build 17763 or newer; Windows 11 is supported by the package
  target as well.
- A reachable Plex Media Server and an active Plexamp session.
- A Plex server URL and token.

The app uses the MSIX packaging model. The minimum Windows version is declared
in `App/Package.appxmanifest`.

## Development

Install the .NET 8 SDK, then run these commands from the repo root on Windows:

~~~powershell
dotnet restore LyricSyncForPlex.sln
dotnet test LyricSyncForPlex.sln --configuration Debug --no-restore
~~~

The solution contains the WinUI app, its shared core library, unit tests, and
integration tests. GitHub Actions runs the solution tests on Windows for pushes
and pull requests to `main`.

## Project layout

| Path | Purpose |
|---|---|
| `App/` | WinUI desktop app and MSIX package configuration |
| `Core/` | Shared Plex API, lyric lookup and parsing, and Japanese romanization |
| `Tests/` | Unit tests |
| `IntegrationTests/` | Integration tests for core behavior |
| `.github/workflows/ci.yml` | Windows build and test workflow |

## Contributing

See `[CONTRIBUTING.md](CONTRIBUTING.md)` for development setup, test commands, and pull request guidance.
