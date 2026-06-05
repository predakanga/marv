These are changes to the Marv framework that would benefit future plugin project.

### 1. Command/Regex Handler Filters

**Problem:** Almost every handler starts with `if (ctx.IsDirect) return;`
or `if (!ctx.IsDirect) return;`. Many also check the channel name.

**Suggestion:** Add filter properties to `[OnCommand]` and `[OnRegex]`:

```csharp
[OnCommand("ban", ChannelOnly = true)]
[OnCommand("identify", DirectOnly = true)]
[OnRegex(@"...", Channel = "#torbot-logfeed")]
```

The MarvPlugin dispatch loop would check these before invoking the handler.

### 2. Built-in Authorization Attribute

**Problem:** Plugins have to implement their own authorization checks in
every handler. Marv already ships `Marv.Plugins.Auth` with
`IAuthorizationService`, but there's no declarative way to use it.

**Suggestion:** Add a `[RequireAuth("permission")]` attribute that
MarvPlugin checks before dispatch, calling `IAuthorizationService`
if registered. This is a common pattern in bot frameworks (Discord.NET,
DSharpPlus).

### 3. IHttpClientFactory Registration

**Problem:** Four exploratory plugins (Tv, Twitter, UrlTitle, Misc) each add
`Microsoft.Extensions.Http` as a dependency and inject
`IHttpClientFactory`. The service registration for `IHttpClientFactory`
must happen somewhere — either in the host application or a plugin.

**Suggestion:** Register `IHttpClientFactory` in the Marv host by default
(via `services.AddHttpClient()`), since HTTP access is a common plugin
need. This removes the per-plugin NuGet dependency.

### 4. Command Prefix Configuration

**Problem:** The command prefix `!` is hardcoded in `MarvPlugin.cs:167`
with a TODO comment: `// TODO: Make configurable per-bot`. We also use
`.` as a prefix for `.invite`, which currently has to use `[OnRegex]`
instead of `[OnCommand]`.

**Suggestion:** Make the command prefix configurable in
`MarvConfiguration`, and consider supporting multiple prefixes.

### 5. Bulk Channel Join

**Problem:** `IdentifyPlugin.SetupIdentifiedUser` and
`IrcPresencePlugin.HandleNickServAuth` both loop over a channel list
sending individual `JOIN` commands. This is rate-limited and slow.

**Suggestion:** Add a `Bot.JoinMultipleAsync(channels)` method that
batches joins efficiently (IRC allows comma-separated channel lists in
`JOIN`).

Side thought:
It could be helpful to slipstream join commands - the bot or connection could maintain a list of pending JOINs, and each call to Bot.JoinAsync could append to that list, enqueuing a join message only if the list was empty. When the message is ready to be sent, the pending joins would be combined into one message and the list cleared.
This approach could be generalizable to other commands (WHO, etc), but I'm concerned that it may break layering.