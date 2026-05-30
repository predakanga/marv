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

## Clarify single DI container requirement

**Date**: 2026-05-30T00:00:00Z

**Prompt**:

> By "the plugin DI container", do you mean that there's a separate DI container for the main app and the plugins?

> Okay. Please make sure that that requirement is documented for future services.
