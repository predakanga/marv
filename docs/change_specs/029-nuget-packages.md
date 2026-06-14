# CS-029: NuGet Packages for Marv.Core and Marv.Testing

**Source:** GitHub issue #5
**Scope:** CI/CD + Core
**Complexity:** Small-Medium
**Breaking changes:** None
**Status:** Pending

---

## Problem

Downstream plugin projects that depend on Marv.Core via `ProjectReference`
produce unversioned assemblies. When those plugins are loaded against the
versioned assemblies in published container images or binary releases
(e.g. Marv.Core v0.6.0), .NET throws assembly version mismatch errors.

Publishing Marv.Core and Marv.Testing as NuGet packages allows downstream
projects to consume them via `PackageReference`, which resolves the version
correctly.

## Changes

### 1. Add NuGet package metadata to Marv.Core

Add standard NuGet metadata properties to `src/Marv.Core/Marv.Core.csproj`:

- `PackageId` (defaults to project name, but set explicitly for clarity)
- `Authors`
- `Description`
- `PackageLicenseExpression`
- `RepositoryUrl`
- `PackageReadmeFile` (optional — a short README for the NuGet listing)

The `Version` is already set in `Directory.Build.props` and will be inherited.

### 2. Add NuGet package metadata to Marv.Testing

Add the same metadata properties to `src/Marv.Testing/Marv.Testing.csproj`.
Marv.Testing already has a `<Description>` element. NSubstitute remains a
dependency — it is required by `MockBot`, `MockUser`, and `MockChannel`
which return NSubstitute proxies.

### 3. Mark non-packable projects

Add `<IsPackable>false</IsPackable>` to `Directory.Build.props` as the
default, then override it to `true` in the two packable projects. This
prevents the host app, example plugins, and test projects from being
accidentally packed.

### 4. Add a `publish-nuget` job to the release workflow

Add a new job to `.github/workflows/release.yml` that runs alongside the
existing `publish-binaries` and `docker` jobs (after `wait-for-ci` and
`version`):

```yaml
publish-nuget:
  runs-on: ubuntu-latest
  needs: [wait-for-ci, version]
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 10.0.x
    - run: |
        VERSION=${{ needs.version.outputs.version }}
        dotnet pack src/Marv.Core/Marv.Core.csproj -c Release -p:Version="$VERSION" -o packages/
        dotnet pack src/Marv.Testing/Marv.Testing.csproj -c Release -p:Version="$VERSION" -o packages/
    - run: |
        dotnet nuget push packages/*.nupkg \
          --source "https://nuget.pkg.github.com/predakanga/index.json" \
          --api-key ${{ secrets.GITHUB_TOKEN }}
```

Packages are published to **GitHub Packages only** (not nuget.org).
Publishing occurs only on tagged releases (the workflow already triggers
on `v*` tags).

### 5. Simplify release binary artifacts

Now that Marv.Core and Marv.Testing are available as NuGet packages,
downstream plugin authors no longer need the empty `plugins/` scaffold
in the release archive. Remove the `mkdir -p plugins` step from the
`publish-binaries` job — plugin authors will build their plugins against
the NuGet packages and deploy them alongside the executable themselves.

The release archive for each platform contains just the published Marv
executable and its runtime dependencies.

### 6. Update the `github-release` job dependency

Add `publish-nuget` to the `needs` list of the `github-release` job so
that the GitHub release is only created after all artifacts (binaries,
Docker image, and NuGet packages) are published successfully.

## Design decisions

- **GitHub Packages only:** The project already uses GitHub Container
  Registry for Docker images. GitHub Packages provides a consistent
  experience and avoids the nuget.org API key management overhead.
  Publishing to nuget.org can be added later if there is demand.
- **Default `IsPackable=false`:** Safer than marking individual projects
  as non-packable — new projects are non-packable by default, reducing
  the risk of accidental publishing.
- **Tagged releases only:** No pre-release packages from CI. This keeps
  the feed clean and avoids version confusion for downstream consumers.
- **No plugins directory in release archive:** With NuGet packages
  available, the empty `plugins/` scaffold in the release archive is
  unnecessary. Plugin authors build against the NuGet packages and
  manage deployment themselves.

## Testing

- Run `dotnet pack` locally for both projects and verify `.nupkg` files
  are produced with correct metadata (`dotnet nuget inspect` or extract
  the `.nuspec` from the package).
- Verify that non-packable projects (host, plugins, tests) produce a
  build warning or error when `dotnet pack` is attempted.
- After a tagged release, verify packages appear in the GitHub Packages
  feed at `https://github.com/predakanga/marv/packages`.
- Test downstream consumption: create a test project that uses
  `PackageReference` for Marv.Core, build a plugin DLL, and load it
  against the published Marv binary.
- Verify the published executable still works without the `plugins/`
  directory present (it should — the directory is created at runtime
  if missing, or configured explicitly).

## Impact

- **Plugin API:** No changes to the plugin API surface.
- **DX:** Downstream plugin authors can consume Marv.Core and
  Marv.Testing via standard NuGet tooling instead of git submodules or
  ProjectReference workarounds.
- **Risk:** Low — this adds a publishing step without changing any
  existing code or behaviour. The only risk is misconfigured NuGet
  metadata, which would result in a failed publish (caught by CI).
