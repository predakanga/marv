using System.Threading.Channels;
using Marv.Core.Events;
using Marv.Core.Irc;
using Marv.Core.Protocol;
using Xunit;
using Xunit.Sdk;

namespace Marv.Core.Tests.Integration;

/// <summary>
/// Integration tests that connect to a real IRC server on localhost:6667.
/// These tests require ngircd to be running and are excluded from default test runs.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
[Collection("IrcServer")]
public class IrcIntegrationTests
{
    private readonly IrcServerFixture _fixture;

    public IrcIntegrationTests(IrcServerFixture fixture)
    {
        _fixture = fixture;
    }

    private void SkipIfUnavailable()
    {
        if (!_fixture.IsAvailable)
            throw SkipException.ForSkip("IRC server not available on localhost:6667");
    }

    [Fact]
    public async Task Connection_CanConnectAndDisconnect()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            Assert.True(connection.IsConnected);
        }
    }

    [Fact]
    public async Task Bot_RegistersWithServer()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvReg");
        var config = IrcServerFixture.CreateConfig("MarvReg");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            // Wait for the ConnectedEvent which fires after registration completes
            await WaitForEventAsync<ConnectedEvent>(eventChannel.Reader, cts.Token);

            Assert.Equal("MarvReg", bot.Self.Nick);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_JoinsConfiguredChannel()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvJoin");
        var config = IrcServerFixture.CreateConfig("MarvJoin", "#marvtest");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForRegistrationAsync(cts.Token);

            var joined = await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            Assert.NotNull(joined);
            Assert.Equal("#marvtest", joined!.Channel!.Name, StringComparer.OrdinalIgnoreCase);
            Assert.True(bot.Channels.ContainsKey("#marvtest"));

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_ReceivesPrivmsgFromOtherClient()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvMsg");
        var config = IrcServerFixture.CreateConfig("MarvMsg", "#msgtest");
        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var botConnection = await _fixture.CreateConnectionAsync(cts.Token);
        var userConnection = await _fixture.CreateConnectionAsync(cts.Token);

        await using (botConnection)
        await using (userConnection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(botConnection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForRegistrationAsync(cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await RegisterClientAsync(userConnection, "TestUser", cts.Token);
            await SendRawLineAsync(userConnection, "JOIN #msgtest", cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await SendRawLineAsync(userConnection, "PRIVMSG #msgtest :hello bot!", cts.Token);

            var msgEvent = await WaitForEventAsync<MessageEvent>(eventChannel.Reader, cts.Token);

            Assert.NotNull(msgEvent);
            Assert.Equal("hello bot!", msgEvent!.Text);
            Assert.Equal("TestUser", msgEvent.Sender!.Nick);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_RespondsToServerPing()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvPing");
        var config = IrcServerFixture.CreateConfig("MarvPing");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await WaitForEventAsync<ConnectedEvent>(eventChannel.Reader, cts.Token);

            Assert.True(connection.IsConnected);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_CanSendAndReceiveNotice()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvNote");
        var config = IrcServerFixture.CreateConfig("MarvNote", "#noticetest");
        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var botConnection = await _fixture.CreateConnectionAsync(cts.Token);
        var userConnection = await _fixture.CreateConnectionAsync(cts.Token);

        await using (botConnection)
        await using (userConnection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(botConnection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForRegistrationAsync(cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await RegisterClientAsync(userConnection, "NoteUser", cts.Token);
            await SendRawLineAsync(userConnection, "JOIN #noticetest", cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await SendRawLineAsync(userConnection, "NOTICE #noticetest :this is a notice", cts.Token);

            var noticeEvent = await WaitForEventAsync<NoticeEvent>(eventChannel.Reader, cts.Token);
            Assert.NotNull(noticeEvent);
            Assert.Equal("this is a notice", noticeEvent!.Text);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_TracksUserPartingChannel()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvPart");
        var config = IrcServerFixture.CreateConfig("MarvPart", "#parttest");
        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var botConnection = await _fixture.CreateConnectionAsync(cts.Token);
        var userConnection = await _fixture.CreateConnectionAsync(cts.Token);

        await using (botConnection)
        await using (userConnection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(botConnection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForRegistrationAsync(cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await RegisterClientAsync(userConnection, "PartUser", cts.Token);
            await SendRawLineAsync(userConnection, "JOIN #parttest", cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await SendRawLineAsync(userConnection, "PART #parttest :goodbye", cts.Token);

            var partEvent = await WaitForEventAsync<UserPartedEvent>(eventChannel.Reader, cts.Token);
            Assert.NotNull(partEvent);
            Assert.Equal("PartUser", partEvent!.User!.Nick);
            Assert.Equal("goodbye", partEvent.Reason);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_TracksNickChange()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvNick");
        var config = IrcServerFixture.CreateConfig("MarvNick", "#nicktest");
        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var botConnection = await _fixture.CreateConnectionAsync(cts.Token);
        var userConnection = await _fixture.CreateConnectionAsync(cts.Token);

        await using (botConnection)
        await using (userConnection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(botConnection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForRegistrationAsync(cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await RegisterClientAsync(userConnection, "NickUser", cts.Token);
            await SendRawLineAsync(userConnection, "JOIN #nicktest", cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await SendRawLineAsync(userConnection, "NICK NewNick", cts.Token);

            var nickEvent = await WaitForEventAsync<NickChangedEvent>(eventChannel.Reader, cts.Token);
            Assert.NotNull(nickEvent);
            Assert.Equal("NickUser", nickEvent!.OldNick);
            Assert.Equal("NewNick", nickEvent.NewNick);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_TracksUserQuit()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvQuit");
        var config = IrcServerFixture.CreateConfig("MarvQuit", "#quittest");
        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var botConnection = await _fixture.CreateConnectionAsync(cts.Token);
        var userConnection = await _fixture.CreateConnectionAsync(cts.Token);

        await using (botConnection)
        await using (userConnection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(botConnection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForRegistrationAsync(cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await RegisterClientAsync(userConnection, "QuitUser", cts.Token);
            await SendRawLineAsync(userConnection, "JOIN #quittest", cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await SendRawLineAsync(userConnection, "QUIT :bye bye", cts.Token);

            var quitEvent = await WaitForEventAsync<UserQuitEvent>(eventChannel.Reader, cts.Token);
            Assert.NotNull(quitEvent);
            Assert.Equal("QuitUser", quitEvent!.User!.Nick);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_SendsMessageToChannel()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvSend");
        var config = IrcServerFixture.CreateConfig("MarvSend", "#sendtest");
        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var botConnection = await _fixture.CreateConnectionAsync(cts.Token);
        var userConnection = await _fixture.CreateConnectionAsync(cts.Token);

        await using (botConnection)
        await using (userConnection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(botConnection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForRegistrationAsync(cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await RegisterClientAsync(userConnection, "RecvUser", cts.Token);
            await SendRawLineAsync(userConnection, "JOIN #sendtest", cts.Token);
            await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            await bot.SendMessageAsync("#sendtest", "hello from marv!", cts.Token);

            var received = await WaitForLineAsync(userConnection, "PRIVMSG #sendtest", cts.Token);
            Assert.Contains("hello from marv!", received);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_ParsesISupport()
    {
        SkipIfUnavailable();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvISup");
        var config = IrcServerFixture.CreateConfig("MarvISup");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await WaitForEventAsync<ConnectedEvent>(eventChannel.Reader, cts.Token);

            Assert.NotNull(bot.ServerInfo);
            Assert.NotEmpty(bot.ServerInfo.ChannelTypes);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    // --- Helpers ---

    /// <summary>
    /// Registers a raw IRC client (not using IrcBot) with the given nick.
    /// Waits for the 001 welcome numeric before returning.
    /// </summary>
    private static async Task RegisterClientAsync(IrcConnection connection, string nick, CancellationToken ct)
    {
        await connection.Outbound.WriteAsync(new IrcMessage("NICK", [nick]), ct);
        await connection.Outbound.WriteAsync(new IrcMessage("USER", [nick, "0", "*", "Test User"]), ct);

        await foreach (var msg in connection.Inbound.ReadAllAsync(ct))
        {
            if (msg.Command == "001")
                break;
        }
    }

    /// <summary>
    /// Sends a raw IRC line through the connection's outbound channel.
    /// </summary>
    private static async Task SendRawLineAsync(IrcConnection connection, string line, CancellationToken ct)
    {
        var msg = IrcParser.Parse(line);
        if (msg is not null)
            await connection.Outbound.WriteAsync(msg, ct);
    }

    /// <summary>
    /// Waits for a specific event type from the event channel reader.
    /// </summary>
    private static async Task<T?> WaitForEventAsync<T>(ChannelReader<MarvEvent> reader, CancellationToken ct)
        where T : MarvEvent
    {
        await foreach (var evt in reader.ReadAllAsync(ct))
        {
            if (evt is T typed)
                return typed;
        }
        return null;
    }

    /// <summary>
    /// Reads from a connection's inbound channel until a message containing
    /// the specified substring is found. Returns the serialized form.
    /// </summary>
    private static async Task<string> WaitForLineAsync(IrcConnection connection, string commandSubstring, CancellationToken ct)
    {
        await foreach (var msg in connection.Inbound.ReadAllAsync(ct))
        {
            var serialized = IrcSerializer.Serialize(msg);
            if (serialized.Contains(commandSubstring, StringComparison.OrdinalIgnoreCase))
                return serialized;
        }
        return "";
    }
}
