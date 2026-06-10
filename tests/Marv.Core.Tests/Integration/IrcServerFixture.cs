using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Marv.Core.Irc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marv.Core.Tests.Integration;

/// <summary>
/// Shared fixture that starts an ngircd container via Testcontainers.
/// The container is started once and shared across all integration tests
/// via the <see cref="IrcServerCollection"/> collection fixture.
/// </summary>
public class IrcServerFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("linuxserver/ngircd")
        .WithPortBinding(6667, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilInternalTcpPortIsAvailable(6667))
        .Build();

    /// <summary>The hostname to connect to the IRC server.</summary>
    public string Host => _container.Hostname;

    /// <summary>The mapped host port for the IRC server.</summary>
    public int Port => _container.GetMappedPublicPort(6667);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a connected <see cref="IrcConnection"/> to the test server.
    /// Caller is responsible for disposing.
    /// </summary>
    internal async Task<IrcConnection> CreateConnectionAsync(CancellationToken ct)
    {
        var connection = new IrcConnection(NullLogger.Instance);
        await connection.ConnectAsync(Host, Port, useTls: false, ct);
        return connection;
    }

    /// <summary>
    /// Creates a fully configured <see cref="IrcBot"/> with the given nick.
    /// </summary>
    internal static IrcBot CreateBot(string nick = "MarvTest")
    {
        var serverInfo = new ServerInfo();
        var capManager = new CapabilityManager();
        var logger = NullLogger<IrcBot>.Instance;
        return new IrcBot(logger, serverInfo, capManager);
    }

    /// <summary>
    /// Creates a <see cref="MarvConfiguration"/> suitable for testing.
    /// </summary>
    public MarvConfiguration CreateConfig(string nick = "MarvTest", params string[] channels)
    {
        return new MarvConfiguration
        {
            Server = Host,
            Port = Port,
            UseTls = false,
            Nick = nick,
            User = "marvtest",
            RealName = "Marv Integration Test",
            Channels = channels.ToArray()
        };
    }
}
