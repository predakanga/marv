# Change Specifications

Actionable change specifications derived from downstream plugin project
feedback (`docs/downstream_suggestions/`). Each spec is a self-contained
document describing a single change or closely related group of changes.

## Index

| # | Spec | Scope | Complexity | Dependencies |
|---|---|---|---|---|
| 1 | [Command Prefix Configuration](001-command-prefix-config.md) | Core | Small | None |
| 2 | [Handler Dispatch Filters](002-handler-dispatch-filters.md) | Core | Small | None |
| 3 | [Handler Filter Pipeline](003-handler-filter-pipeline.md) | Core | Medium | #2 |
| 4 | [IHttpClientFactory Registration](004-httpclient-registration.md) | Host | Trivial | None |
| 5 | [Bulk Channel Join](005-bulk-channel-join.md) | Core | Small-Medium | None |
| 6 | [Plugin API Documentation](006-plugin-api-documentation.md) | Docs | Medium | None |
| 7 | [Test Infrastructure](007-test-infrastructure.md) | New package | Medium | None |
| 8 | [Example Plugin](008-example-plugin.md) | Examples | Medium | #2, #3 |

Specs 1-5 are code changes. Specs 6-8 are documentation/DX changes.

Recommended implementation order: 1, 4, 2, 5, 3, 7, 6, 8.
