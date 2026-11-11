#!/usr/bin/env bash
# Generates a CycloneDX (JSON) SBOM for a .NET project from its NuGet
# dependency graph, using the CycloneDX .NET tool pinned in dotnet-tools.json.
#
# Usage: ./scripts/generate-sbom.sh <project.csproj> <output-file> [rid]
#
#   project.csproj  The project to describe.
#   output-file     Where to write the SBOM (e.g. build/sbom/Marv.cdx.json).
#   rid             Optional runtime identifier (e.g. linux-x64). Pass it for
#                   RID-specific builds so the SBOM matches what was published.
#
# Environment:
#   VERSION         Version to record in the SBOM. Defaults to the project's
#                   <Version> (from Directory.Build.props).
#
# The component name, type and purl are derived from the project:
#   - Packable projects are recorded as a "library" named after their
#     PackageId, with a pkg:nuget purl so they match the published package.
#   - Everything else is recorded as an "application" named after its
#     AssemblyName.
#
# Build-only dependencies (SourceLink, ReproducibleBuilds, ...) are kept in
# the SBOM with scope "excluded", which is how CycloneDX marks dependencies
# that are not part of the shipped artifact.

set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
    echo "Usage: $0 <project.csproj> <output-file> [rid]" >&2
    exit 1
fi

PROJECT=$1
OUTPUT=$2
RID=${3:-}

REPO_ROOT=$(git rev-parse --show-toplevel)
cd "$REPO_ROOT"

if [[ ! -f "$PROJECT" ]]; then
    echo "Error: project '$PROJECT' not found." >&2
    exit 1
fi

get_property() {
    dotnet msbuild "$PROJECT" -nologo -getProperty:"$1"
}

VERSION=${VERSION:-$(get_property Version)}
IS_PACKABLE=$(get_property IsPackable)

CDX_ARGS=(--output-format Json --spec-version 1.6 --set-version "$VERSION")

if [[ "$IS_PACKABLE" == "true" ]]; then
    CDX_ARGS+=(--set-name "$(get_property PackageId)" --set-type Library --set-nuget-purl)
else
    CDX_ARGS+=(--set-name "$(get_property AssemblyName)" --set-type Application)
    # Include Marv.Core etc. as components rather than only their packages.
    CDX_ARGS+=(--include-project-references)
fi

if [[ -n "$RID" ]]; then
    CDX_ARGS+=(--runtime "$RID")
fi

OUTPUT_DIR=$(dirname "$OUTPUT")
mkdir -p "$OUTPUT_DIR"

dotnet tool restore >/dev/null
dotnet CycloneDX "$PROJECT" "${CDX_ARGS[@]}" \
    --output "$OUTPUT_DIR" --filename "$(basename "$OUTPUT")"
