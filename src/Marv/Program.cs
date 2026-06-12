using System.CommandLine;
using Json5;
using Marv;
using Marv.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentry.Extensions.Logging;

var configOption = new Option<string?>("--config", "-c")
{
    Description = "Path to the configuration file. Format is determined by extension (.json, .json5, .yaml/.yml, .xml)."
};

var rootCommand = new RootCommand("Marv IRC Bot") { configOption };
foreach (var option in ConfigurationOptions.All)
    rootCommand.Add(option);

rootCommand.SetAction(async (result, ct) =>
{
    var configPath = result.GetValue(configOption);

    var builder = Host.CreateApplicationBuilder();

    // Replace default JSON sources with JSON5 equivalents so appsettings.json
    // files gain JSON5 comment support
    ReplaceJsonWithJson5(builder.Configuration);

    // Add marv.json (or user-specified config) on top of the default stack
    var effectivePath = configPath ?? "marv.json";
    AddConfigFile(builder.Configuration, effectivePath, required: configPath is not null);

    // Environment variables with MARV_ prefix (the default host builder adds
    // an unprefixed provider, so standard .NET env vars still work)
    builder.Configuration.AddEnvironmentVariables("MARV_");

    // CLI argument overrides (highest priority)
    builder.Configuration.Sources.Add(ConfigurationOptions.CreateSource(result));

    // Register Marv core services
    builder.Services.AddMarv(builder.Configuration);

    // Configure logging with LogLevel override
    builder.Logging.AddConsole();

    var sentryDsn = builder.Configuration.GetValue<string?>("SentryDsn");
    if (!string.IsNullOrEmpty(sentryDsn))
    {
        builder.Logging.AddSentry(o =>
        {
            o.Dsn = sentryDsn;
            o.MinimumEventLevel = LogLevel.Error;
            o.MinimumBreadcrumbLevel = LogLevel.Warning;
            o.TracesSampleRate = 0;
        });
    }

    var marvLogLevel = builder.Configuration.GetValue<LogLevel?>("LogLevel");
    if (marvLogLevel.HasValue)
    {
        builder.Logging.SetMinimumLevel(marvLogLevel.Value);
    }

    var host = builder.Build();
    await host.RunAsync(ct);
});

var parseResult = rootCommand.Parse(args);
await parseResult.InvokeAsync();

/// <summary>
/// Walks the configuration sources and replaces any <see cref="JsonConfigurationSource"/>
/// instances with <see cref="Json5ConfigurationSource"/> equivalents, preserving
/// path, optional, and reloadOnChange settings.
/// </summary>
static void ReplaceJsonWithJson5(IConfigurationBuilder config)
{
    var sources = config.Sources;
    for (var i = 0; i < sources.Count; i++)
    {
        if (sources[i] is JsonConfigurationSource jsonSource)
        {
            sources[i] = new Json5ConfigurationSource
            {
                Path = jsonSource.Path!,
                Optional = jsonSource.Optional,
                ReloadOnChange = jsonSource.ReloadOnChange,
                FileProvider = jsonSource.FileProvider,
            };
        }
    }
}

/// <summary>
/// Adds a configuration file source based on the file extension.
/// Supports .json, .json5, .yaml/.yml, and .xml formats.
/// </summary>
static void AddConfigFile(IConfigurationBuilder config, string path, bool required)
{
    var extension = Path.GetExtension(path).ToLowerInvariant();

    switch (extension)
    {
        case ".json" or ".json5":
            config.AddJson5File(path, optional: !required, reloadOnChange: true);
            break;
        case ".yaml" or ".yml":
            config.AddYamlFile(path, optional: !required, reloadOnChange: true);
            break;
        case ".xml":
            config.AddXmlFile(path, optional: !required, reloadOnChange: true);
            break;
        default:
            throw new InvalidOperationException(
                $"Unsupported configuration file format: '{extension}'. " +
                "Supported formats: .json, .json5, .yaml, .yml, .xml");
    }
}
