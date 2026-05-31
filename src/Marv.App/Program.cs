using System.CommandLine;
using Marv.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Configuration file
var configOption = new Option<string?>("--config", "-c")
{
    Description = "Path to the configuration file. Format is determined by extension (.json, .yaml/.yml, .xml)."
};

// IRC connection options
var serverOption = new Option<string?>("--server")
{
    Description = "IRC server hostname."
};
var portOption = new Option<int?>("--port")
{
    Description = "IRC server port."
};
var useTlsOption = new Option<bool?>("--use-tls")
{
    Description = "Use TLS for the connection."
};
var nickOption = new Option<string?>("--nick")
{
    Description = "Bot nickname."
};
var userOption = new Option<string?>("--user")
{
    Description = "Bot username (ident)."
};
var realNameOption = new Option<string?>("--real-name")
{
    Description = "Bot real name (GECOS)."
};
var saslUserOption = new Option<string?>("--sasl-user")
{
    Description = "SASL username for authentication."
};
var saslPasswordOption = new Option<string?>("--sasl-password")
{
    Description = "SASL password for authentication."
};
var nickServPasswordOption = new Option<string?>("--nickserv-password")
{
    Description = "NickServ password for legacy authentication."
};
var channelsOption = new Option<string[]?>("--channels")
{
    Description = "Channels to join on connect.",
    AllowMultipleArgumentsPerToken = true
};
var commandPrefixOption = new Option<string?>("--command-prefix")
{
    Description = "Command prefix for plugin commands."
};

// Plugin options
var pluginDirectoriesOption = new Option<string[]?>("--plugin-directories")
{
    Description = "Directories to scan for plugin assemblies.",
    AllowMultipleArgumentsPerToken = true
};
var pluginsOption = new Option<string[]?>("--plugins")
{
    Description = "Plugin names to load.",
    AllowMultipleArgumentsPerToken = true
};

// Logging
var logLevelOption = new Option<LogLevel?>("--log-level")
{
    Description = "Override for the default log level."
};

var rootCommand = new RootCommand("Marv IRC Bot")
{
    configOption,
    serverOption,
    portOption,
    useTlsOption,
    nickOption,
    userOption,
    realNameOption,
    saslUserOption,
    saslPasswordOption,
    nickServPasswordOption,
    channelsOption,
    commandPrefixOption,
    pluginDirectoriesOption,
    pluginsOption,
    logLevelOption
};

rootCommand.SetAction(async (result, ct) =>
{
    var configPath = result.GetValue(configOption);

    var builder = Host.CreateApplicationBuilder();

    // Clear default config sources and rebuild with our layered approach
    builder.Configuration.Sources.Clear();

    // Layer 1: Default configuration file (marv.json)
    // Layer 2: User-specified configuration file (via --config)
    var effectivePath = configPath ?? "marv.json";
    AddConfigFile(builder.Configuration, effectivePath, required: configPath is not null);

    // Layer 3: Environment variables with MARV_ prefix
    builder.Configuration.AddEnvironmentVariables("MARV_");

    // Layer 4: CLI argument overrides (highest priority)
    var overrides = new Dictionary<string, string?>();

    if (result.GetValue(serverOption) is { } server)
        overrides["Server"] = server;
    if (result.GetValue(portOption) is { } port)
        overrides["Port"] = port.ToString();
    if (result.GetValue(useTlsOption) is { } useTls)
        overrides["UseTls"] = useTls.ToString();
    if (result.GetValue(nickOption) is { } nick)
        overrides["Nick"] = nick;
    if (result.GetValue(userOption) is { } user)
        overrides["User"] = user;
    if (result.GetValue(realNameOption) is { } realName)
        overrides["RealName"] = realName;
    if (result.GetValue(saslUserOption) is { } saslUser)
        overrides["SaslUser"] = saslUser;
    if (result.GetValue(saslPasswordOption) is { } saslPassword)
        overrides["SaslPassword"] = saslPassword;
    if (result.GetValue(nickServPasswordOption) is { } nickServPassword)
        overrides["NickServPassword"] = nickServPassword;
    if (result.GetValue(commandPrefixOption) is { } commandPrefix)
        overrides["CommandPrefix"] = commandPrefix;
    if (result.GetValue(logLevelOption) is { } logLevel)
        overrides["LogLevel"] = logLevel.ToString();

    if (result.GetValue(channelsOption) is { } channels)
        for (var i = 0; i < channels.Length; i++)
            overrides[$"Channels:{i}"] = channels[i];

    if (result.GetValue(pluginDirectoriesOption) is { } pluginDirs)
        for (var i = 0; i < pluginDirs.Length; i++)
            overrides[$"PluginDirectories:{i}"] = pluginDirs[i];

    if (result.GetValue(pluginsOption) is { } plugins)
        for (var i = 0; i < plugins.Length; i++)
            overrides[$"Plugins:{i}"] = plugins[i];

    if (overrides.Count > 0)
        builder.Configuration.AddInMemoryCollection(overrides);

    // Register Marv core services
    builder.Services.AddMarv(builder.Configuration);

    // Configure logging with LogLevel override
    builder.Logging.AddConsole();

    var marvLogLevel = builder.Configuration.GetValue<LogLevel?>("LogLevel");
    if (marvLogLevel.HasValue)
    {
        var defaultLogLevel = builder.Configuration.GetValue<LogLevel?>("Logging:LogLevel:Default")
                              ?? LogLevel.Information;
        var effectiveLevel = (LogLevel)Math.Max((int)marvLogLevel.Value, (int)defaultLogLevel);
        builder.Logging.SetMinimumLevel(effectiveLevel);
    }

    var host = builder.Build();
    await host.RunAsync(ct);
});

var parseResult = rootCommand.Parse(args);
await parseResult.InvokeAsync();

/// <summary>
/// Adds a configuration file source based on the file extension.
/// Supports .json, .yaml/.yml, and .xml formats.
/// </summary>
static void AddConfigFile(IConfigurationBuilder config, string path, bool required)
{
    var extension = Path.GetExtension(path).ToLowerInvariant();

    switch (extension)
    {
        case ".json":
            config.AddJsonFile(path, optional: !required, reloadOnChange: false);
            break;
        case ".yaml" or ".yml":
            config.AddYamlFile(path, optional: !required, reloadOnChange: false);
            break;
        case ".xml":
            config.AddXmlFile(path, optional: !required, reloadOnChange: false);
            break;
        default:
            throw new InvalidOperationException(
                $"Unsupported configuration file format: '{extension}'. " +
                "Supported formats: .json, .yaml, .yml, .xml");
    }
}
