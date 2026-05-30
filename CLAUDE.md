# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Mandatory instructions

- Log all prompts to docs/prompts.md, with a summary of the prompt as the header.
- After each task, commit the current working tree to git. Only commit,
  never push.

# IRC Bot — Claude Code Instructions

## Project overview

This project is a C# IRC bot called Marv targetting .NET 10.

The goal of this project is to provide a robust IRC bot capable of interfacing
with all major IRC servers/networks, and utilizing IRCv3 features to create a
seamless experience for developers and users.

The bot will only support connection to a single IRC network at a time, and is
only concerned with real-time communications; no effort will be taken to handle
historic chat messages through features like PLAYBACK, nor will features such
as DCC be supported.

## Design goals

- Maintenance - Assume that a human will be maintaining the project without
  AI assistance. Comments, documentation and tests should be robust and clear.
- Modularity - While the core of Marv will implement some basic logic, setup
  and common functionality, most functionality will be implemented as plugins.
- Developer Experience (DX) - Because most functionality will live in plugins,
  DX is a priority; care should be taken to make plugin authoring as simple as
  possible and commonly used code should be provided by the core component.
  Debugging and trace logging are also a key component of DX.

## Specific architecture goals

- There will be a command-line application which serves as the user interface
  to Marv. This will read the config, initialize logging, validate and load
  plugins and run the bot's main loop.
  In addition to file-based configuration, the app will support configuration
  by environment variable and command-line arguments.
- There will be a core assembly containing the bot's core logic (connecting to
  the IRC server, negotiating capabilities, authenticating to IRC services,
  managing oper state and joining initial channels) and plugin management.
- Plugins will be able to provide services to each other through a service
  registry managed by the core assembly. The interfaces defining these services
  may be implemented in any loaded assembly, including the plugin's assembly or 
  a separate contracts assembly.
- Plugins will be able to respond to all aspects of the bot's lifecycle, the
  connection's lifecycle, and events which occur on the IRC server. They will
  be able to opt-in to responding to individual protocol messages, but there
  should also be DX-friendly abstractions provided by the plugin base class and
  the bot class so that this is rarely necessary.
- There should be multiple example plugins provided to demonstrate best
  practices of plugin authoring, as well as to test the plugin interfaces.

## Non-negotiable rules

- All code must compile before presenting results. Run `dotnet build`
  and fix all errors.
- All tests must pass before presenting results. Run `dotnet test`
  and fix all failures.
- Never modify the plugin API surface without explicitly flagging it
  and explaining why.
- Prefer `async`/`await` throughout. Use `System.Threading.Channels`
  for internal message passing.

## Coding conventions

- C# 13 / .NET 10 features are encouraged where they aid clarity
- Nullable reference types enabled everywhere
- One class per file, filename matches class name
- XML doc comments on all public API members

## Architecture decisions

Stored in `/docs/adr/`. Read relevant ADRs before making any
structural changes.

## How to run things

To simplify building and testing the bot, provide a makefile that builds and
tests the bot, and copies the bot and all plugins into a single directory for
the user to run.