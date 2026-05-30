using System.CommandLine;
using Marv.App;
using Marv.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var configOption = new Option<string?>("--config", "-c")
{
    Description = "Path to the configuration file. Format is determined by extension (.json, .yaml/.yml, .xml, .toml)."
};

var rootCommand = new RootCommand("Marv IRC Bot")
{
    configOption
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

    // Layer 4: Command-line arguments
    builder.Configuration.AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());

    // Read plugin paths from configuration
    var pluginPaths = builder.Configuration.GetSection("Plugins").Get<List<string>>() ?? [];

    // Register Marv core services
    builder.Services.AddMarv(builder.Configuration, pluginPaths);

    // Configure logging
    builder.Logging.AddConsole();

    var host = builder.Build();

    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Marv IRC Bot starting...");

    var config = builder.Configuration.Get<MarvConfiguration>() ?? new MarvConfiguration();
    logger.LogInformation("Server: {Server}:{Port} (TLS: {UseTls})",
        config.Irc.Server, config.Irc.Port, config.Irc.UseTls);
    logger.LogInformation("Nick: {Nick}", config.Irc.Nick);
    logger.LogInformation("Plugins: {Count} configured", pluginPaths.Count);

    // TODO: Connect to IRC server and run the main loop
    logger.LogInformation("Configuration loaded successfully. Bot is ready to connect.");
    logger.LogInformation("(Connection implementation pending — this is the application shell)");

    await host.RunAsync(ct);
});

var parseResult = rootCommand.Parse(args);
await parseResult.InvokeAsync();

/// <summary>
/// Adds a configuration file source based on the file extension.
/// Supports .json, .yaml/.yml, .xml, and .toml formats.
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
