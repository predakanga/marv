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

## Make event dispatch explicit via HandleEventAsync

**Date**: 2026-05-30T11:59:00Z

**Prompt**:

> It's not very clear how handlers are actually called at this point - I think the assumption is that the core directly calls the handler, which feels too magical to me.
> What I'd like to see is an OnIrcEvent method on IPlugin (feel free to change the name to something more appropriate), with the implementation in MarvPlugin using reflection to discover handlers and dispatch the events accordingly.
> MarvPlugin will also need to be provided with a way to instantiate classes, to dispatch to HandlerGroup classes. My first thought is a utility class which wraps IServiceProvider and uses ActivatorUtilities.CreateInstance<T>, but I'm open to other possibilities.

## Make IPluginActivator.CreateInstance generic

**Date**: 2026-05-30T12:06:00Z

**Prompt**:

> That looks good, except that the IPluginActivator should make CreateInstance generic, i.e. T CreateInstance<T>(params object[] params);

## ConfigureServices on IPlugin, reinstantiate on reconnect

**Date**: 2026-05-30T12:21:00Z

**Prompt**:

> Make ConfigureServices a static method on IPlugin, with an empty default implementation.
> Don't register the plugins or HandlerGroups themselves in the service provider. Instantiate them with ActivatorUtilities instead. Plugins can be reinstantiated when reconnecting to a server so that there's no chance of stale references.
> Update architecture.md to note that the core should not call handlers on HandlerGroups; MarvPlugin will do that.

## Implement the full bot from design documents

**Date**: 2026-05-30T13:45:00Z

**Prompt**:

> Before anything else, please read CLAUDE.md and acknowledge the prompt logging and git requirements.
>
> I have reviewed and approved the design in docs/architecture.md,
> docs/platform-abstraction-draft.md, and docs/plugin-api-draft.md.
> Now implement it.
>
> Proceed in this order, verifying that each layer compiles and its
> tests pass before moving to the next:
>
> 1. Solution and project structure (csproj/sln files only, no logic)
>
> 2. The platform abstraction layer — the interfaces and types that
>    define channels, users, messages, metadata/tags, events, and
>    capabilities. No implementation yet, just contracts.
>
> 3. The IRC layer, including the parser, capability manager and interface implementations.
>    Unit test edge cases identified in docs/research.md. Include references to sources such as `ircdocs/parser-tests`.
>
> 4. The plugin host — plugin discovery/loading, the event dispatch
>    pipeline, and utility classes as designed in ADR 003.
>
> 5. The plugin API project — the types plugin authors reference including
>    the implementation of MarvPlugin.
>
> 6. Four reference plugins to validate the API:
>    a. A simple greeting plugin — to validate basic event handling and message sending
>    b. A two-plugin pair: one plugin that registers an auth service, and one
>       that consumes it — to validate the inter-plugin service mechanism
>    c. A plugin demonstrating use of HandlerGroups to return canned responses
>
> 7. The main application - include support for JSON, YAML, XML and TOML configuration.
>
> Provide an option (makefile or build steps) to copy all the assemblies and plugins to one directory ready for use.
>
> Rules:
> - Run `dotnet build` after each layer. Fix all errors before
>   continuing.
> - Run `dotnet test` after each layer. Fix all failures before
>   continuing.
> - Commit your changes to git after each layer.
> - If you make any architectural decision not covered by the ADRs,
>   write a new ADR before writing the code that depends on it.
> - Do not change the plugin API or platform abstraction surface without
>   flagging it to me first.

## Keep PluginName as instance property

**Date**: 2026-05-30T15:00:00Z

**Prompt**:

> Regarding your change to the design of PluginName, C# definitely does support static abstract properties. See @static_abstract_property.cs for an example of this. Please stick with the original design.

> it looks like the feature is only available in preview versions of C#. Make PluginName a regular instance property in all cases, forget about the static approach.

## Replace PluginName property with convention and attribute

**Date**: 2026-05-30T15:15:00Z

**Prompt**:

> On second thought, making PluginName an instance property doesn't work either - the bot needs to be able to choose which plugins to load based on the config. Let's adopt your approach (deriving the name from the plugin class, stripping Plugin off the end), but also provide a PluginName attribute which can be applied to the plugin class.

## Implement IRC connection and main loop

**Date**: 2026-05-31T00:30:00Z

**Prompt**:

> Now let's implement the IRC connection and main loop

## Flatten config, add LogLevel override, add CLI options

**Date**: 2026-05-31T01:00:00Z

**Prompt**:

> Let's start with some simple changes - first up, I'd like to change the layout of the config. I've provided an example of how I want it to look in @marv.yaml. I've used snake casing for the keys because that's what feels natural to me, but this is not a requirement for the actual implementation.
> Inline these into MarvConfiguration and move that into the Marv.Core package.
>
> Next, I want to add logging configuration to the config file - I know that logging can be configured through appsettings.json, but I'd like an additional LogLevel override in the Marv config file.
> When provided, the default log level should be set to the higher of the Marv LogLevel and the appsettings.json LogLevel.
>
> Next, let's look at the CLI itself - at the minute it only exposes one option, `--config`. I'd like it to expose options for each of the members of the core's configuration, but not for plugins. Also, make a note in CLAUDE.md that any changes to the configuration must be reflected in the command line.

## Auto-generate CLI options from MarvConfiguration

**Date**: 2026-05-31T01:30:00Z

**Prompt**:

