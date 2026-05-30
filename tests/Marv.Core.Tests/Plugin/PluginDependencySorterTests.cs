using Xunit;
using Marv.Core.Plugin;
using System.Reflection;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests for <see cref="PluginDependencySorter"/> topological sorting.
/// </summary>
public class PluginDependencySorterTests
{
    private static PluginDescriptor MakeDescriptor(
        string name,
        Type? pluginType = null,
        IReadOnlyList<Type>? providedServices = null,
        IReadOnlyList<Type>? explicitDeps = null,
        IReadOnlyList<Type>? requiredServices = null,
        IReadOnlyList<Type>? optionalServices = null)
    {
        return new PluginDescriptor
        {
            Name = name,
            PluginType = pluginType ?? typeof(object),
            ProvidedServices = providedServices ?? [],
            ExplicitDependencies = explicitDeps ?? [],
            RequiredServices = requiredServices ?? [],
            OptionalServices = optionalServices ?? [],
            Configurations = [],
            Assembly = Assembly.GetExecutingAssembly()
        };
    }

    [Fact]
    public void Sort_NoDependencies_ReturnsAll()
    {
        var plugins = new[]
        {
            MakeDescriptor("A"),
            MakeDescriptor("B"),
            MakeDescriptor("C"),
        };

        var sorted = PluginDependencySorter.Sort(plugins);
        Assert.Equal(3, sorted.Count);
    }

    [Fact]
    public void Sort_ExplicitDependency_OrdersCorrectly()
    {
        var typeA = typeof(string);
        var typeB = typeof(int);

        var a = MakeDescriptor("A", pluginType: typeA);
        var b = MakeDescriptor("B", pluginType: typeB, explicitDeps: [typeA]);

        var sorted = PluginDependencySorter.Sort([b, a]); // B first, should be reordered
        Assert.Equal("A", sorted[0].Name);
        Assert.Equal("B", sorted[1].Name);
    }

    [Fact]
    public void Sort_ServiceDependency_OrdersProviderFirst()
    {
        var serviceType = typeof(IDisposable);
        var typeA = typeof(string);

        var provider = MakeDescriptor("Provider", pluginType: typeA,
            providedServices: [serviceType]);
        var consumer = MakeDescriptor("Consumer",
            requiredServices: [serviceType]);

        var sorted = PluginDependencySorter.Sort([consumer, provider]);
        Assert.Equal("Provider", sorted[0].Name);
        Assert.Equal("Consumer", sorted[1].Name);
    }

    [Fact]
    public void Sort_MissingRequired_Throws()
    {
        var serviceType = typeof(IDisposable);
        var consumer = MakeDescriptor("Consumer",
            requiredServices: [serviceType]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => PluginDependencySorter.Sort([consumer]));
        Assert.Contains("Consumer", ex.Message);
        Assert.Contains("IDisposable", ex.Message);
    }

    [Fact]
    public void Sort_MissingOptional_DoesNotThrow()
    {
        var serviceType = typeof(IDisposable);
        var consumer = MakeDescriptor("Consumer",
            optionalServices: [serviceType]);

        var sorted = PluginDependencySorter.Sort([consumer]);
        Assert.Single(sorted);
    }

    [Fact]
    public void Sort_DuplicateServiceProvider_Throws()
    {
        var serviceType = typeof(IDisposable);
        var a = MakeDescriptor("A", providedServices: [serviceType]);
        var b = MakeDescriptor("B", providedServices: [serviceType]);

        Assert.Throws<InvalidOperationException>(
            () => PluginDependencySorter.Sort([a, b]));
    }

    [Fact]
    public void Sort_Cycle_Throws()
    {
        var typeA = typeof(string);
        var typeB = typeof(int);

        var a = MakeDescriptor("A", pluginType: typeA, explicitDeps: [typeB]);
        var b = MakeDescriptor("B", pluginType: typeB, explicitDeps: [typeA]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => PluginDependencySorter.Sort([a, b]));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
