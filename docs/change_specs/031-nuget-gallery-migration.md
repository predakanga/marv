# CS-031: Migrate NuGet Packages to nuget.org

**Source:** GitHub issue #7
**Scope:** CI/CD
**Complexity:** Small
**Breaking changes:** None
**Status:** Pending

---

## Problem

The NuGet packages for Marv.Core and Marv.Testing are currently published to
GitHub Packages (`nuget.pkg.github.com`), which requires authentication even
for consumers. This adds unnecessary friction for downstream plugin authors
who need to configure a PAT just to restore packages. Publishing to the main
NuGet Gallery (nuget.org) removes this barrier entirely.

## Changes

### 1. Update the `publish-nuget` job in `.github/workflows/release.yml`

Change the `dotnet nuget push` target from GitHub Packages to nuget.org:

- Replace the source URL with `https://api.nuget.org/v3/index.json`.
- Replace the API key from `${{ secrets.GITHUB_TOKEN }}` to
  `${{ secrets.NUGET_API_KEY }}` (a new repository secret).
- Rename the step from "Push to GitHub Packages" to "Push to NuGet Gallery".

### 2. Remove the `packages: write` permission

The `packages: write` permission in the workflow's top-level `permissions`
block is only needed for GitHub Packages. Since Docker images use
`ghcr.io` (Container Registry, not Packages), and binary artifacts use
`upload-artifact`, the `packages: write` permission can be removed.

**Note:** Verify that `ghcr.io` push does not require `packages: write`.
If it does, keep the permission.

### 3. Remove existing packages from GitHub Packages

After the first successful nuget.org publish, delete the existing packages
from GitHub Packages to avoid confusion. This is a manual step performed
by the repository owner via the GitHub UI or API.

### 4. Add `NUGET_API_KEY` repository secret

The repository owner must create an API key on nuget.org scoped to the
`Marv.Core` and `Marv.Testing` packages, then add it as a repository
secret named `NUGET_API_KEY`. This is a manual step.

## Design decisions

- **Single feed, not dual-publish:** The owner confirmed that GitHub Packages
  should be removed entirely rather than kept as a mirror. This avoids
  confusion about which feed is canonical and simplifies the workflow.
- **No changes to `.csproj` files:** The existing package metadata (PackageId,
  Authors, Description, License, RepositoryUrl) already meets nuget.org
  requirements. No project file changes are needed.
- **No `PackageProjectUrl`:** The `RepositoryUrl` property is already set and
  nuget.org uses it as the project link. Adding a separate `PackageProjectUrl`
  is unnecessary.

## Testing

- Verify the release workflow runs successfully with the new nuget.org target
  by creating a test pre-release tag.
- Confirm that the published packages are visible and installable from
  nuget.org without authentication.
- Confirm that downstream projects can `dotnet add package Marv.Core` without
  any custom NuGet source configuration.

## Impact

- **Downstream plugin authors:** Positive — no longer need to configure
  GitHub Packages authentication to consume Marv.Core and Marv.Testing.
- **Risk:** Low — the change is isolated to the release workflow and does not
  affect the build, tests, or application code.
- **Manual steps required:** The owner must create a nuget.org API key and
  add it as a repository secret before the next release.
