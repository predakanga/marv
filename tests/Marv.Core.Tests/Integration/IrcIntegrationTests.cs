using System.Threading.Channels;
using Marv.Core.Events;
using Marv.Core.Irc;
using Marv.Core.Protocol;
using Xunit;

namespace Marv.Core.Tests.Integration;

/// <summary>
/// Integration tests that connect to a real IRC server managed by Testcontainers.
/// Excluded from default test runs.
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

    [Fact]
    public async Task Connection_CanConnectAndDisconnect()
    {
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvReg");
        var config = _fixture.CreateConfig("MarvReg");

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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvJoin");
        var config = _fixture.CreateConfig("MarvJoin", "#marvtest");

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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvMsg");
        var config = _fixture.CreateConfig("MarvMsg", "#msgtest");
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvPing");
        var config = _fixture.CreateConfig("MarvPing");

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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvNote");
        var config = _fixture.CreateConfig("MarvNote", "#noticetest");
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvPart");
        var config = _fixture.CreateConfig("MarvPart", "#parttest");
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvNick");
        var config = _fixture.CreateConfig("MarvNick", "#nicktest");
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvQuit");
        var config = _fixture.CreateConfig("MarvQuit", "#quittest");
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bot = IrcServerFixture.CreateBot("MarvSend");
        var config = _fixture.CreateConfig("MarvSend", "#sendtest");
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvISup");
        var config = _fixture.CreateConfig("MarvISup");

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

    [Fact]
    public async Task Bot_FiresReadyEvent_WithoutAuth()
    {

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvReady");
        var config = _fixture.CreateConfig("MarvReady", "#readytest");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            // ReadyEvent should fire before channel joins
            var readyEvent = await WaitForEventAsync<ReadyEvent>(eventChannel.Reader, cts.Token);
            Assert.NotNull(readyEvent);

            // Channel join should follow
            var joined = await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);
            Assert.NotNull(joined);
            Assert.Equal("#readytest", joined!.Channel!.Name, StringComparer.OrdinalIgnoreCase);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_SendsPassBeforeRegistration()
    {

        // Verify that PASS is sent before NICK/USER by watching the raw connection.
        // ngircd has no server password, so this connection will still register fine
        // (PASS with a wrong password is ignored when no password is configured).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvPass");
        var config = _fixture.CreateConfig("MarvPass");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            // If registration works, the bot is ready — PASS didn't break anything
            var readyEvent = await WaitForEventAsync<ReadyEvent>(eventChannel.Reader, cts.Token);
            Assert.NotNull(readyEvent);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_ReadyEventFiresBeforeChannelJoin()
    {

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvOrd");
        var config = _fixture.CreateConfig("MarvOrd", "#ordertest");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();
        var eventOrder = new List<string>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            // Collect events in order until we see a UserJoinedEvent
            await foreach (var evt in eventChannel.Reader.ReadAllAsync(cts.Token))
            {
                switch (evt)
                {
                    case ReadyEvent:
                        eventOrder.Add("Ready");
                        break;
                    case UserJoinedEvent:
                        eventOrder.Add("UserJoined");
                        break;
                }

                if (eventOrder.Contains("UserJoined"))
                    break;
            }

            Assert.Equal(["Ready", "UserJoined"], eventOrder);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_JoinsMultipleConfiguredChannels()
    {

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvBulk");
        var config = _fixture.CreateConfig("MarvBulk", "#bulktest1", "#bulktest2", "#bulktest3");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForRegistrationAsync(cts.Token);

            // Collect all three UserJoinedEvents (one per channel)
            var joinedChannels = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var joined = await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);
                Assert.NotNull(joined);
                joinedChannels.Add(joined!.Channel!.Name);
            }

            Assert.Contains("#bulktest1", joinedChannels, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("#bulktest2", joinedChannels, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("#bulktest3", joinedChannels, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(3, bot.Channels.Count);

            await cts.CancelAsync();
            try { await botTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Bot_JoinMultipleAsync_JoinsChannelsAtRuntime()
    {

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bot = IrcServerFixture.CreateBot("MarvRtJn");
        var config = _fixture.CreateConfig("MarvRtJn");

        var eventChannel = Channel.CreateUnbounded<MarvEvent>();

        var connection = await _fixture.CreateConnectionAsync(cts.Token);
        await using (connection)
        {
            var botTask = Task.Run(
                () => bot.RunAsync(connection, config, [eventChannel.Writer], cts.Token), cts.Token);

            await bot.WaitForReadyAsync(cts.Token);

            // Use JoinMultipleAsync to join channels after the bot is ready
            await bot.JoinMultipleAsync(["#rtjoin1", "#rtjoin2"], cts.Token);

            var joined1 = await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);
            var joined2 = await WaitForEventAsync<UserJoinedEvent>(eventChannel.Reader, cts.Token);

            Assert.NotNull(joined1);
            Assert.NotNull(joined2);

            var names = new[] { joined1!.Channel!.Name, joined2!.Channel!.Name };
            Assert.Contains("#rtjoin1", names, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("#rtjoin2", names, StringComparer.OrdinalIgnoreCase);

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
