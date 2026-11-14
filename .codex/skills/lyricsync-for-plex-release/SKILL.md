---
name: lyricsync-for-plex-release
description: Prepare and publish versioned LyricSync for Plex Windows MSIX releases.
---

# LyricSync for Plex releases

- Release from `main` unless the user directs otherwise. This repo has no
  version-specific release branch convention.
- Check the worktree, target commit, remote refs, and existing tags before
  preparing a release.
- Use a three-part marketing version (`MAJOR.MINOR.PATCH`) and map it to the
  four-part MSIX version as `MAJOR.MINOR.PATCH.0`. This maps the MSIX `Build`
  field to the marketing patch by project convention; keep `Revision` at 0.
- `App/Package.appxmanifest` is the package-version source of truth.
  Keep the embedded identity in `App/app.manifest` in sync. Package
  revision auto-increment is off, so update both versions explicitly. The package identity is now LyricSyncForPlex, which is separate from existing PlexLyricSync installs. Keep the Music/PlexLyricSync settings and lyrics path unchanged; verify the new install before removing the old package.
- The package version was reset to `0.1.0.0` before meaningful deployment. The
  prior `1.0.0.0` GitHub release and tag remain historical; never move or replace
  them. Before publishing a lower-version release, decide how to handle the
  older public release and uninstall the existing `1.0.0.0` local package
  before installing `0.1.0.0`.
- The package currently targets `Windows.Universal`. If Microsoft Store release
  becomes a goal, review the current Store version rules first: UWP package
  versions require a nonzero Major field and Revision 0, so `0.x` is not
  eligible as-is.
- Use the package version as a new annotated Git tag, following the existing
  numeric tag format. Never move or replace a published tag.
- Validate with the `lyricsync-for-plex-testing` skill. Build a Release x64 MSIX and
  confirm the artifact, package version, architecture, and signature.
- Check the resolved package output path: the project currently sets an
  project-relative `AppxPackageDir`, and `AppPackages/` is ignored.
- Before public distribution, confirm signing uses the intended release
  certificate and publisher. The project points to a local temporary
  certificate; never attach a private `.pfx` file to a release.
- Use the `commit-messages` skill for the release commit.
- When publication is authorized, push the requested branch and tag, create
  GitHub release notes that summarize user-facing changes, and attach the
  intended MSIX package. Verify the remote refs and release page afterward.
