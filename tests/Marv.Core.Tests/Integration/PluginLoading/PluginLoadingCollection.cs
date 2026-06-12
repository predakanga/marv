using Xunit;

namespace Marv.Core.Tests.Integration.PluginLoading;

/// <summary>
/// Collection definition that shares a single <see cref="PublishedOutputFixture"/>
/// across all plugin loading integration tests, so the publish step runs only once.
/// </summary>
[CollectionDefinition("PluginLoading")]
public class PluginLoadingCollection : ICollectionFixture<PublishedOutputFixture>;
