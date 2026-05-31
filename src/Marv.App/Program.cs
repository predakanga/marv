using System.CommandLine;
using Marv.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var configOption = new Option<string?>("--config", "-c")
{
    Description = "Path to the configuration file. Format is determined by extension (.json, .yaml/.yml, .xml)."
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

    // Register Marv core services (includes IrcBot, MarvBotService, plugins)
    builder.Services.AddMarv(builder.Configuration, pluginPaths);

    // Configure logging
    builder.Logging.AddConsole();

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
