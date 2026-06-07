using Marv.Core.Plugin;

namespace Marv.Plugins.Moderation;

/// <summary>
/// Configuration for the Moderation plugin. Bound to the "Moderation" configuration section.
/// Demonstrates the typed plugin configuration pattern with <see cref="PluginConfigAttribute"/>.
/// </summary>
[PluginConfig(Section = "Moderation")]
public record ModerationConfig
{
    /// <summary>Default ban duration in minutes. Expired bans are cleaned up periodically.</summary>
    public int BanDurationMinutes { get; init; } = 60;

    /// <summary>Channel to send audit log messages to. If null, audit logging is disabled.</summary>
    public string? AuditChannel { get; init; }
}
