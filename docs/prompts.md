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

## Responses to design documentation sanity check

**Date**: 2026-05-30T11:07:00Z

**Prompt**:

> Responses to the sanity check:
>
> - Remove the dangling references to ConsumeService
> - Regarding the IUser mutation model, can you sketch out what atomic property replacement would look like?
> - Regarding IChannel.SendMessageAsync, change the eaxmples to use bot.SendMessageAsync instead.
> - Regarding CTCP events, implement CTCP VERSION, PING and TIME in the core, but also provide a generic CtcpEvent to allow plugins to implement arbritrary commands. Note that the internal implementations of CTCP commands must not expose host information, only bot version, etc.
> - Regarding plugin configuration, instead of selecting the plugin config class through MarvPlugin<TConfig>, scan the assembly for classes tagged with a [PluginConfig(Section = "Something")] attribute and register that accordingly.
> - The configuration file format should default to JSON, but there should also be a CLI argument to specify the config path; if this is provided, the extension of the config path should determine the file format.
> - For command-line handling, use System.CommandLine 2.x
> - Regarding reconnection behaviour, the default stance should be for state to be discarded - SendAndAwaitAsync calls should be cancelled, message queues cleared, and stale objects discarded.
> - Handler discovery rules: Handler methods don't need to be public. The dispatching should be done from within MarvPlugin so that protected methods are accessible. This does raise the question of how MarvPlugin can construct the HandlerGroup handlers, though.
>   If multiple handlers have the same OnEvent, they will be called consecutively but in an undefined order.
>   HandlerGroups may handle all events, including lifecycle events.
> - Remove the references to HasService<T> and GetOptionalService<T>.
> - I think it's reasonable to inject IBot into plugins by default, unless you can think of a good reason not to?

## Further design refinements

**Date**: 2026-05-30T11:32:00Z

**Prompt**:

> Some more thoughts:
>
> - If optional dependencies are already nullable constructor parameters, do we need OptionalService?
> - Document that assemblies may only contain one MarvPlugin, and it must contain a static property declaring it's name, to be used in log messages, the plugin loading config, etc.
> - I'm not sure I like the snapshot-on-publish approach - what are the risks involved with allowing the models to be mutable, and is there a way to mitigate those risks?
> - Note that handler methods inside MarvPlugin don't need to be public, but those in HandlerGroups do.
> - How will IBot injection into GreetPlugin be accomplished? I expected a constructor parameter that's passed to the base MarvPlugin constructor.

## Mutable models confirmed, enforce PluginName via interface

**Date**: 2026-05-30T11:43:00Z

**Prompt**:

> I'm okay with the mutable model approach, so long as it's clearly documented.
>
> Instead of validating PluginName at discovery time, enforce it with an interface. In fact, include all the plugin methods on that interface so that authors can bypass MarvPlugin if they want.
