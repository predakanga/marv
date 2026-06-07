# CS-013: Docker Action Build Caching

**Source:** `TODO.md` item 2
**Scope:** CI/CD (`.github/workflows/release.yml`, `.github/workflows/ci.yml`)
**Complexity:** Small
**Breaking changes:** None

---

## Problem

The release workflow's Docker build (`docker/build-push-action`) does not
configure layer caching. Every release rebuild downloads base images,
restores NuGet packages, and compiles the entire solution from scratch.
For multi-platform builds (`linux/amd64,linux/arm64`), this doubles the
work. The CI workflow has a similar issue — four independent jobs each run
`dotnet restore` and `dotnet build` without sharing cached artifacts.

### Docker build

The `docker/build-push-action` step uses Buildx but does not specify a
cache backend. Buildx supports GitHub Actions cache (`type=gha`) which
stores and retrieves layer caches via the Actions cache API, avoiding
redundant restores and builds across runs.

### CI workflow

The CI workflow runs four jobs (`build-and-test`, `lint`,
`static-analysis`, `security`) that each independently restore and
sometimes build the solution. The `integration-test` job also restores and
builds. NuGet package restoration is the most duplicated step.

## Changes

### 1. Enable GitHub Actions cache for Docker Buildx

In `release.yml`, add cache configuration to the `docker/build-push-action`
step:

```yaml
- name: Build and push Docker image
  uses: docker/build-push-action@v6
  with:
    context: .
    push: true
    platforms: linux/amd64,linux/arm64
    build-args: |
      VERSION=${{ needs.version.outputs.version }}
    tags: |
      ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ needs.version.outputs.version }}
      ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:latest
    cache-from: type=gha
    cache-to: type=gha,mode=max
```

The `mode=max` setting caches all layers (not just the final stage),
which is important for the multi-stage Dockerfile — the `build` stage's
restore and compile layers are the most expensive and benefit most from
caching.

### 2. Add NuGet package caching to CI workflow

Add a NuGet cache step to each CI job that runs `dotnet restore`. Use
`actions/cache` with the NuGet global packages folder:

```yaml
- name: Cache NuGet packages
  uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: nuget-${{ runner.os }}-${{ hashFiles('**/*.csproj', 'Marv.slnx') }}
    restore-keys: |
      nuget-${{ runner.os }}-
```

Add this step to: `build-and-test`, `lint`, `static-analysis`, `security`,
and `integration-test`.

### 3. Add NuGet package caching to release binary builds

The `publish-binaries` job in `release.yml` runs across 5 matrix
configurations. Add the same NuGet cache step:

```yaml
- name: Cache NuGet packages
  uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: nuget-${{ runner.os }}-${{ matrix.rid }}-${{ hashFiles('**/*.csproj', 'Marv.slnx') }}
    restore-keys: |
      nuget-${{ runner.os }}-${{ matrix.rid }}-
      nuget-${{ runner.os }}-
```

## Design decisions

**Why `type=gha` over registry caching?** GitHub Actions cache is free
within the repository's cache quota (10 GB), requires no additional
registry configuration, and is the simplest option for GitHub-hosted
runners. Registry-based caching (`type=registry`) would require a
separate cache image tag and additional GHCR permissions. The `gha`
backend is the recommended default for GitHub Actions.

**Why not consolidate CI jobs?** The four CI jobs (`build-and-test`,
`lint`, `static-analysis`, `security`) run in parallel, which is faster
than running them sequentially. Consolidating them into one job would
reduce restore duplication but increase total wall-clock time. NuGet
caching addresses the duplication without sacrificing parallelism.

## Impact

- **CI speed:** NuGet restore hits cache on second run, saving 15-30s per
  job depending on package count.
- **Docker build speed:** Layer cache avoids redundant restore and compile
  steps across releases. First build after cache miss is unchanged;
  subsequent builds with the same dependencies are significantly faster.
- **Cache storage:** NuGet cache uses ~100-300 MB of the repository's
  Actions cache quota. Docker layer cache varies but `mode=max` may use
  1-2 GB. Both are well within the 10 GB default quota.
- **Plugin API:** No changes.
