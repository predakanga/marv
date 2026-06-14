# CS-030: Update GitHub Actions to Node.js 24 — COMPLETED

**Source:** GitHub issue #6
**Scope:** CI/CD
**Complexity:** Small
**Breaking changes:** None
**Status:** Completed

---

## Problem

All GitHub Actions used in both the CI and Release workflows are running on
Node.js 20, which is being deprecated. GitHub will force actions to run on
Node.js 24 starting June 16, 2026, and will remove Node.js 20 entirely on
September 16, 2026. Every job in both workflows currently emits a deprecation
warning.

## Changes

### 1. Audit and update actions in `ci.yml`

The following actions are flagged in the CI workflow:

| Action | Current | Target | Notes |
|--------|---------|--------|-------|
| `actions/checkout` | `@v4` | `@v5` or latest with Node 24 | Check for v5 release |
| `actions/cache` | `@v4` | `@v5` or latest with Node 24 | Check for v5 release |
| `actions/setup-dotnet` | `@v4` | `@v5` or latest with Node 24 | Check for v5 release |

### 2. Audit and update actions in `release.yml`

The following additional actions are flagged in the Release workflow:

| Action | Current | Target | Notes |
|--------|---------|--------|-------|
| `actions/upload-artifact` | `@v4` | `@v5` or latest with Node 24 | Check for v5 release |
| `actions/download-artifact` | `@v4` | `@v5` or latest with Node 24 | Must match upload-artifact version |
| `docker/login-action` | `@v3` | `@v4` or latest with Node 24 | Check for v4 release |
| `docker/setup-buildx-action` | `@v3` | `@v4` or latest with Node 24 | Check for v4 release |
| `docker/build-push-action` | `@v6` | `@v7` or latest with Node 24 | Check for v7 release |
| `softprops/action-gh-release` | `@v2` | `@v3` or alternative | Also has `Buffer()` deprecation; consider switching to `gh release create` via CLI if no Node 24 version exists |

### 3. Verify updated actions

After updating, trigger a CI run and verify:
- All jobs pass without Node.js deprecation warnings.
- No `Buffer()` deprecation warnings in the github-release job.
- Artifact upload/download still works correctly.
- Docker build and push still works.

### 4. Fallback: `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24`

If any action does not yet have a Node.js 24-compatible release, set
`FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` as an environment variable at
the workflow level as a temporary measure, and document which actions
still need updating.

## Design decisions

- **Prefer bumping major versions** over using the `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24`
  env var — the env var is a stopgap that may mask compatibility issues.
- **Replace `softprops/action-gh-release`** with the `gh` CLI if no
  Node 24-compatible version is available — this removes a third-party
  dependency and the `Buffer()` deprecation warning.
- **Match upload-artifact and download-artifact versions** — these must
  be compatible with each other to ensure artifact transfer works.

## Testing

- Run the CI workflow and verify all jobs pass without deprecation warnings.
- Run a test release (or inspect the workflow YAML for correctness) to
  verify the release workflow changes.
- Confirm Docker image builds and pushes succeed.
- Confirm GitHub Release creation with binary artifacts works.

## Impact

- **Plugin API:** No changes.
- **DX:** CI/CD warnings are eliminated, reducing noise in workflow logs.
- **Risk:** Low — action version bumps are well-tested by the community.
  The main risk is an action removing or changing a feature between major
  versions, but this is unlikely for the standard actions used here.
