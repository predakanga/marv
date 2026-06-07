using Marv.Core;
using Marv.Core.Platform;
using Marv.Core.Plugin;

namespace Marv.Plugins.CannedResponses;

/// <summary>
/// Handler group for informational canned responses.
/// Demonstrates organizing related command handlers into a separate class.
/// </summary>
[HandlerGroup]
public class InfoHandlers
{
    private readonly IBot _bot;

    /// <summary>
    /// Creates a new <see cref="InfoHandlers"/> with the specified bot.
    /// Constructor parameters are resolved from DI via <see cref="IPluginActivator"/>.
    /// </summary>
    public InfoHandlers(IBot bot)
    {
        _bot = bot;
    }

    /// <summary>Responds with version information.</summary>
    [OnCommand("version")]
    public async Task HandleVersion(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync($"Marv IRC Bot v{MarvVersion.Current}", ct);
    }

    /// <summary>Responds with help text.</summary>
    [OnCommand("help")]
    public async Task HandleHelp(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync("Available commands: !help, !version, !source, !ping", ct);
    }

    /// <summary>Responds with the source code URL.</summary>
    [OnCommand("source")]
    public async Task HandleSource(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync("Source code: https://github.com/predakanga/marv", ct);
    }
}
