# Release Runbook

This document describes how to create a new release of Marv.

## Prerequisites

- Push access to the repository
- All CI checks passing on `main`

## Steps

### 1. Decide on a version number

Marv uses [Semantic Versioning](https://semver.org/):

- **Major** (`X.0.0`): Breaking changes to the plugin API or configuration format
- **Minor** (`0.X.0`): New features, new plugins, new configuration options
- **Patch** (`0.0.X`): Bug fixes, dependency updates, documentation changes

While Marv is pre-1.0, minor version bumps may include breaking changes.

### 2. Verify main is ready

```bash
git checkout main
git pull
dotnet build -c Release
dotnet test -c Release --filter "Category!=Integration"
```

Ensure all tests pass and the build is clean.

### 3. Bump the version number

Update the version in these locations:

- **`Directory.Build.props`** — the `<Version>` property (sets assembly and
  package versions for all projects)
- **`Dockerfile`** — the `ARG VERSION=` default value
- **`CHANGELOG.md`** — rename the `[Unreleased]` section to
  `[X.Y.Z] - YYYY-MM-DD` and add a comparison link at the bottom of the file

Commit these changes to `main` before tagging.

### 4. Create and push a version tag

```bash
git tag vX.Y.Z
git push origin vX.Y.Z
```

Replace `vX.Y.Z` with the version number you chose.

### 5. Monitor the release workflow

The `Release` workflow triggers automatically on version tags. It will:

1. Run the full test suite
2. Build platform-specific binaries (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64)
3. Build and push a multi-arch Docker image to `ghcr.io`
4. Create a GitHub Release with auto-generated release notes and attached binaries

Monitor the workflow at **Actions > Release** in the GitHub UI.

### 6. Review the GitHub Release

Once the workflow completes:

1. Go to the **Releases** page
2. Review the auto-generated release notes — edit if needed to highlight important changes
3. If this is a pre-release, check the **Set as a pre-release** box

### 7. Verify the Docker image

```bash
docker pull ghcr.io/predakanga/marv:0.1.0
docker run --rm ghcr.io/predakanga/marv:0.1.0 --help
```


Replace `0.1.0` with the release version.

## Rolling back a release

If a release has a critical issue:

1. Delete the GitHub Release from the Releases page
2. Delete the tag:
   ```bash
   git tag -d v0.1.0
   git push origin :refs/tags/v0.1.0
   ```
3. Fix the issue, then re-release following the steps above

The Docker image tag will be overwritten by the new release. The `:latest` tag always points to the most recent release.

## Hotfix releases

For urgent fixes against an older release:

1. Create a branch from the release tag:
   ```bash
   git checkout -b hotfix/v0.1.1 v0.1.0
   ```
2. Apply the fix and push the branch
3. Tag and push:
   ```bash
   git tag v0.1.1
   git push origin v0.1.1
   ```
4. Cherry-pick the fix back to `main` if applicable
