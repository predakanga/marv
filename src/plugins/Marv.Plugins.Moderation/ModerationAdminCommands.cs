using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;

namespace Marv.Plugins.Moderation;

/// <summary>
/// Handler group for admin-facing moderation commands. Demonstrates the
/// <see cref="HandlerGroupAttribute"/> pattern for organizing handlers into separate
/// classes, DM-only commands, raw message handling, and <see cref="IBot.SendAndAwaitAsync"/>.
/// </summary>
[HandlerGroup]
public class ModerationAdminCommands
{
    private readonly IBot _bot;

    /// <summary>
    /// Creates a new <see cref="ModerationAdminCommands"/>.
    /// Constructor parameters are resolved from DI via <see cref="IPluginActivator"/>.
    /// </summary>
    public ModerationAdminCommands(IBot bot)
    {
        _bot = bot;
    }

    /// <summary>
    /// Reports moderation stats via DM. Demonstrates <c>DirectOnly = true</c> to
    /// restrict a command to private messages only.
    /// </summary>
    [OnCommand("modstats", DirectOnly = true)]
    public async Task HandleModStats(CommandContext ctx, CancellationToken ct)
    {
        var channelCount = _bot.Channels.Count;
        var userCount = _bot.Users.Count;
        await ctx.ReplyAsync($"Monitoring {channelCount} channel(s) with {userCount} visible user(s).", ct);
    }

    /// <summary>
    /// Auto-joins channels on invite. Demonstrates <see cref="OnRawMessageAttribute"/>
    /// for handling IRC protocol messages not covered by typed events.
    /// </summary>
    [OnRawMessage("INVITE")]
    public async Task HandleInvite(IrcMessage msg, CancellationToken ct)
    {
        // INVITE params: [target_nick, channel]
        if (msg.Parameters.Count >= 2)
            await _bot.JoinAsync(msg.Parameters[1], null, ct);
    }

    /// <summary>
    /// Queries WHO information for a nick. Demonstrates <see cref="IBot.SendAndAwaitAsync"/>
    /// which sends an IRC command and waits for the server's correlated response.
    /// </summary>
    [OnCommand("whois", DirectOnly = true)]
    public async Task HandleWhois(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count == 0)
        {
            await ctx.ReplyAsync("Usage: !whois <nick>", ct);
            return;
        }

        var nick = ctx.Args[0];
        var replies = await _bot.SendAndAwaitAsync(
            new IrcMessage("WHOIS", [nick]), ct);

        var accountLine = replies.FirstOrDefault(r => r.Command == "330");
        if (accountLine is not null && accountLine.Parameters.Count >= 3)
            await ctx.ReplyAsync($"{nick} is logged in as {accountLine.Parameters[2]}", ct);
        else
            await ctx.ReplyAsync($"{nick}: no account info available", ct);
    }
}
