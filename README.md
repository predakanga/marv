# Marv

An IRC bot built on .NET 10 with a plugin-based architecture and IRCv3 support.

Marv connects to a single IRC network and provides extensible functionality through plugins. It supports SASL authentication, IRCv3 capability negotiation, and IRC operator commands out of the box.

## AI Disclaimer

This project was created to experiment with AI-based development; it was designed and coded almost entirely with Anthropic Opus 4.6 via Claude Code, and the prompts logged in [docs/prompts.md](/docs/prompts.md).

## Features

- IRCv3 capability negotiation (SASL, multi-prefix, message-tags, and more)
- Plugin system with dependency injection and inter-plugin services
- Multiple configuration formats (JSON, YAML, XML)
- Configuration layering: file, environment variables, CLI arguments
- Outbound rate limiting with configurable token bucket
- Optional Sentry error reporting
- Bundled plugins: Greet, Auth, AuthConsumer, CannedResponses

## Quick start

### From a release binary

Download the latest release for your platform from the [Releases](/releases) page, extract it, and run:

```bash
cp marv.example.json marv.json
# Edit marv.json with your server details
./Marv --config marv.json
```

### With Docker

```bash
docker run --rm -v ./marv.json:/app/marv.json ghcr.io/predakanga/marv:latest
```


### From source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
make publish
build/output/Marv --config marv.json
```

## Configuration

Marv loads configuration from multiple sources, in priority order (highest wins):

1. CLI arguments (e.g. `--server irc.libera.chat`)
2. Environment variables with `MARV_` prefix (e.g. `MARV_SERVER=irc.libera.chat`)
3. Configuration file (default: `marv.json`, override with `--config`)

See [`marv.example.json`](marv.example.json) for all available options.

### Key options

| Option | Description | Default |
|---|---|---|
| `Server` | IRC server hostname | *(required)* |
| `Port` | IRC server port | `6697` |
| `UseTls` | Use TLS | `true` |
| `Nick` | Bot nickname | *(required)* |
| `Channels` | Channels to join on connect | `[]` |
| `CommandPrefix` | Prefix for plugin commands | `!` |
| `PluginDirectories` | Directories to scan for plugins | `["plugins"]` |
| `Plugins` | Plugin names to load | `[]` |

## Plugins

Plugins are .NET assemblies placed in a plugin directory. Each plugin is a class that extends `MarvPlugin` and is discovered by name.

### Bundled plugins

- **Greet** — Welcomes users when they join a channel
- **Auth** — Provides an authorization service based on IRC account names
- **AuthConsumer** — Example plugin demonstrating how to consume the Auth service
- **CannedResponses** — Responds to messages matching configured patterns

### Plugin configuration

Plugins are configured under `Plugins:<Name>` in the config file:

```json
{
  "Plugins": ["Greet"],
  "Plugins:Greet": {
    "GreetMessage": "Welcome, {nick}!",
    "GreetOnJoin": true
  }
}
```

## Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://docs.docker.com/get-docker/) (for integration tests)

### Build and test

```bash
make build       # Build all projects
make test        # Run unit tests
```

### Integration tests

Integration tests run against an ngircd IRC server in a Docker container:

```bash
make test-integration   # Starts IRC server, runs tests, stops server
```

You can also manage the IRC server independently:

```bash
make ircd-start    # Start the IRC server container
make ircd-stop     # Stop the IRC server container
```

### Project structure

```
src/
  Marv/             # CLI application (entry point)
  Marv.Core/        # Core library (bot logic, plugin system, IRC client)
  plugins/          # Bundled plugins
tests/
  Marv.Core.Tests/  # Unit and integration tests for core
  Marv.Plugins.Tests/ # Tests for bundled plugins
docs/
  adr/              # Architecture Decision Records
  releasing.md      # Release runbook
```

## License

See the [LICENSE](LICENSE) file for details.
