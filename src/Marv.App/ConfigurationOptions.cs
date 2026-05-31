using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using Marv.Core;

namespace Marv.App;

/// <summary>
/// Auto-generates System.CommandLine <see cref="Option"/> instances from
/// <see cref="MarvConfiguration"/> properties, and extracts CLI overrides
/// into a config-compatible dictionary after parsing.
/// </summary>
internal static class ConfigurationOptions
{
    private abstract record Entry(Option Option)
    {
        /// <summary>
        /// If the option was provided on the command line, writes the value(s) into the overrides dictionary
        /// using .NET configuration keys (e.g. "Server", "Channels:0").
        /// </summary>
        public abstract void Apply(ParseResult result, Dictionary<string, string?> overrides);
    }

    private sealed record ScalarEntry<T>(Option<T> TypedOption, string ConfigKey) : Entry(TypedOption)
    {
        public override void Apply(ParseResult result, Dictionary<string, string?> overrides)
        {
            if (result.GetResult(TypedOption) is null) return;
            var value = result.GetValue(TypedOption);
            if (value is not null)
                overrides[ConfigKey] = value.ToString();
        }
    }

    private sealed record BoolEntry(Option<bool> TypedOption, string ConfigKey) : Entry(TypedOption)
    {
        public override void Apply(ParseResult result, Dictionary<string, string?> overrides)
        {
            if (result.GetResult(TypedOption) is null) return;
            overrides[ConfigKey] = result.GetValue(TypedOption).ToString();
        }
    }

    private sealed record CollectionEntry(Option<string[]> TypedOption, string ConfigKey) : Entry(TypedOption)
    {
        public override void Apply(ParseResult result, Dictionary<string, string?> overrides)
        {
            if (result.GetResult(TypedOption) is null) return;
            var values = result.GetValue(TypedOption);
            if (values is null) return;
            for (var i = 0; i < values.Length; i++)
                overrides[$"{ConfigKey}:{i}"] = values[i];
        }
    }

    private static readonly List<Entry> Entries = Build();

    /// <summary>All generated CLI options, ready to add to a <see cref="RootCommand"/>.</summary>
    public static IEnumerable<Option> All => Entries.Select(e => e.Option);

    /// <summary>
    /// Inspects the <see cref="ParseResult"/> and returns a dictionary of configuration
    /// overrides for any options the user explicitly provided on the command line.
    /// </summary>
    public static Dictionary<string, string?> GetOverrides(ParseResult result)
    {
        var overrides = new Dictionary<string, string?>();
        foreach (var entry in Entries)
            entry.Apply(result, overrides);
        return overrides;
    }

    private static List<Entry> Build()
    {
        var entries = new List<Entry>();

        foreach (var prop in typeof(MarvConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var cliName = $"--{ToKebabCase(prop.Name)}";
            var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            var propType = prop.PropertyType;
            var underlying = Nullable.GetUnderlyingType(propType) ?? propType;

            if (propType == typeof(bool))
            {
                var opt = new Option<bool>(cliName) { Description = description };
                entries.Add(new BoolEntry(opt, prop.Name));
            }
            else if (underlying == typeof(int))
            {
                var opt = new Option<int?>(cliName) { Description = description };
                entries.Add(new ScalarEntry<int?>(opt, prop.Name));
            }
            else if (underlying == typeof(string))
            {
                var opt = new Option<string?>(cliName) { Description = description };
                entries.Add(new ScalarEntry<string?>(opt, prop.Name));
            }
            else if (underlying.IsEnum)
            {
                AddEnumEntry(entries, underlying, cliName, description, prop.Name);
            }
            else if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var opt = new Option<string[]>(cliName)
                {
                    Description = description,
                    AllowMultipleArgumentsPerToken = true
                };
                entries.Add(new CollectionEntry(opt, prop.Name));
            }
        }

        return entries;
    }

    /// <summary>
    /// Adds a <see cref="ScalarEntry{T}"/> for a nullable enum type via reflection,
    /// so new enum properties on <see cref="MarvConfiguration"/> are handled automatically.
    /// </summary>
    private static void AddEnumEntry(List<Entry> entries, Type enumType, string cliName, string description, string configKey)
    {
        var nullableType = typeof(Nullable<>).MakeGenericType(enumType);
        var optionType = typeof(Option<>).MakeGenericType(nullableType);
        var opt = (Option)Activator.CreateInstance(optionType, cliName)!;
        opt.Description = description;

        var entryType = typeof(ScalarEntry<>).MakeGenericType(nullableType);
        entries.Add((Entry)Activator.CreateInstance(entryType, opt, configKey)!);
    }

    private static string ToKebabCase(string pascalCase)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
