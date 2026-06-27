using System.CommandLine;
using Marv;
using Marv.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentry.Extensions.Logging;

var configOption = new Option<string?>("--config", "-c")
{
    Description = "Path to the configuration file. Format is determined by extension (.json, .yaml/.yml, .xml)."
};

var rootCommand = new RootCommand("Marv IRC Bot") { configOption };
foreach (var option in ConfigurationOptions.All)
    rootCommand.Add(option);

rootCommand.SetAction(async (result, ct) =>
{
    var configPath = result.GetValue(configOption);

    var builder = Host.CreateApplicationBuilder();

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
/// Adds a configuration file source based on the file extension.
/// Supports .json, .yaml/.yml, and .xml formats.
/// </summary>
static void AddConfigFile(IConfigurationBuilder config, string path, bool required)
{
    var extension = Path.GetExtension(path).ToLowerInvariant();

    switch (extension)
    {
        case ".json":
            config.AddJsonFile(path, optional: !required, reloadOnChange: true);
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
                "Supported formats: .json, .yaml, .yml, .xml");
    }
}
