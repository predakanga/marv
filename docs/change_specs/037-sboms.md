# CS-037: Software Bills of Materials (SBOMs) — COMPLETED

**Source:** Maintainer request
**Scope:** CI/CD
**Complexity:** Medium
**Breaking changes:** None
**Status:** Completed

---

## Problem

Releases ship three kinds of artifact: NuGet packages (`TDW.Marv.Core`,
`TDW.Marv.Testing`), standalone binaries, and a multi-arch container image
on GHCR. None of them carry an SBOM, so downstream users cannot feed them
into vulnerability tracking tools.

The maintainer's tooling sets a minimum requirement:

- **Dependency-Track** only ingests **CycloneDX**.
- **sbom-operator** either scans running images with Syft itself or, with
  `fetch-oci-attestations` enabled, uses SBOMs attached to the image in the
  registry. It accepts CycloneDX/SPDX/Syft SBOMs attached as raw OCI
  referrers, legacy cosign `.att`/`.sbom` tags, or Sigstore bundles (OCI
  referrers). It does not verify signatures, and it looks attestations up
  by the exact digest Kubernetes reports as the pod's `imageID`. With
  containerd that is the multi-arch **index** digest.

So every artifact needs at least a CycloneDX SBOM, and the image's SBOM
must be attached to the index digest in the registry.

## Changes

### 1. CycloneDX generation script

`scripts/generate-sbom.sh <project> <output> [rid]` wraps the CycloneDX
.NET tool (`CycloneDX`, pinned in the new `dotnet-tools.json` local tool
manifest). It reads the project's NuGet dependency graph
(`project.assets.json`) and derives the SBOM's root component from MSBuild:

- Packable projects → type `library`, named after `PackageId`, with a
  `pkg:nuget/...` purl so Dependency-Track matches it to the package.
- Other projects → type `application`, named after `AssemblyName`, with
  project references included as components.

Output is CycloneDX **1.6** JSON. 1.7 is the tool's default, but older
Dependency-Track versions reject it. Build-only packages (SourceLink,
ReproducibleBuilds) stay in the SBOM with `scope: excluded`.

`make sbom` writes SBOMs for all three projects to `build/sbom/`.

### 2. SBOMs embedded in NuGet packages

`Directory.Build.targets` (packable projects only):

- References **Microsoft.Sbom.Targets** with `GenerateSBOM=true`, so
  `dotnet pack` embeds an SPDX 2.2 SBOM at
  `_manifest/spdx_2.2/manifest.spdx.json`. This is Microsoft's convention
  for SBOMs in NuGet packages. It works for local packs too.
- When `SbomDirectory` is set, packs `$(SbomDirectory)/$(PackageId).cdx.json`
  as `sbom/bom.cdx.json`, and fails the pack if that file is missing. It
  doesn't go under `_manifest/` because Microsoft.Sbom.Targets deletes that
  directory before regenerating it.

### 3. Release workflow

**NuGet** (`publish-nuget`): generates the CycloneDX SBOMs, packs with
`SbomDirectory`, then attests each `.nupkg` with its SBOM using
`actions/attest@v4` (`sbom-path`). NuGet.org has no SBOM attachment
mechanism, so the GitHub attestation is what binds an SBOM to the exact
package bytes. Anyone can verify it with `gh attestation verify`.

**Binaries** (`publish-binaries`): each matrix job generates a
RID-specific CycloneDX SBOM (`marv-<rid>.cdx.json`) and attests the binary
with it.

**Container** (`docker`):

