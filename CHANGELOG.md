# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Command prefix is now configurable via `CommandPrefix` in bot configuration
  (previously hardcoded to `!`), with support for multi-character prefixes
- `IBot.CommandPrefix` property exposes the configured prefix to plugins
- `[OnCommand]` attribute gains a `Prefix` property to override the bot-wide
  prefix on a per-handler basis (e.g. `[OnCommand("invite", Prefix = ".")]`)
- Bot automatically sets bot user mode (e.g. `+B`) when the server advertises
  the `BOT` ISUPPORT token
- `UserModes` config option to set additional user modes after authentication
  (e.g. `"+ix"`), applied before the ready signal
- Server MOTD is now available via `IServerInfo.Motd` after connection

### Fixed

- Boolean CLI options (e.g. `--use-tls`, `--rate-limit-enabled`) no longer
  override config file values when not explicitly provided on the command line
- Nullable string config properties (e.g. `TlsCaCertFile`, `SaslUser`) no
  longer become empty strings when set to `null` in JSON config files

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

[0.1.0]: https://github.com/predakanga/marv/releases/tag/v0.1.0
