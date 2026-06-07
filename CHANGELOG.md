# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.2] - 2026-06-07

### Fixed

- Plugin metadata scanner failing to resolve `Marv.Core` assembly in
  framework-dependent deployments — `AppContext.BaseDirectory` is now
  included in the resolver's search paths

## [0.3.1] - 2026-06-07

### Fixed

- Plugin loading failing with `FileNotFoundException` from
  `MetadataLoadContext` when scanning plugin assemblies — the metadata
  resolver now includes runtime assemblies so the core assembly can be found
- `--log-level` CLI option not applying to bootstrap logging during plugin
  discovery, causing scan warnings/errors to be hidden
- Plugin metadata scanner failing to resolve `Marv.Core` assembly when plugin
  DLLs are in a separate directory from the application

## [0.3.0] - 2026-06-07

### Added

- `Options` property on `[OnRegex]` attribute for specifying `RegexOptions`
  (e.g., `IgnoreCase`, `IgnorePatternWhitespace`)
- `IrcUtils.BatchChannels` public utility for batching channel lists within
  the IRC line length limit
- `HandlerContext` abstract base class shared by `CommandContext` and
  `RegexMatchContext`, providing common `Sender`, `Channel`, `Bot`,
  `RawMessage`, `IsDirect`, and `ReplyAsync` members
- JSON5 support for configuration files — comments, trailing commas, and
  other JSON5 features are now supported in `.json` and `.json5` config files
- Docker layer caching (`type=gha`) for release Docker builds
- NuGet package caching in CI and release workflows
- GitHub release notes are now extracted from CHANGELOG.md instead of
  auto-generated from commit history

### Changed

- `[HandlerGroup]` attribute no longer requires a `Type pluginType`
  argument — handler groups are discovered automatically in the plugin's
  assembly

- Docker image no longer includes sample plugins — users add plugins by
  creating a derived image (`FROM ghcr.io/predakanga/marv:latest`)
- Binary release archives no longer include sample plugins

- Plugin loading failures are now fatal — the bot will not start if any
  requested plugin cannot be loaded, instead of silently running in a degraded
  state
- Plugin name resolution now supports assembly filename convention matching
  (e.g., `CannedResponses` matches `Marv.Plugins.CannedResponses.dll`)
- Non-plugin DLLs in plugin directories are no longer eagerly loaded; only
  assemblies containing an `IPlugin` implementation are loaded into the runtime
- Assembly resolver now also probes `AppContext.BaseDirectory` for
  `PublishSingleFile` support

### Fixed

- Plugin assembly dependency resolution now probes configured plugin directories
  for transitive managed and native dependencies, fixing `FileNotFoundException`
  when plugins depend on libraries not in Marv's own dependency graph
- Plugin dependency sorter no longer throws for constructor parameters that are
  not declared via `[ProvidesService]` by any loaded plugin (e.g.
  `IHttpClientFactory`), treating them as framework-provided DI services instead
- Duplicate plugin directories in config no longer cause plugins to load twice,
  preventing duplicate handler registrations and `[ProvidesService]` conflicts
- Unmatched plugin names in config now produce a fatal error with "did you mean?"
  suggestions listing available plugins, instead of being silently ignored
- Collection config properties (`Channels`, `PluginDirectories`, `Plugins`)
  no longer double their values when multiple configuration layers are merged;
  each layer now overwrites the previous value instead of appending

## [0.2.0] - 2026-06-06

### Added

- Command prefix is now configurable via `CommandPrefix` in bot configuration
  (previously hardcoded to `!`), with support for multi-character prefixes
- `IBot.CommandPrefix` property exposes the configured prefix to plugins
- `[OnCommand]` attribute gains a `Prefix` property to override the bot-wide
  prefix on a per-handler basis (e.g. `[OnCommand("invite", Prefix = ".")]`)
- `IHttpClientFactory` is now registered by default, so plugins can inject it
  without adding the `Microsoft.Extensions.Http` package themselves
- `[OnCommand]` and `[OnRegex]` attributes gain `ChannelOnly`, `DirectOnly`,
  and `Channel` filter properties for declarative message context filtering
