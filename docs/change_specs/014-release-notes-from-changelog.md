# CS-014: Generate Release Notes from CHANGELOG.md — COMPLETED

**Source:** `TODO.md` item 3
**Scope:** CI/CD (`.github/workflows/release.yml`)
**Complexity:** Small
**Breaking changes:** None
**Status:** Completed

---

## Problem

The release workflow uses `softprops/action-gh-release` with
`generate_release_notes: true`, which generates release notes from commit
messages and PR titles between the previous and current tag. This produces
noisy, developer-facing notes that duplicate information already
maintained in `CHANGELOG.md`. The changelog follows Keep a Changelog
conventions and contains curated, user-facing descriptions of changes.

The GitHub release notes should come from the changelog, not from raw
commit history.

## Decisions

- Extract the `[Unreleased]` or version-specific section from
  `CHANGELOG.md` and use it as the GitHub release body.
- Remove `generate_release_notes: true` from the release action.
- Parsing should handle both `[Unreleased]` (for pre-release) and
  `[x.y.z]` version headings.
- If the changelog section is empty or missing, fall back to a brief
  note directing users to the full changelog.

## Changes

### 1. Add a changelog extraction step

In the `github-release` job, add a step before the release creation that
extracts the relevant section from `CHANGELOG.md`:

```yaml
- name: Extract release notes from CHANGELOG.md
  id: changelog
  run: |
    VERSION="${GITHUB_REF_NAME#v}"
    # Extract the section for this version, or [Unreleased] as fallback
    NOTES=$(awk -v ver="$VERSION" '
      /^## \[/ {
        if (found) exit
        if ($0 ~ "\\[" ver "\\]" || (ver == "" && $0 ~ "\\[Unreleased\\]")) {
          found=1
          next
        }
      }
      found { print }
    ' CHANGELOG.md)

    if [ -z "$NOTES" ]; then
      NOTES="See [CHANGELOG.md](CHANGELOG.md) for details."
    fi

    # Write to file for multiline output
    echo "$NOTES" > release-notes.md
```

### 2. Update the release action

Replace the `generate_release_notes` flag with a `body_path` pointing to
the extracted notes:

```yaml
- name: Create GitHub Release
  uses: softprops/action-gh-release@v2
  with:
    body_path: release-notes.md
    files: artifacts/**/*.tar.gz
```

### 3. CHANGELOG.md workflow guidance

When cutting a release, the release process should:

1. Rename `[Unreleased]` to `[x.y.z] - YYYY-MM-DD` in `CHANGELOG.md`.
2. Add a new empty `[Unreleased]` section above it.
3. Tag the commit.

The extraction script matches by version number, so the rename must happen
before tagging. This is standard Keep a Changelog workflow and does not
require tooling changes.

## Design decisions

**Why `awk` instead of a dedicated changelog parser?** The changelog
follows a well-defined format (headings delimited by `## [...]`). A
simple `awk` script is portable, has no dependencies, and is easy to
debug. A dedicated tool like `changelog-parser` would add a dependency
for marginal benefit.

**Why write to a file instead of using `body` directly?** The release
notes may contain multiline text, special characters, and markdown
formatting. Writing to a file and using `body_path` avoids shell escaping
issues that arise with inline `body` in YAML.

## Impact

- **Release notes:** GitHub releases show curated, user-facing notes from
  the changelog instead of raw commit history.
- **Workflow:** Release process requires `CHANGELOG.md` to be updated
  before tagging, which is already the project convention per `CLAUDE.md`.
- **Plugin API:** No changes.