> There's a lot of duplication around the CLI configuration overrides. Let's auto-generate that by scanning MarvConfiguration. You can use reflection, a source generator, or any other approach you deem appropriate.

> It looks like the methods you're looking for are in System.CommandLine 3. Update that dependency to the latest preview version.

## Remove manual CLI sync note from CLAUDE.md

**Date**: 2026-05-31T01:45:00Z

**Prompt**:

> With that change, we can remove the line from CLAUDE.md about keeping MarvConfiguration and Program.cs in sync.

## Improve build output: single-file app and flat plugin directory

**Date**: 2026-05-31T02:00:00Z

**Prompt**:

> Let's improve the build output - configure the main app to use PublishSingleFile, and update the references on plugins to use <Private>false</Private>. Then, update the makefile so that the plugins are published to a common plugin directory, instead of their own subdirectories.

## Rename Marv.App to Marv

**Date**: 2026-05-31T03:00:00Z

**Prompt**:

> Rename Marv.App to Marv

## Adjust logging levels for plugin discovery and wire protocol

**Date**: 2026-05-31T03:30:00Z

**Prompt**:

> Lets adjust the logging - information about plugin/service discovery should move to the debug level, while the actual load messages can stay informational. Add logging of the actual wire protocol (incoming and outgoing) at the trace level.

## Move "Instantiated plugin" log to debug

**Date**: 2026-05-31T03:35:00Z

**Prompt**:

> Slight tweak - the "Instantiated plugin" messages should be debug, not informational

## Fix --log-level not allowing lower levels like Trace

**Date**: 2026-05-31T03:40:00Z

**Prompt**:

> When I run the bot with --log-level Trace, I don't see the expected protocol trace

## Fix NickServ IDENTIFY sent with empty password

**Date**: 2026-05-31T03:45:00Z

**Prompt**:

> NickServ identify is being sent even when a password isn't provided

## Research IRC formatting code APIs for plugin authors

**Date**: 2026-05-31T04:00:00Z

**Prompt**:

> I want to provide a simple way for plugin authors to use IRC formatting codes. Do some research and provide me a summary on the methods used by other bots to do this. Deliver the output in docs/formatting-research.md.

## Evaluate formatting research against a real-world example

**Date**: 2026-05-31T04:15:00Z

**Prompt**:

> In order to evaluate that research properly, I think it's worth considering an example message - this is typical of the plugins that will be used with this bot: "\x0310,01[\x037 Community \x0310] :: [\x033 Network: \x037NBC \x0310] :: [ \x033Runtime:\x037 25 minutes \x0310] :: [\x033 Rating:\x037 \x02TV-PG\x02\x0310 ] :: [\x0314 https://thetvdb.com/series/community \x0310]\017"

## Implement IRC formatting API in Marv.Core.Formatting

**Date**: 2026-05-31T04:30:00Z

**Prompt**:

> Okay, let's implement the proposed approach. These should live in a Util package to make it clear that they're not coupled tightly to the core.

> I didn't expect that that would create a whole new assembly - what's the standard practice for utility classes like this?

> yeah, do that

## Strip formatting from text before OnCommand and OnRegex matching

**Date**: 2026-05-31T04:45:00Z

**Prompt**:

> Make sure that OnCommand and OnRegex match against the formatting-stripped text

## Prevent bot from triggering itself with echo-message

**Date**: 2026-05-31T05:00:00Z

**Prompt**:

> I haven't checked the code, but I'm worried the bot may end up triggering itself when the echo-message capability is enabled. Make sure that doesn't happen

## Change plugin config key back to Plugins: and update example config

**Date**: 2026-05-31T05:15:00Z

**Prompt**:

> I've changed my mind about the config key for plugins - change it back to Plugins:{PluginName}, instead of PluginConfigs:{PluginName}. Also, make sure that the example config matches the current schema.

## Add integration tests against real IRC server

**Date**: 2026-05-31T09:15:00Z

**Prompt**:

> All of our tests so far are quite isolated. It's important to test the bot against a real IRC server, so please add some tests that use an IRC server running on localhost.
>
> The IRC server won't be available in all environments so these tests shouldn't run by default, but note in CLAUDE.md that you should start the IRC server, run tests, then stop the server again for your own testing. Also update the Makefile to do that when running tests.

## Implement server authentication (PASS, NickServ, OPER) and ReadyEvent

**Date**: 2026-05-31T12:00:00Z

**Prompt**:

> Our next challenge is fleshing out the bot's authentication to the server. There are three aspects that need improvement:
> - IRC server password (i.e. PASS command)
> - NickServ authentication (research the top services and decide whether we need different implementations and a NickservType config)
> - Oper authentication
>
> It may be worth adding an event to alert plugins when all required authentication has been completed, but that would be our first synthetic event so it needs careful thought.
>
> (Follow-up) Go ahead and implement them, but I have a quick note - it's important to block channel joins until all the auth is completed, but the AuthenticationCompleteEvent name probably isn't right; there should be a single event that plugins can wait on whether or not the bot is configured to authenticate. The timeout is unfortunate, but necessary.
>
> (Follow-up) I think it's probably better to wait for the 900 numeric than wait for a notice

## Move OnInterval handlers to a background timer task

**Date**: 2026-05-31T12:30:00Z

**Prompt**:

> I noticed that the OnInterval handlers are only triggered when an event arrives - is there any reason not to use a background task to service those handlers instead?
>
> (Follow-up) I think it should use OnLoadAsync instead - could run while the bot isn't yet connected