- Bot automatically sets bot user mode (e.g. `+B`) when the server advertises
  the `BOT` ISUPPORT token
- `UserModes` config option to set additional user modes after authentication
  (e.g. `"+ix"`), applied before the ready signal
- Server MOTD is now available via `IServerInfo.Motd` after connection
- `IBot.JoinMultipleAsync` sends a single comma-separated `JOIN` command per
  RFC 2812, with automatic batching to stay within the 512-byte line limit
- Handler filter pipeline: plugins can intercept handler invocations via
  `FilterHandlerAsync` override or declarative `IFilteringAttribute` +
  `IFilterEvaluator` pairs for cross-cutting concerns like authorization,
  rate limiting, and auditing
- New `Marv.Testing` package with fluent builders for `CommandContext`,
  `RegexMatchContext`, and events, plus `PluginTestHarness<T>` for
  creating plugin instances with mocked dependencies in ~2 lines
- `docs/PLUGIN_API.md` — consolidated plugin API reference covering all
  handler attributes, context types, events, IBot, formatting, configuration,
  services, handler groups, filters, and testing patterns
- `docs/PLUGIN_PROJECT_CLAUDE.md` — CLAUDE.md template for downstream
  plugin projects
- Old `plugin-api-draft.md` archived to `docs/archive/` with redirect
- `IBot` gains convenience methods for common IRC actions: `KickAsync`,
  `SetTopicAsync`, `InviteAsync`, `SetModeAsync`, `GiveOpAsync`,
  `RemoveOpAsync`, `GiveVoiceAsync`, `RemoveVoiceAsync`, `ChangeNickAsync`
- `IBot.CaseComparer` exposes the server's IRC case mapping as an
  `IEqualityComparer<string>` for correct nick/channel comparisons
- New `Marv.Plugins.Moderation` example plugin demonstrating advanced API
  patterns: typed configuration, declarative auth filters, event handling,
  interval timers, bot action methods, case-mapped collections, handler groups,
  raw message handling, and `SendAndAwaitAsync`

### Changed

- Initial channel join on connect now uses bulk `JOIN` instead of individual
  commands per channel, reducing startup traffic and rate-limit pressure

- Updated Microsoft.Extensions.* packages from 10.0.0-preview.4 to 10.0.8 (stable)
- Updated Microsoft.NET.Test.Sdk from 17.14.0 to 18.6.0
- Updated xunit.runner.visualstudio from 3.1.0 to 3.1.5

### Removed

- Docker image build from CI workflow — Docker images are now only built during
  tagged releases

### Fixed

- Boolean CLI options (e.g. `--use-tls`, `--rate-limit-enabled`) no longer
  override config file values when not explicitly provided on the command line
- Nullable string config properties (e.g. `TlsCaCertFile`, `SaslUser`) no
  longer become empty strings when set to `null` in JSON config files
- Dockerfile missing `.csproj` entries for Moderation plugin, Marv.Testing,
  and Marv.Testing.Tests, causing `dotnet restore` to fail

## [0.1.0] - 2026-06-01

### Added

- IRC client with IRCv3 capability negotiation (SASL, multi-prefix, message-tags, labeled-response, and more)
- Plugin system with dependency injection, inter-plugin services, and typed configuration
- Bundled plugins: Greet, Auth, AuthConsumer, CannedResponses
- Configuration layering: JSON/YAML/XML files, environment variables, CLI arguments
- Outbound rate limiting with configurable token bucket
- TLS support with optional certificate validation skip and custom CA certificate
- SASL PLAIN authentication and NickServ/OPER authentication with configurable timeout
- Optional Sentry error reporting
- Dockerfile for containerized deployment
- CI/CD with GitHub Actions (build, test, lint, static analysis, security, integration tests, Docker)
- Release workflow producing cross-platform binaries and multi-arch Docker images

[0.3.2]: https://github.com/predakanga/marv/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/predakanga/marv/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/predakanga/marv/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/predakanga/marv/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/predakanga/marv/releases/tag/v0.1.0
