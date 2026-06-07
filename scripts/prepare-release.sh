#!/usr/bin/env bash
# Prepares a release: bumps version, updates CHANGELOG.md, commits, and tags.
#
# Usage: ./scripts/prepare-release.sh [major|minor|patch]
#
# If no argument is given, prompts interactively for the bump type.
# The [Unreleased] section in CHANGELOG.md must have content to release.

set -euo pipefail

# Portable in-place sed (macOS sed requires a suffix with -i)
sedi() { sed -i'' -e "$@"; }

REPO_ROOT=$(git rev-parse --show-toplevel)
cd "$REPO_ROOT"

# --- Validate preconditions ---

if [[ -n $(git status --porcelain) ]]; then
    echo "Error: working tree is not clean. Commit or stash changes first." >&2
    exit 1
fi

UNRELEASED_CONTENT=$(sed -n '/^## \[Unreleased\]/,/^## \[/{ /^## \[/d; /^$/d; p; }' CHANGELOG.md)
if [[ -z "$UNRELEASED_CONTENT" ]]; then
    echo "Error: no content under [Unreleased] in CHANGELOG.md." >&2
    exit 1
fi

# --- Read current version ---

CURRENT=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' Directory.Build.props)
IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT"

NEXT_MAJOR="$((MAJOR + 1)).0.0"
NEXT_MINOR="${MAJOR}.$((MINOR + 1)).0"
NEXT_PATCH="${MAJOR}.${MINOR}.$((PATCH + 1))"

# --- Determine bump type ---

bump_type="${1:-}"

if [[ -z "$bump_type" ]]; then
    echo "Current version: ${CURRENT}"
    echo ""
    echo "  1) patch  → ${NEXT_PATCH}"
    echo "  2) minor  → ${NEXT_MINOR}"
    echo "  3) major  → ${NEXT_MAJOR}"
    echo ""
    read -rp "Bump type [1/2/3]: " choice
    case "$choice" in
        1|patch) bump_type=patch ;;
        2|minor) bump_type=minor ;;
        3|major) bump_type=major ;;
        *) echo "Invalid choice." >&2; exit 1 ;;
    esac
fi

case "$bump_type" in
    patch) VERSION="$NEXT_PATCH" ;;
    minor) VERSION="$NEXT_MINOR" ;;
    major) VERSION="$NEXT_MAJOR" ;;
    *) echo "Error: argument must be 'major', 'minor', or 'patch'." >&2; exit 1 ;;
esac

TAG="v${VERSION}"
DATE=$(date +%Y-%m-%d)

if git rev-parse "$TAG" >/dev/null 2>&1; then
    echo "Error: tag ${TAG} already exists." >&2
    exit 1
fi

echo "Releasing ${TAG}..."

# --- Update files ---

sedi "s|<Version>[^<]*</Version>|<Version>${VERSION}</Version>|" Directory.Build.props

sedi "s/^## \[Unreleased\]$/## [Unreleased]\n\n## [${VERSION}] - ${DATE}/" CHANGELOG.md

PREV_TAG=$(grep -o '^\[[0-9][0-9.]*\]' CHANGELOG.md | head -2 | tail -1 | tr -d '[]')
sedi "/^\[${PREV_TAG}\]:/ i\\[${VERSION}]: https://github.com/predakanga/marv/compare/v${PREV_TAG}...v${VERSION}" CHANGELOG.md

# --- Commit and tag ---

git add Directory.Build.props CHANGELOG.md
git commit -m "Prepare ${TAG} release"
git tag -a "$TAG" -m "$TAG"

echo ""
echo "Release ${TAG} prepared. Push with:"
echo "  git push --follow-tags"
