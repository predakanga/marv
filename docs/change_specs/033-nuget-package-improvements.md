# CS-033: NuGet Package Improvements

**Source:** GitHub issue #9
**Scope:** CI/CD + Core
**Complexity:** Small-Medium
**Breaking changes:** None — additive improvements to package metadata and build configuration
**Status:** Pending

---

## Problem

The NuGet packages (TDW.Marv.Core and TDW.Marv.Testing) have minimal
metadata and lack several features that improve the consumer experience:
deterministic builds, symbol packages with Source Link for debugging,
and per-package README files for the nuget.org listing.

## Changes

### 1. Enable deterministic builds

Add the following to `Directory.Build.props` so both packable projects
inherit the settings:

```xml
<PropertyGroup Condition="'$(CI)' == 'true'">
  <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
</PropertyGroup>
```

This ensures file paths embedded in PDBs are reproducible in CI. The
condition avoids affecting local development builds.

Add the `DeterministicBuildTargets` package to both packable projects
(or `Directory.Build.props` with a condition on `IsPackable`):

```xml
<PackageReference Include="DotNet.ReproducibleBuilds"
                  Version="*"
                  PrivateAssets="All" />
```

### 2. Enable Source Link

Add the `Microsoft.SourceLink.GitHub` package reference to
`Directory.Build.props` (conditional on `IsPackable`):

```xml
<ItemGroup Condition="'$(IsPackable)' == 'true'">
  <PackageReference Include="Microsoft.SourceLink.GitHub"
                    Version="*"
                    PrivateAssets="All" />
</ItemGroup>
```

Add the supporting properties:

```xml
<PropertyGroup Condition="'$(IsPackable)' == 'true'">
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
</PropertyGroup>
```

Both packable projects already set `<RepositoryUrl>`, which Source Link
requires.

### 3. Enable symbol packages

Add to `Directory.Build.props` (conditional on `IsPackable`):

```xml
<PropertyGroup Condition="'$(IsPackable)' == 'true'">
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
```

Update the `publish-nuget` job in `.github/workflows/release.yml` to
push `.snupkg` files. The existing `dotnet nuget push packages/*.nupkg`
command already handles this when `.snupkg` files are present in the
same directory — `dotnet nuget push` automatically uploads the
associated symbol package. No command change needed, but verify this
in testing.

### 4. Add per-package README files

Create standalone `README.md` files in:

- `src/Marv.Core/README.md` — describing the core library, plugin API
  surface, and basic usage.
- `src/Marv.Testing/README.md` — describing the test helpers, builders,
  and harness.

Add to each `.csproj`:

```xml
<PackageReadmeFile>README.md</PackageReadmeFile>
```

And an `ItemGroup` entry to pack the file:

```xml
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="" />
</ItemGroup>
```

## Design decisions

- **Conditional on `IsPackable`:** Source Link, symbol packages, and
  deterministic build settings are only needed for the two packable
  projects. Conditioning on `IsPackable` in `Directory.Build.props`
  keeps the configuration centralised without affecting the host app or
  test projects.
- **Standalone READMEs:** The main project README covers development
  setup and contribution guidelines, which aren't relevant to package
  consumers. Standalone READMEs tailored to each package's audience are
  more useful on nuget.org.
- **Floating versions (`*`) for build-tooling packages:** Source Link
  and ReproducibleBuilds are build-time-only (`PrivateAssets="All"`)
  and don't affect the output. Floating versions ensure we pick up
  fixes without manual bumps.

## Testing

- Build locally and verify `.snupkg` files are produced alongside
  `.nupkg` files.
- Inspect a `.nupkg` to confirm the README is included.
- Inspect PDB to confirm Source Link metadata is embedded (e.g. using
  `dotnet sourcelink test`).
- Publish a pre-release version to nuget.org and verify the package
  page shows the README and offers symbol download.

## Impact

- **Plugin API:** No changes.
- **DX:** Consumers get debugger step-into support via Source Link,
  symbol packages, and better documentation on nuget.org.
- **Risk:** Low — all changes are additive build/packaging metadata.
  No runtime behaviour changes.
