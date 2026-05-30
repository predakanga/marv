# Prompt Log

## Research the problem space before writing code

**Date**: 2026-05-30T00:00:00Z

**Prompt**:

> Before anything else, please read CLAUDE.md and acknowledge the prompt logging and git requirements
>
> Before we write any code, I want you to research the problem space.
> Please investigate the following and produce a research summary saved
> to docs/research.md:
>
> 1. Existing C# IRC libraries available on NuGet — evaluate candidates on:
>    - IRCv3 support, including message tags
>    - Quality of the API (is it easy to build on top of?)
>    - Maintenance status and test coverage
>    - Licensing
>    Make a recommendation with justification. We will use one of these
>    rather than implementing the IRC protocol from scratch.
>
> 2. IRCv3 specifically:
>    - What capabilities are most relevant to a bot (message tags,
>      account-tag, labeled-response, etc)?
>
> 3. Existing IRC bots — look at how bots/frameworks that
>    support multiple platforms design their internal models and APIs.
>    Consider IRC bots in multiple languages, and pay particular attention to
>    Sopel.
>
> 4. Plugin architectures with inter-plugin services — we want plugins
>    to be able to register services (e.g. an authn/authz service) that
>    other plugins can consume. Research:
>    - How existing bot frameworks handle inter-plugin dependencies
>    - Whether .NET's IServiceProvider pattern is appropriate here, or
>      whether a more explicit service registry is better
>    - How to handle load order and optional dependencies gracefully
>
> 5. Common failure modes in IRC bot implementations — from test suites
>    and issue trackers, what edge cases trip up IRC clients most often?
>    List them — we will use this to inform our test suite.
>
> Do not write any C# code yet. The deliverable is docs/research.md only.

## Plugin configuration should be injected, not accessed via bot object

**Date**: 2026-05-30T00:00:00Z

**Prompt**:

> In the conclusion of section 3, you note that it is common for plugins to access configuration from a bot object. I consider this to be an anti-pattern; for Marv, plugins should declare a class modeling their configuration, which will be injected into the plugin when it's constructed.

## Question about plugin loading and configuration viability

**Date**: 2026-05-30T00:00:00Z

**Prompt**:

> In the conclusion of section 4, you recommend loading all plugins first and giving them a chance to contribute to the IServiceProvider. Is this viable if the list of plugins to load is stored in the core's configuration object?

## Clarify single DI container requirement

**Date**: 2026-05-30T00:00:00Z

**Prompt**:

> By "the plugin DI container", do you mean that there's a separate DI container for the main app and the plugins?

> Okay. Please make sure that that requirement is documented for future services.

## Produce design documents for the bot

**Date**: 2026-05-30T09:30:00Z

**Prompt**:

> Before anything else, please read CLAUDE.md and acknowledge the prompt logging and git requirements.
>
> Read docs/research.md and produce a design for the bot. Deliverables:
>
> 1. docs/architecture.md covering:
>    - Project/assembly structure and responsibilities
>    - The layering of components, parsers, etc
>    - The async/threading model
>    - How plugins are discovered and loaded at runtime
>    - How inter-plugin services work: registration, discovery, optional
>      vs required dependencies, and load order
>
> 2. docs/platform-abstraction-draft.md — describe the core concepts that
>    the bot will model and present to plugins:
>    - Messages (including metadata/tags)
>    - Channels and users
>    - Capabilities (how plugins discover what the current platform
>      supports — e.g. not all platforms have message editing)
>    - Events (message received, user joined, etc)
>
> 3. docs/plugin-api-draft.md covering:
>    - What types a plugin author works with day-to-day
>    - How a plugin registers interest in events
>    - How a plugin registers a service for other plugins to consume
>    - How a plugin declares and resolves a dependency on another
>      plugin's service
>    - What the bot exposes back to plugins (sending messages, querying
>      channel/user state, etc)
>    - A concrete example: what does the simplest possible plugin look
>      like, and what does a plugin that depends on an auth service look
>      like?
>
> 4. ADRs in docs/adr/:
>    - 001-platform-abstraction.md — how and why we model the IRC network's
>      entities
>    - 002-irc-library-choice.md — which IRC library we use and why
>      (from the research recommendation)
>    - 003-plugin-service-registry.md — how inter-plugin services work
>    - 004-async-model.md — the async/threading model
>
>> Do not write any C# code yet. I will review these documents and
> approve them before we proceed to implementation.

## Review feedback on design documents

**Date**: 2026-05-30T10:00:00Z

**Prompt**:

> Some questions and notes regarding the architecture document:
>
> - Should Marv.App be the one building the DI container? Plugin loading belongs in the core, which seems like a conflict.
> - Is a single IrcMessage class for both inbound and outbound messages suitable?
> - Handling PING/PONG at the connection layer seems like it breaks layering, why not handle it in the core?
> - I'd prefer to have each plugin run simultaneously - i.e. they should each have their own task and channel to receive IrcMessages. Is this feasible while still allowing direct access to the channel/user stores?
> - The ProvidesService and ConsumesService attributes aren't great DX - can we gather this info automatically?
> - Requiring plugins to register their own config classes isn't great DX either - I anticipate that most plugins will not register services, but will have configs. Can we automate this, potentially through the abstract plugin base class?
>
> Some questions and notes regarding the platform-abstraction-draft:
>
> - Do we need the IChannelMember relation? There are really only two properties (Prefixes, JoinedAt) - all others can be derived from them. Might make more sense to store them in the IChannel itself
> - Can we combine the Channel*Event and Private*Events into a common class with a flag like IsDirect?
> - I'm not sure whether we really want to handle BatchEvent - this forces plugin authors to handle events both by themselves and as batched. Is there a simple way to let authors choose to un-batch them?
>
> Some questions and notes regarding plugin-api-draft.md:
>
> - What is the purpose of having both IBot.GetChannel and IBot.Channels? If there's not a good reason, I prefer just having IBot.Channels.
> - Why is there no IBot.Users, only IBot.GetUser? For consistency there should be IBot.Users. If there's not a good reason, we should also get rid of IBot.GetUser.
> - What does IBot.SendAndAwaitAsync do?
> - In addition to the OnCommand attribute, I'd like an OnRegex attribute which matches against a regular expression and passes the handler the Match object in it's context.
> - In the plugin structure, you mention that handlers may be in their own namespace/classes, but the only examples are of handlers in the main plugin class. How would this work?

## Use ProvidesService attribute instead of auto-scanning ConfigureServices

**Date**: 2026-05-30T10:30:00Z

**Prompt**:

> Could you explain how scanning ConfigureServices registrations would work? My idea was just to scan assemblies for implementations of the consumed types, but I can see how that could be flawed.

> let's go with the attribute approach, update the docs

## Sanity check all design documentation

**Date**: 2026-05-30T10:44:00Z

**Prompt**:

> Before anything else, please read CLAUDE.md and acknowledge the prompt logging and git requirements
>
> I'm getting ready to start development of a C# IRC bot.
>
> Could you read the initial docs that I generated with another Claude Code session (all MD files except for prompts.md) and perform a sanity check?
