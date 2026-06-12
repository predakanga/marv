using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Extensions.Configuration;

namespace Marv;

/// <summary>
/// An <see cref="IConfigurationProvider"/> that extracts explicitly-provided CLI
/// values from a <see cref="ParseResult"/> and presents them as configuration keys.
/// Only options the user actually specified on the command line appear; absent
/// options produce no keys, so lower-priority sources are not overwritten.
/// </summary>
internal sealed class CommandLineConfigurationProvider : ConfigurationProvider
{
    private readonly ParseResult _parseResult;
    private readonly IReadOnlyList<ConfigurationOptions.Entry> _entries;

    public CommandLineConfigurationProvider(CommandLineConfigurationSource source)
    {
        _parseResult = source.ParseResult;
        _entries = source.Entries;
    }

    /// <inheritdoc />
    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _entries)
            entry.Extract(_parseResult, data);

        Data = data;
    }
}

/// <summary>
/// Configuration source that creates a <see cref="CommandLineConfigurationProvider"/>
/// for extracting CLI argument values from a <see cref="ParseResult"/>.
/// </summary>
internal sealed class CommandLineConfigurationSource : IConfigurationSource
{
    /// <summary>The parsed command-line result to extract values from.</summary>
    public required ParseResult ParseResult { get; init; }

    /// <summary>The typed option entries that map CLI options to configuration keys.</summary>
    public required IReadOnlyList<ConfigurationOptions.Entry> Entries { get; init; }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new CommandLineConfigurationProvider(this);
}
