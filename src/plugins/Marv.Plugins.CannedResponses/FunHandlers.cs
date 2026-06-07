using System.Text.RegularExpressions;
using Marv.Core.Platform;
using Marv.Core.Plugin;

namespace Marv.Plugins.CannedResponses;

/// <summary>
/// Handler group for fun/casual canned responses.
/// Demonstrates [OnCommand] and [OnRegex] handlers in a handler group.
/// </summary>
[HandlerGroup]
public class FunHandlers
{
    private readonly IBot _bot;

    /// <summary>
    /// Creates a new <see cref="FunHandlers"/> with the specified bot.
    /// </summary>
    public FunHandlers(IBot bot)
    {
        _bot = bot;
    }

    /// <summary>Responds to ping with pong.</summary>
    [OnCommand("ping")]
    public async Task HandlePing(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync("pong", ct);
    }

    /// <summary>Responds to dice roll command.</summary>
    [OnCommand("roll")]
    public async Task HandleRoll(CommandContext ctx, CancellationToken ct)
    {
        var result = Random.Shared.Next(1, 7);
        await ctx.ReplyAsync($"🎲 {ctx.Sender.Nick} rolled a {result}!", ct);
    }

    /// <summary>Responds when someone says "good bot".</summary>
    [OnRegex(@"\bgood\s+bot\b", Options = RegexOptions.IgnoreCase)]
    public async Task HandleGoodBot(RegexMatchContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync("Thank you! 😊", ct);
    }
}
