namespace Marv.Core.Plugin;

/// <summary>
/// Declares that a plugin provides the specified service type to other plugins.
/// The plugin must also override <see cref="IPlugin.ConfigureServices"/> to register the implementation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ProvidesServiceAttribute(Type serviceType) : Attribute
{
    /// <summary>The service interface type this plugin provides.</summary>
    public Type ServiceType { get; } = serviceType;
}

/// <summary>
/// Declares that a plugin depends on another plugin for load ordering,
/// without implying a service relationship.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnAttribute(Type pluginType) : Attribute
{
    /// <summary>The plugin type that must load before this plugin.</summary>
    public Type PluginType { get; } = pluginType;
}

/// <summary>
/// Overrides the default plugin name (derived by stripping the "Plugin" suffix from the class name).
/// Applied to the plugin class itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginNameAttribute(string name) : Attribute
{
    /// <summary>The human-readable plugin name.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// Tags a configuration class for automatic registration as IOptions&lt;T&gt;
/// bound to the Plugins:{Section} configuration section.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginConfigAttribute : Attribute
{
    /// <summary>
    /// The configuration section name under "Plugins" (e.g. "Greet" maps to "Plugins:Greet").
    /// </summary>
    public required string Section { get; init; }
}

/// <summary>
/// Marks a class as a handler group belonging to a specific plugin.
/// Handler groups are discovered and instantiated by <see cref="MarvPlugin"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HandlerGroupAttribute(Type pluginType) : Attribute
{
    /// <summary>The plugin type this handler group belongs to.</summary>
    public Type PluginType { get; } = pluginType;
}

/// <summary>
/// Marks a method as an event handler. The event type is inferred from the method's
/// first parameter. The method must accept a single event parameter and a CancellationToken.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnEventAttribute : Attribute;

/// <summary>
/// Marks a method as a command handler. Triggered when a message starts with the
/// configured command prefix followed by the specified command name.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnCommandAttribute(string command) : Attribute
{
    /// <summary>The command name to match (without the prefix).</summary>
    public string Command { get; } = command;

    /// <summary>
    /// Overrides the bot-wide command prefix for this handler.
    /// When null, the bot's configured <see cref="IBot.CommandPrefix"/> is used.
    /// </summary>
    public string? Prefix { get; init; }
}

/// <summary>
/// Marks a method as a regex-matched handler. Triggered when a message matches
/// the specified regular expression pattern.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnRegexAttribute(string pattern) : Attribute
{
    /// <summary>The regular expression pattern to match against message text.</summary>
    public string Pattern { get; } = pattern;
}

/// <summary>
/// Marks a method as a raw IRC message handler for the specified IRC command.
/// Useful for protocol-level handling not covered by typed events.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnRawMessageAttribute(string command) : Attribute
{
    /// <summary>The IRC command to match (e.g. "INVITE").</summary>
    public string Command { get; } = command;
}

/// <summary>
/// Marks a method as a periodic handler that runs at the specified interval while connected.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnIntervalAttribute : Attribute
{
    /// <summary>The interval in minutes between invocations.</summary>
    public double Minutes { get; init; }

    /// <summary>The interval in seconds between invocations.</summary>
    public double Seconds { get; init; }

    /// <summary>
    /// Creates an interval attribute. Specify either <see cref="Minutes"/> or <see cref="Seconds"/>.
    /// </summary>
    public OnIntervalAttribute() { }
}
