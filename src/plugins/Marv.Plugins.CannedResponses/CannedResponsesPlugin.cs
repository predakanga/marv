using Marv.Core.Platform;
using Marv.Core.Plugin;

namespace Marv.Plugins.CannedResponses;

/// <summary>
/// Plugin that demonstrates the use of HandlerGroups to organize canned responses.
/// The plugin class itself is minimal; all handler logic lives in handler groups.
/// </summary>
public class CannedResponsesPlugin : MarvPlugin
{
    /// <summary>
    /// Creates a new <see cref="CannedResponsesPlugin"/>.
    /// </summary>
    public CannedResponsesPlugin(IBot bot, IPluginActivator activator)
        : base(bot, activator) { }
}
