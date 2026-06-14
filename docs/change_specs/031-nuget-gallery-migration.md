# CS-031: Migrate NuGet Packages to nuget.org — COMPLETED

**Source:** GitHub issue #7
**Scope:** CI/CD
**Complexity:** Small
**Breaking changes:** None
**Status:** Completed

---

## Problem

The NuGet packages for Marv.Core and Marv.Testing are currently published to
GitHub Packages (`nuget.pkg.github.com`), which requires authentication even
for consumers. This adds unnecessary friction for downstream plugin authors
who need to configure a PAT just to restore packages. Publishing to the main
NuGet Gallery (nuget.org) removes this barrier entirely.

## Changes

### 1. Update the `publish-nuget` job in `.github/workflows/release.yml`

Replace the legacy API key approach with NuGet Trusted Publishing (OIDC):

- Add `id-token: write` permission to the `publish-nuget` job so GitHub
  can issue an OIDC token.
- Add a step using `NuGet/login@v1` to exchange the OIDC token for a
  short-lived nuget.org API key. The `user` input should reference a
  repository secret (`${{ secrets.NUGET_USER }}`) containing the nuget.org
  profile name (not email).
- Update the `dotnet nuget push` step to use the temporary API key from
  the login step's output (`${{ steps.nuget-login.outputs.NUGET_API_KEY }}`)
  and target `https://api.nuget.org/v3/index.json`.
- Remove the old "Push to GitHub Packages" step entirely.

Example workflow snippet:

```yaml
publish-nuget:
  runs-on: ubuntu-latest
  needs: [wait-for-ci, version]
  permissions:
    id-token: write
  steps:
    - uses: actions/checkout@v6

    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x

    # ... cache and pack steps unchanged ...

    - name: NuGet login (OIDC)
      uses: NuGet/login@v1
      id: nuget-login
      with:
        user: ${{ secrets.NUGET_USER }}

    - name: Push to NuGet Gallery
      run: |
        dotnet nuget push packages/*.nupkg \
          --api-key ${{ steps.nuget-login.outputs.NUGET_API_KEY }} \
          --source https://api.nuget.org/v3/index.json
```

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

### 4. Configure Trusted Publishing policy on nuget.org (manual)

The repository owner must configure a Trusted Publishing policy on
nuget.org before the first release:

1. Log into nuget.org, go to username → **Trusted Publishing**.
2. Add a policy with:
   - **Repository Owner:** `predakanga`
   - **Repository:** `marv`
   - **Workflow File:** `release.yml`
   - **Environment:** (leave empty unless a GitHub environment is added)
3. Add a repository secret `NUGET_USER` containing the nuget.org profile
   name used above.

## Design decisions

- **Trusted Publishing over legacy API keys:** NuGet's Trusted Publishing
  uses GitHub OIDC tokens to obtain short-lived (1 hour) API keys at publish
  time. This eliminates the need to store a long-lived nuget.org API key as a
  repository secret, reducing the risk of credential leakage and removing the
  need for key rotation. See
  https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing.
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
- **Manual steps required:** The owner must configure a Trusted Publishing
  policy on nuget.org and add a `NUGET_USER` secret to the repository before
  the next release.
