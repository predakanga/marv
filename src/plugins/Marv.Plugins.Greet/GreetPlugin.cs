using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.Options;

namespace Marv.Plugins.Greet;

/// <summary>
/// A simple greeting plugin that welcomes users when they join a channel.
/// Demonstrates basic event handling, configuration, and message sending.
/// </summary>
public class GreetPlugin : MarvPlugin
{
    /// <inheritdoc />
    public override string PluginName => "Greet";

    private readonly GreetPluginConfig _config;

    /// <summary>
    /// Creates a new <see cref="GreetPlugin"/> with the specified bot, activator, and configuration.
    /// </summary>
    public GreetPlugin(IBot bot, IPluginActivator activator, IOptions<GreetPluginConfig> config)
        : base(bot, activator)
    {
        _config = config.Value;
    }

    /// <summary>
    /// Handles user join events by sending a greeting message to the channel.
    /// Does not greet the bot itself.
    /// </summary>
    [OnEvent]
    private async Task HandleJoin(UserJoinedEvent e, CancellationToken ct)
    {
        if (!_config.GreetOnJoin)
            return;

        // Don't greet ourselves
        if (e.User.Nick == Bot.Self.Nick)
            return;

        var message = _config.GreetMessage.Replace("{nick}", e.User.Nick);
        await Bot.SendMessageAsync(e.Channel.Name, message, ct);
    }

    /// <summary>
    /// Responds to the !hello command with a personalized greeting.
    /// </summary>
    [OnCommand("hello")]
    private async Task HandleHello(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync($"Hello, {ctx.Sender.Nick}!", ct);
    }
}
