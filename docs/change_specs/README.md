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
| 4 | [Bulk Channel Join](004-bulk-channel-join.md) | Core | Small-Medium | None | **Done** |
| 5 | [Handler Filter Pipeline](005-handler-filter-pipeline.md) | Core | Medium | #3 | **Done** |
| 6 | [Test Infrastructure](006-test-infrastructure.md) | New package | Medium | None | **Done** |
| 7 | [Plugin API Documentation](007-plugin-api-documentation.md) | Docs | Medium | None | **Done** |
| 8 | [Example Plugin](008-example-plugin.md) | Examples | Medium | #3, #5 | **Done** |
| 9 | [Bot Action Methods](009-bot-action-methods.md) | Core | Small-Medium | None | **Done** |
| 10 | [Case Mapping for Plugins](010-casemapping-for-plugins.md) | Core | Small | None | **Done** |
| 11 | [Plugin Loading Robustness](011-plugin-loading-robustness.md) | Core | Medium-Large | None | **Done** |

| 12 | [Dockerfile Sample Plugin Inclusion](012-dockerfile-sample-plugins.md) | Dockerfile | Small | None | **Done** |
| 13 | [Docker Action Build Caching](013-docker-action-caching.md) | CI/CD | Small | None | **Done** |
| 14 | [Release Notes from CHANGELOG.md](014-release-notes-from-changelog.md) | CI/CD | Small | None | **Done** |
| 15 | [Regex Options for OnRegex](015-onregex-options.md) | Core | Small | None | **Done** |
| 16 | [Extract BatchChannels to Utility](016-batch-channels-utility.md) | Core | Trivial | None | **Done** |
| 17 | [Common HandlerContext Base Class](017-common-handler-context.md) | Core | Small-Medium | None | **Done** |
| 18 | [Remove PluginType from HandlerGroup](018-remove-handlergroup-plugintype.md) | Core | Small | None | **Done** |
| 19 | [JSON5 Configuration Parser](019-json5-config-parser.md) | Host | Small-Medium | None | **Done** |

| 20 | [Bot Statistics Property](020-bot-statistics.md) | Core | Medium | None | **Pending** |
| 21 | [Message Queue Management](021-message-queue-management.md) | Core | Small | None | **Pending** |
| 22 | [Live Config Reload & IOptions Docs](022-plugin-api-ioptions-docs.md) | Host + Docs | Small | None | **Pending** |
| 23 | [CTCP VERSION Response Override](023-ctcp-version-override.md) | Core | Small | None | **Pending** |
| 24 | [SendAndAwaitAsync ENDOF* Fallback](024-sendandawait-endof-fallback.md) | Core | Medium | None | **Pending** |
| 25 | [Testcontainers Integration](025-testcontainers-integration.md) | Tests | Small-Medium | None | **Done** |

Specs 1-5, 9-10 are code changes. Specs 6-8 are documentation/DX changes.
Spec 11 is a robustness/DX improvement to the plugin loading pipeline.
Specs 12-14 are CI/CD and infrastructure changes. Specs 15-19 are code changes.
Specs 20-24 are downstream feature requests (code and docs).
