# Change Specifications

Actionable change specifications derived from downstream plugin project
feedback (`docs/downstream_suggestions/`). Each spec is a self-contained
document describing a single change or closely related group of changes.

Specs are numbered in recommended implementation order.

## Index

| # | Spec | Scope | Complexity | Dependencies | Status |
|---|---|---|---|---|---|
| 1 | [Command Prefix Configuration](001-command-prefix-config.md) | Core | Small | None | **Done** |
| 2 | [IHttpClientFactory Registration](002-httpclient-registration.md) | Host | Trivial | None | **Done** |
| 3 | [Handler Dispatch Filters](003-handler-dispatch-filters.md) | Core | Small | None | **Done** |
| 4 | [Bulk Channel Join](004-bulk-channel-join.md) | Core | Small-Medium | None | |
| 5 | [Handler Filter Pipeline](005-handler-filter-pipeline.md) | Core | Medium | #3 | |
| 6 | [Test Infrastructure](006-test-infrastructure.md) | New package | Medium | None | |
| 7 | [Plugin API Documentation](007-plugin-api-documentation.md) | Docs | Medium | None | |
| 8 | [Example Plugin](008-example-plugin.md) | Examples | Medium | #3, #5 | |

Specs 1-5 are code changes. Specs 6-8 are documentation/DX changes.
