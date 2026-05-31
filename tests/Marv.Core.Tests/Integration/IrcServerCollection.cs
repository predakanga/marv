using Xunit;

namespace Marv.Core.Tests.Integration;

/// <summary>
/// Collection definition that shares a single <see cref="IrcServerFixture"/>
/// across all integration tests, avoiding redundant server probes.
/// </summary>
[CollectionDefinition("IrcServer")]
public class IrcServerCollection : ICollectionFixture<IrcServerFixture>;
