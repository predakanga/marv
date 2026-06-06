using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Plugins.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marv.Plugins.Moderation;

/// <summary>
/// Example moderation plugin demonstrating advanced API patterns: typed configuration,
/// event handling, interval timers, bot action methods, case-mapped collections,
/// declarative authorization filters, and inter-plugin dependencies.
/// </summary>
[DependsOn(typeof(AuthPlugin))]
public class ModerationPlugin : MarvPlugin
{
    private readonly ModerationConfig _config;

    // Ban tracking: maps hostmask → expiry. Rebuilt on each connection using
    // Bot.CaseComparer so nick lookups respect the server's case mapping rules.
    private Dictionary<string, DateTimeOffset> _activeBans = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a new <see cref="ModerationPlugin"/>.</summary>
    public ModerationPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory,
        IOptions<ModerationConfig> config)
        : base(bot, activator, loggerFactory)
    {
        _config = config.Value;
    }

    /// <inheritdoc />
    public override Task OnConnectedAsync(CancellationToken ct)
    {
        // Rebuild connection-scoped state with the server's case mapping comparer
        _activeBans = new Dictionary<string, DateTimeOffset>(Bot.CaseComparer);
        return base.OnConnectedAsync(ct);
    }

    /// <inheritdoc />
    public override Task OnDisconnectedAsync()
    {
        _activeBans.Clear();
        return base.OnDisconnectedAsync();
    }

    /// <summary>Kicks a user from the channel. Requires "mod.kick" permission.</summary>
    [RequireAuth("mod.kick")]
    [OnCommand("kick", ChannelOnly = true)]
    private async Task HandleKick(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count == 0)
        {
            await ctx.ReplyAsync("Usage: !kick <nick> [reason]", ct);
            return;
        }

        var nick = ctx.Args[0];
        var reason = ctx.Args.Count > 1 ? ctx.ArgString[(nick.Length + 1)..] : null;
        await Bot.KickAsync(ctx.Channel!.Name, nick, reason, ct);
        await AuditAsync($"{ctx.Sender.Nick} kicked {nick} from {ctx.Channel.Name}", ct);
    }

    /// <summary>
    /// Bans a user's hostmask from the channel. Demonstrates stacked [OnCommand]
    /// attributes for command aliases — both "!ban" and "!b" trigger this handler.
    /// Requires "mod.ban" permission.
    /// </summary>
    [RequireAuth("mod.ban")]
    [OnCommand("ban", ChannelOnly = true)]
    [OnCommand("b", ChannelOnly = true)]
    private async Task HandleBan(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count == 0)
        {
            await ctx.ReplyAsync("Usage: !ban <nick>", ct);
            return;
        }

        var nick = ctx.Args[0];
        var mask = $"{nick}!*@*";
        await Bot.SetModeAsync(ctx.Channel!.Name, "+b", mask, ct);
        _activeBans[nick] = DateTimeOffset.UtcNow.AddMinutes(_config.BanDurationMinutes);
        await AuditAsync($"{ctx.Sender.Nick} banned {nick} in {ctx.Channel.Name} for {_config.BanDurationMinutes}m", ct);
    }

    /// <summary>Mutes a user (+q mode). Requires "mod.mute" permission.</summary>
    [RequireAuth("mod.mute")]
    [OnCommand("mute", ChannelOnly = true)]
    private async Task HandleMute(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count == 0)
        {
            await ctx.ReplyAsync("Usage: !mute <nick>", ct);
            return;
        }

        await Bot.SetModeAsync(ctx.Channel!.Name, "+q", ctx.Args[0], ct);
        await AuditAsync($"{ctx.Sender.Nick} muted {ctx.Args[0]} in {ctx.Channel.Name}", ct);
    }

    /// <summary>
    /// Sends a notice to the channel when a user joins. Demonstrates
    /// [OnEvent] with UserJoinedEvent and Bot.SendNoticeAsync.
    /// </summary>
    [OnEvent]
    private async Task HandleJoin(UserJoinedEvent e, CancellationToken ct)
    {
        if (Bot.CaseComparer.Equals(e.User.Nick, Bot.Self.Nick))
            return;

        await Bot.SendNoticeAsync(e.Channel.Name,
            $"Welcome, {e.User.Nick}. This channel is moderated.", ct);
    }

    /// <summary>
    /// Logs kicks to the audit channel. Demonstrates [OnEvent] with UserKickedEvent.
    /// </summary>
    [OnEvent]
    private async Task HandleKicked(UserKickedEvent e, CancellationToken ct)
    {
        await AuditAsync(
            $"{e.Kicked.Nick} was kicked from {e.Channel.Name} by {e.Kicker.Nick}: {e.Reason ?? "no reason"}", ct);
    }

    /// <summary>
    /// Periodically removes expired bans. Demonstrates [OnInterval] for
    /// background timer tasks that run independently of the event stream.
    /// </summary>
    [OnInterval(Minutes = 5)]
    private Task CleanupExpiredBans(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _activeBans.Where(kvp => kvp.Value <= now).Select(kvp => kvp.Key).ToList();
        foreach (var nick in expired)
            _activeBans.Remove(nick);

        if (expired.Count > 0)
            Logger.LogInformation("Cleaned up {Count} expired ban(s)", expired.Count);

        return Task.CompletedTask;
    }

    /// <summary>Sends an audit message to the configured audit channel, if set.</summary>
    internal async Task AuditAsync(string message, CancellationToken ct)
    {
        if (_config.AuditChannel is not null)
            await Bot.SendMessageAsync(_config.AuditChannel, $"[Mod] {message}", ct);
    }

    /// <summary>Exposes active bans for testing and admin commands.</summary>
    internal IReadOnlyDictionary<string, DateTimeOffset> ActiveBans => _activeBans;
}
