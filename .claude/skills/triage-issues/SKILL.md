---
name: triage-issues
description: >-
  Triage open GitHub issues — fetch, analyse, and interactively route each
  issue to investigation, spec creation, direct implementation, or rejection.
  Use when the user asks to triage issues, process issues, or check GitHub issues.
disable-model-invocation: true
---

# Triage GitHub Issues

Fetch open GitHub issues, analyse them against the codebase, and present
them to the user in the TUI for interactive routing. Discussion happens
here in the conversation — GitHub receives only a terse record of
decisions made.

## When to use

Invoke with `/triage-issues` when you want to process open GitHub issues.

## Workflow

### 1. Fetch and filter open issues

Run:

```
gh issue list --repo predakanga/marv --state open --json number,title,body,labels,comments --limit 50
```

Filter out issues that already have a label `triaged`, `rejected`, or
`wontfix`.

### 2. Analyse and present

For each remaining issue:

1. Fetch details with `gh issue view <NUMBER> --repo predakanga/marv --json number,title,body,labels,comments`.
2. Read the issue description and any comments.
3. Read relevant source files mentioned or implied by the issue.
4. Check `docs/change_specs/README.md` for overlapping or existing
   pending specs.
5. Assess the issue type (bug, feature, trivial fix) and complexity.

Present a summary table to the user:

```
## Open Issues

| # | Title | Type | Complexity | Notes |
|---|-------|------|------------|-------|
| #7 | UserMod enable no response | Bug | Small | No existing spec |
| #12 | Add !weather command | Feature | Medium | — |
| #15 | Typo in help text | Trivial | Trivial | — |
```

Then ask: **"Which issue would you like to work on?"**

### 3. Present issue detail and routing options

When the user selects an issue, present the full analysis:

- Issue description (summarised)
- Affected files / areas of the codebase
- Whether an existing change spec covers this issue
- Your assessment of type and complexity

Then present the routing options:

1. **Investigate** — dig into the code, run tests, identify root cause,
   report findings. Best for bugs or issues where the scope is unclear.
2. **Spec** — create a change specification. Best for features or
   complex changes that need design work.
3. **Implement** — fix it directly, no spec needed. Best for trivial or
   obvious changes.
4. **Reject** — decline with a reason.
5. **Skip** — move to the next issue.

Include a recommendation based on your analysis (e.g. "This looks like
a bug — I'd recommend investigating first"), but let the user decide.

Wait for the user to choose before proceeding.

### 4. Path: Investigate

1. Read all source files relevant to the issue.
2. Run targeted tests if applicable (e.g. `dotnet test --filter`).
3. Trace the code path to identify the root cause or confirm the
   reported behaviour.
4. Present findings concisely:
   - Root cause (or best hypothesis)
   - Affected files and line ranges
   - Whether the fix is obvious or needs design work
   - Whether existing tests cover the area
5. Re-offer routing: "Based on this, I'd suggest [Implement / Spec].
   What would you like to do?"

The user may choose Implement, Spec, Reject, or Skip at this point.

### 5. Path: Create change specification

1. Determine the next CS number from `docs/change_specs/README.md`.
2. Write the spec file following the template in the CLAUDE.md change
   specification section. Include a `**Source:** GitHub issue #N`
   metadata line.
3. Update `docs/change_specs/README.md` with the new entry (status:
   **Pending**).
4. Present the spec to the user in the TUI for review.
5. If the user requests changes, edit the spec and re-present. Iterate
   until the user is satisfied.
6. Commit per CLAUDE.md mandatory instructions.

Do NOT write any implementation code in this path — specs only.

### 6. Path: Implement directly

1. State what the fix will be and get confirmation from the user.
2. Implement the change.
3. Run `dotnet build` and `dotnet test` to verify.
4. Present the diff to the user.
5. Commit per CLAUDE.md mandatory instructions.

### 7. Path: Reject

1. Ask the user for a brief reason (or accept the reason if they
   already provided one).
2. Post a comment on the issue: `Declined: <reason>`
3. Add the `rejected` label:
   ```
   gh issue edit <NUMBER> --repo predakanga/marv --add-label rejected
   ```

### 8. Post GitHub summary

After each issue is processed (not skipped), post a terse record to
GitHub. This is a decision record, not a discussion — keep it short.

| Outcome | Label | GitHub comment |
|---------|-------|----------------|
| Investigated (not yet resolved) | `investigated` | Short summary of findings (see template below) |
| Spec created | `triaged` | `Change specification CS-NNN created from this issue. See docs/change_specs/NNN-slug.md.` |
| Implemented directly | `triaged` | None needed — the commit with `Closes #N` handles it |
| Rejected | `rejected` | `Declined: <reason>` |

Investigation summary template:

```
## Investigation Summary

**Root cause:** <one-line description>
**Affected:** <file list>
**Fix complexity:** <Trivial / Small / Medium>

<2-3 sentence summary of findings>

*Investigated via Claude Code.*
```

Apply labels with:

```
gh issue edit <NUMBER> --repo predakanga/marv --add-label <label>
```

### 9. Continue or finish

Ask the user if they want to process another issue from the list, or
stop. If stopping, follow the mandatory instructions from CLAUDE.md
(commit).

## Important notes

- This skill presents analysis and recommendations, but the **user
  makes all routing decisions**. Never proceed to a path without the
  user choosing it.
- All discussion happens in the TUI. GitHub comments are terse records
  of decisions, not part of the conversation.
- Paths are fluid — Investigate can lead to Implement or Spec, and Spec
  can lead to Implement. Handle transitions naturally without forcing
  the user to re-invoke the skill.
- If an issue already has a pending change spec, mention this in the
  analysis and suggest implementing the existing spec rather than
  creating a duplicate.
- When analysing feasibility, read relevant source files — don't guess.
- The Implement path requires `dotnet build` and `dotnet test` to pass
  before presenting results.
- When committing work that resolves a GitHub issue (whether via the
  Implement path or a later spec implementation), include `Closes #N`
  in the commit message body.
- If `gh` commands fail with auth errors, ask the user to run
  `! gh auth login` in their terminal.
