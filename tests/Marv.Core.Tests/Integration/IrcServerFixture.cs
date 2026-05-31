using System.Net.Sockets;
using Marv.Core.Irc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marv.Core.Tests.Integration;

/// <summary>
/// Shared fixture that verifies the local IRC server is reachable.
/// Tests using this fixture are skipped when the server is not available.
/// </summary>
public class IrcServerFixture : IAsyncLifetime
{
    public const string Host = "localhost";
    public const int Port = 6667;

    /// <summary>True if the IRC server was reachable during setup.</summary>
    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        IsAvailable = await ProbeServerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
    public static MarvConfiguration CreateConfig(string nick = "MarvTest", params string[] channels)
    {
        return new MarvConfiguration
        {
            Server = Host,
            Port = Port,
            UseTls = false,
            Nick = nick,
            User = "marvtest",
            RealName = "Marv Integration Test",
            Channels = channels.ToList()
        };
    }

    private static async Task<bool> ProbeServerAsync()
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(Host, Port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