- `sbom: true` and `provenance: mode=max` on `docker/build-push-action`.
  BuildKit attaches an SPDX SBOM and SLSA provenance to each platform image.
  This is Docker's native mechanism, read by `docker buildx imagetools
  inspect`, Docker Scout, etc.
- Syft scans each pushed platform image into a CycloneDX 1.6 SBOM
  (`marv-image-linux-<arch>.cdx.json`). Syft reads .NET single-file
  bundles, so the SBOM covers OS packages, the .NET runtime and Marv's NuGet
  dependencies.
- `actions/attest@v4` with `push-to-registry: true` attaches each SBOM as a
  signed in-toto attestation (a Sigstore bundle stored as an OCI referrer)
  to its platform manifest digest. The linux/amd64 SBOM is also attached to
  the index digest; see the design decisions below.

**GitHub Release**: every CycloneDX SBOM (NuGet, binaries, image) is
attached as a release asset.

### 4. CI

`build-and-test` runs `make sbom` and packs `Marv.Core` with
`SbomDirectory`, checking that both SBOMs end up in the package. Tooling
breakage then shows up on PRs instead of at release time.

## Design decisions

- **CycloneDX everywhere, SPDX where it is native.** CycloneDX is the hard
  requirement. SPDX is added only where the platform produces it for free
  (Microsoft.Sbom.Targets for NuGet, BuildKit for images), so two SBOM
  generators don't have to be kept consistent per artifact.
- **`actions/attest` instead of `cosign attest`.** Both produce the same
  thing for a registry: a Sigstore bundle stored as an OCI referrer, which
  sbom-operator reads. `actions/attest` also covers non-registry artifacts
  (nupkg, binaries), records attestations in the repo's Attestations tab,
  and needs no extra tool install. `create-storage-record` is disabled. It
  feeds the org-level "linked artifacts" feature and would need the
  `artifact-metadata` permission.
- **amd64 SBOM on the index digest.** A CycloneDX document describes one
  thing, and an index is a set of platform images, so no single SBOM is
  exactly right for it. The index still needs one, because containerd
  reports the index digest as the pod's `imageID` and sbom-operator looks
  attestations up by that digest. Both platforms come from the same base
  image tag and the same NuGet restore, so they contain the same package
  names and versions. Only the `arch` qualifier in `pkg:deb` purls differs,
  and that doesn't affect vulnerability matching. Each platform digest
  still gets its own exact SBOM.
- **No file listings in the image SBOM.** `SYFT_FILE_METADATA_SELECTION=none`
  cuts the SBOM from ~1.4 MB to ~0.4 MB. Dependency tracking only needs
  packages, and BuildKit's SPDX SBOM still lists files.
- **NuGet attestations cover the pre-signing package.** nuget.org adds a
  repository signature (`.signature.p7s`) to every package it accepts,
  changing the digest, so the attestation never matches the nuget.org
  download directly. The attested `.nupkg` files are attached to the GitHub
  Release. Deleting `.signature.p7s` from the nuget.org copy restores the
  attested bytes exactly (verified against 0.10.0), so that copy can be
  checked too; `docs/releasing.md` has both recipes. Attesting after
  nuget.org signs the package was rejected: it would mean waiting on
  nuget.org validation, which can take minutes to hours.
- **CycloneDX tool as a local tool.** Pinned in `dotnet-tools.json`, so CI
  and developers use the same version and `dotnet tool restore` installs it.
  It needs no global install.
- **Rejected: switching the container to a non-single-file publish** so
  scanners can see `Marv.deps.json`. Not needed: Syft 1.x already
  catalogues NuGet packages inside single-file bundles (verified locally).

## Testing

- `make sbom` produces valid CycloneDX 1.6 for all three projects, with
  correct root component type, name, version and purl.
- `dotnet pack -p:SbomDirectory=build/sbom` embeds both
  `sbom/bom.cdx.json` and `_manifest/spdx_2.2/manifest.spdx.json`. A plain
  `dotnet pack` embeds only the SPDX SBOM. A missing CycloneDX file fails
  the pack with a clear error.
- A Syft scan of the locally built image finds 97 deb and 42 NuGet
  packages, including Sentry, System.CommandLine and YamlDotNet from the
  single-file bundle.
- The platform-enumeration `jq` filter was checked against a public
  multi-arch image with BuildKit attestation manifests.
- The release workflow itself can only be fully exercised by a tagged
  release. After the next release, check it with the verification steps in
  `docs/releasing.md`.

## Impact

- **Plugin API:** No changes.
- **DX:** `make sbom` for local SBOMs. `dotnet pack` gets a few seconds
  slower and logs a Microsoft.Sbom.Targets summary line.
- **Risk:** Low for the library and app. All changes are packaging and CI.
  The main risk is a release-workflow failure in the new steps, mitigated
  by the CI smoke test for the .NET side.
