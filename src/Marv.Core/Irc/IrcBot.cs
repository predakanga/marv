using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace Marv.Core.Irc;

/// <summary>
/// Implements <see cref="IBot"/> and runs the message processor loop.
/// Handles PING/PONG, CAP negotiation, SASL, ISUPPORT processing, state tracking,
/// event translation, and fan-out to plugin event channels.
/// </summary>
internal sealed class IrcBot : IBot
{
    private readonly ILogger<IrcBot> _logger;
    private readonly ServerInfo _serverInfo;
    private readonly CapabilityManager _capabilityManager;
    private IrcConnection? _connection;

    private readonly ConcurrentDictionary<string, IrcUser> _users;
    private readonly ConcurrentDictionary<string, IrcChannel> _channels;

    private IrcUser _self;
    private string _currentNick;
    private MarvConfiguration _config = new();

    // CAP negotiation state
    private TaskCompletionSource<bool>? _registrationTcs;
    private readonly HashSet<string> _pendingCaps = new(StringComparer.OrdinalIgnoreCase);
    private bool _saslInProgress;

    // Post-registration auth state
    private TaskCompletionSource<bool>? _readyTcs;
    private bool _nickServPending;
    private bool _operPending;

    // Labeled response correlation
    private int _labelCounter;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<IrcMessage>>> _pendingLabels = new();
    private readonly ConcurrentDictionary<string, List<IrcMessage>> _labelBuffers = new();

    // Plugin event dispatch
    private IReadOnlyList<ChannelWriter<MarvEvent>> _eventWriters = [];

    // Capabilities the bot wants to negotiate
    private static readonly string[] DesiredCapabilities =
    [
        Platform.Capabilities.MultiPrefix,
        Platform.Capabilities.ExtendedJoin,
        Platform.Capabilities.AccountTag,
        Platform.Capabilities.AccountNotify,
        Platform.Capabilities.AwayNotify,
        Platform.Capabilities.Chghost,
        Platform.Capabilities.Setname,
        Platform.Capabilities.ServerTime,
        Platform.Capabilities.Batch,
        Platform.Capabilities.LabeledResponse,
        Platform.Capabilities.EchoMessage,
        Platform.Capabilities.UserhostInNames,
        Platform.Capabilities.CapNotify,
        Platform.Capabilities.MessageTags,
        Platform.Capabilities.InviteNotify,
        Platform.Capabilities.Sasl,
        Platform.Capabilities.StandardReplies
    ];

    public IrcBot(ILogger<IrcBot> logger, ServerInfo serverInfo, CapabilityManager capabilityManager)
    {
        _logger = logger;
        _serverInfo = serverInfo;
        _capabilityManager = capabilityManager;
        _currentNick = "Marv";

        var comparer = CaseMapping.GetComparer(_serverInfo.CaseMapping);
        _users = new ConcurrentDictionary<string, IrcUser>(comparer);
        _channels = new ConcurrentDictionary<string, IrcChannel>(comparer);
        _self = new IrcUser(_currentNick, comparer);
    }

    // --- IBot implementation ---

    /// <inheritdoc />
    public IUser Self => _self;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IChannel> Channels
    {
        get
        {
            var dict = new Dictionary<string, IChannel>(
                CaseMapping.GetComparer(_serverInfo.CaseMapping));
            foreach (var kvp in _channels)
                dict[kvp.Key] = kvp.Value;
            return dict;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IUser> Users
    {
        get
        {
            var dict = new Dictionary<string, IUser>(
                CaseMapping.GetComparer(_serverInfo.CaseMapping));
            foreach (var kvp in _users)
                dict[kvp.Key] = kvp.Value;
            return dict;
        }
    }

    /// <inheritdoc />
    public IServerInfo ServerInfo => _serverInfo;

    /// <inheritdoc />
    public ICapabilityManager Capabilities => _capabilityManager;

    /// <inheritdoc />
    public string CommandPrefix => _config.CommandPrefix;

    /// <inheritdoc />
    public async Task SendMessageAsync(string target, string text, CancellationToken ct)
    {
        await SendRawAsync(new IrcMessage("PRIVMSG", [target, text]), ct);
    }

    /// <inheritdoc />
    public async Task SendNoticeAsync(string target, string text, CancellationToken ct)
    {
        await SendRawAsync(new IrcMessage("NOTICE", [target, text]), ct);
    }

    /// <inheritdoc />
    public async Task SendActionAsync(string target, string text, CancellationToken ct)
    {
        await SendRawAsync(new IrcMessage("PRIVMSG", [target, $"\x01ACTION {text}\x01"]), ct);
    }

    /// <inheritdoc />
    public async Task SendRawAsync(IrcMessage message, CancellationToken ct)
    {
        if (_connection is null)
            throw new InvalidOperationException("Not connected.");

        await _connection.Outbound.WriteAsync(message, ct);
    }

    /// <inheritdoc />
    public async Task JoinAsync(string channel, string? key, CancellationToken ct)
    {
        if (key is not null)
            await SendRawAsync(new IrcMessage("JOIN", [channel, key]), ct);
        else
            await SendRawAsync(new IrcMessage("JOIN", [channel]), ct);
    }

    /// <inheritdoc />
    public async Task JoinMultipleAsync(IReadOnlyList<string> channels, CancellationToken ct)
    {
        if (channels.Count == 0) return;

        foreach (var batch in BatchChannels(channels))
        {
            var joined = string.Join(',', batch);
            await SendRawAsync(new IrcMessage("JOIN", [joined]), ct);
        }
    }

    /// <summary>
    /// Splits a list of channel names into batches that fit within the
    /// 512-byte IRC line length limit. "JOIN " = 5 bytes, "\r\n" = 2 bytes,
    /// leaving 505 bytes for the comma-separated channel list.
    /// </summary>
    internal static IEnumerable<List<string>> BatchChannels(
        IReadOnlyList<string> channels, int maxPayloadLength = 505)
    {
        var batch = new List<string>();
        var currentLength = 0;

        foreach (var channel in channels)
        {
            var addedLength = batch.Count == 0
                ? channel.Length
                : channel.Length + 1; // +1 for comma separator

            if (currentLength + addedLength > maxPayloadLength && batch.Count > 0)
            {
                yield return batch;
                batch = [];
                currentLength = 0;
                addedLength = channel.Length;
            }

            batch.Add(channel);
            currentLength += addedLength;
        }

        if (batch.Count > 0)
            yield return batch;
    }

    /// <inheritdoc />
    public async Task PartAsync(string channel, string? reason, CancellationToken ct)
    {
        if (reason is not null)
            await SendRawAsync(new IrcMessage("PART", [channel, reason]), ct);
        else
            await SendRawAsync(new IrcMessage("PART", [channel]), ct);
    }

    /// <inheritdoc />
    public async Task KickAsync(string channel, string nick, string? reason, CancellationToken ct)
    {
        if (reason is not null)
            await SendRawAsync(new IrcMessage("KICK", [channel, nick, reason]), ct);
        else
            await SendRawAsync(new IrcMessage("KICK", [channel, nick]), ct);
    }

    /// <inheritdoc />
    public async Task SetTopicAsync(string channel, string topic, CancellationToken ct)
    {
        await SendRawAsync(new IrcMessage("TOPIC", [channel, topic]), ct);
    }

    /// <inheritdoc />
    public async Task InviteAsync(string nick, string channel, CancellationToken ct)
    {
        await SendRawAsync(new IrcMessage("INVITE", [nick, channel]), ct);
    }

    /// <inheritdoc />
    public async Task SetModeAsync(string target, string modeString, CancellationToken ct)
    {
        await SendRawAsync(new IrcMessage("MODE", [target, modeString]), ct);
    }

    /// <inheritdoc />
    public async Task SetModeAsync(string target, string modeString, string parameter, CancellationToken ct)
    {
        await SendRawAsync(new IrcMessage("MODE", [target, modeString, parameter]), ct);
    }

    /// <inheritdoc />
    public Task GiveOpAsync(string channel, string nick, CancellationToken ct) =>
        SetModeAsync(channel, "+o", nick, ct);

    /// <inheritdoc />
    public Task RemoveOpAsync(string channel, string nick, CancellationToken ct) =>
        SetModeAsync(channel, "-o", nick, ct);

    /// <inheritdoc />
    public Task GiveVoiceAsync(string channel, string nick, CancellationToken ct) =>
        SetModeAsync(channel, "+v", nick, ct);

    /// <inheritdoc />
    public Task RemoveVoiceAsync(string channel, string nick, CancellationToken ct) =>
        SetModeAsync(channel, "-v", nick, ct);

    /// <inheritdoc />
    public async Task ChangeNickAsync(string newNick, CancellationToken ct)
    {
        await SendRawAsync(new IrcMessage("NICK", [newNick]), ct);
    }

    /// <inheritdoc />
    public IEqualityComparer<string> CaseComparer =>
        CaseMapping.GetComparer(_serverInfo.CaseMapping);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IrcMessage>> SendAndAwaitAsync(IrcMessage message, CancellationToken ct)
    {
        if (_capabilityManager.IsNegotiated(Platform.Capabilities.LabeledResponse))
        {
            var label = $"marv-{Interlocked.Increment(ref _labelCounter)}";
            var tags = new Dictionary<string, string?>(message.Tags) { ["label"] = label };
            var labeled = new IrcMessage(tags.AsReadOnly(), message.Source, message.Command, message.Parameters);

            var tcs = new TaskCompletionSource<IReadOnlyList<IrcMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingLabels[label] = tcs;
            _labelBuffers[label] = [];

            await SendRawAsync(labeled, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _pendingLabels.TryRemove(label, out _);
                _labelBuffers.TryRemove(label, out _);
                throw new TimeoutException($"Timed out waiting for labeled response '{label}'.");
            }
        }
        else
        {
            // Fallback: just send and return empty (no correlation available)
            await SendRawAsync(message, ct);
            return [];
        }
    }

    // --- Connection lifecycle ---

    /// <summary>
    /// Connects to the IRC server, performs registration (CAP, NICK, USER),
    /// and runs the message processor until disconnected or cancelled.
    /// </summary>
    public async Task RunAsync(IrcConnection connection, MarvConfiguration config,
        IReadOnlyList<ChannelWriter<MarvEvent>> eventWriters, CancellationToken ct)
    {
        _connection = connection;
        _config = config;
        _eventWriters = eventWriters;
        _currentNick = config.Nick;
        _saslInProgress = false;
        _pendingCaps.Clear();

        var comparer = CaseMapping.GetComparer(_serverInfo.CaseMapping);
        _self = new IrcUser(_currentNick, comparer);
        _users.Clear();
        _channels.Clear();

        _registrationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _nickServPending = false;
        _operPending = false;

        // Send server password before anything else
        if (!string.IsNullOrEmpty(config.ServerPassword))
        {
            await SendRawAsync(new IrcMessage("PASS", [config.ServerPassword]), ct);
        }

        // Begin CAP negotiation and registration
        await SendRawAsync(new IrcMessage("CAP", ["LS", "302"]), ct);
        await SendRawAsync(new IrcMessage("NICK", [config.Nick]), ct);
        await SendRawAsync(new IrcMessage("USER", [config.User, "0", "*", config.RealName]), ct);

        // Process messages
        await ProcessMessagesAsync(ct);
    }

    /// <summary>
    /// Resets state on disconnection — clears users, channels, pending labels.
    /// </summary>
    public void ResetState()
    {
        _users.Clear();
        _channels.Clear();
        _serverInfo.Reset();
        _capabilityManager.Reset();

        foreach (var kvp in _pendingLabels)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingLabels.Clear();
        _labelBuffers.Clear();

        _readyTcs?.TrySetCanceled();
        _readyTcs = null;
        _nickServPending = false;
        _operPending = false;

        _eventWriters = [];
        _connection = null;
    }

    // --- Message processor ---

    private async Task ProcessMessagesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var message in _connection!.Inbound.ReadAllAsync(ct))
            {
                try
                {
                    await ProcessMessageAsync(message, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message: {Command}", message.Command);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    private async Task ProcessMessageAsync(IrcMessage message, CancellationToken ct)
    {
        // Check for labeled-response correlation
        if (message.Tags.TryGetValue("label", out var label) && label is not null
            && _labelBuffers.TryGetValue(label, out var buffer))
        {
            buffer.Add(message);

            // ACK with label means the response is complete
            if (message.Command == "ACK" && _pendingLabels.TryRemove(label, out var tcs))
            {
                _labelBuffers.TryRemove(label, out _);
                tcs.TrySetResult(buffer);
                return;
            }
        }

        // Dispatch RawMessageEvent before typed events
        var rawEvent = CreateRawEvent(message);
        await FanOutEventAsync(rawEvent, ct);

        switch (message.Command)
        {
            case "PING":
                await HandlePing(message, ct);
                break;

            case "PONG":
                break; // Ignore server PONGs

            case "CAP":
                await HandleCap(message, ct);
                break;

            case "AUTHENTICATE":
                await HandleAuthenticate(message, ct);
                break;

            // Registration numerics
            case "001": // RPL_WELCOME
                await HandleWelcome(message, ct);
                break;
            case "005": // RPL_ISUPPORT
                HandleISupport(message);
                break;
            case "375": // RPL_MOTDSTART
                _serverInfo.BeginMotd();
                break;
            case "372": // RPL_MOTD
                _serverInfo.AppendMotdLine(message.Parameters.Count > 1
                    ? message.Parameters[1]
                    : "");
                break;
            case "376": // RPL_ENDOFMOTD
            case "422": // ERR_NOMOTD
                await HandleEndOfMotd(message, ct);
                break;

            // SASL numerics
            case "900": // RPL_LOGGEDIN
                HandleSaslSuccess(message);
                break;
            case "903": // RPL_SASLSUCCESS
                await HandleSaslComplete(message, ct);
                break;
            case "902": // ERR_NICKLOCKED
            case "904": // ERR_SASLFAIL
            case "905": // ERR_SASLTOOLONG
            case "906": // ERR_SASLABORTED
            case "907": // ERR_SASLALREADY
                await HandleSaslFailed(message, ct);
                break;

            // Channel/user state
            case "PRIVMSG":
                await HandlePrivmsg(message, ct);
                break;
            case "NOTICE":
                await HandleNotice(message, ct);
                break;
            case "JOIN":
                await HandleJoin(message, ct);
                break;
            case "PART":
                await HandlePart(message, ct);
                break;
            case "KICK":
                await HandleKick(message, ct);
                break;
            case "QUIT":
                await HandleQuit(message, ct);
                break;
            case "NICK":
                await HandleNick(message, ct);
                break;
            case "MODE":
                await HandleMode(message, ct);
                break;
            case "TOPIC":
                await HandleTopic(message, ct);
                break;
            case "INVITE":
                await HandleInvite(message, ct);
                break;
            case "ACCOUNT":
                await HandleAccount(message, ct);
                break;
            case "AWAY":
                await HandleAway(message, ct);
                break;
            case "CHGHOST":
                await HandleChghost(message, ct);
                break;
            case "BATCH":
                await HandleBatch(message, ct);
                break;

            // NAMES replies
            case "353": // RPL_NAMREPLY
                HandleNamesReply(message);
                break;
            case "366": // RPL_ENDOFNAMES
                break;

            // TOPIC replies
            case "332": // RPL_TOPIC
                HandleTopicReply(message);
                break;
            case "333": // RPL_TOPICWHOTIME
                HandleTopicWhoTime(message);
                break;

            // Channel creation time
            case "329": // RPL_CREATIONTIME
                HandleCreationTime(message);
                break;

            // OPER numerics
            case "381": // RPL_YOUREOPER
                HandleOperSuccess(message);
                break;
            case "464": // ERR_PASSWDMISMATCH
            case "491": // ERR_NOOPERHOST
                HandleOperFailed(message);
                break;

            // Nick errors
            case "431": // ERR_NONICKNAMEGIVEN
            case "432": // ERR_ERRONEUSNICKNAME
            case "433": // ERR_NICKNAMEINUSE
            case "436": // ERR_NICKCOLLISION
                await HandleNickError(message, ct);
                break;

            default:
                _logger.LogDebug("Unhandled command: {Command}", message.Command);
                break;
        }
    }

    // --- Protocol handlers ---

    private async Task HandlePing(IrcMessage message, CancellationToken ct)
    {
        var param = message.Parameters.Count > 0 ? message.Parameters[0] : "";
        await SendRawAsync(new IrcMessage("PONG", [param]), ct);
    }

    private async Task HandleCap(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count < 3) return;

        var subCommand = message.Parameters[1].ToUpperInvariant();
        var isMultiLine = message.Parameters.Count > 3 && message.Parameters[2] == "*";
        var capList = message.Parameters[isMultiLine ? 3 : 2];

        switch (subCommand)
        {
            case "LS":
                foreach (var cap in ParseCapList(capList))
                {
                    _capabilityManager.SetAvailable(cap.Key, cap.Value);
                }

                if (!isMultiLine)
                {
                    // All capabilities listed — request the ones we want
                    var toRequest = DesiredCapabilities
                        .Where(c => _capabilityManager.IsAvailable(c))
                        .ToList();

                    if (toRequest.Count > 0)
                    {
                        _logger.LogInformation("Requesting capabilities: {Caps}",
                            string.Join(", ", toRequest));
                        await SendRawAsync(new IrcMessage("CAP", ["REQ", string.Join(' ', toRequest)]), ct);
                    }
                    else
                    {
                        await SendRawAsync(new IrcMessage("CAP", ["END"]), ct);

                    }
                }
                break;

            case "ACK":
                foreach (var capName in capList.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    _capabilityManager.SetNegotiated(capName);
                    _logger.LogDebug("Capability acknowledged: {Cap}", capName);
                }

                // Start SASL if negotiated and credentials are configured
                if (_capabilityManager.IsNegotiated(Platform.Capabilities.Sasl)
                    && !string.IsNullOrEmpty(_config.SaslUser) && !string.IsNullOrEmpty(_config.SaslPassword)
                    && !_saslInProgress)
                {
                    _saslInProgress = true;
                    await SendRawAsync(new IrcMessage("AUTHENTICATE", ["PLAIN"]), ct);
                }
                else
                {
                    await SendRawAsync(new IrcMessage("CAP", ["END"]), ct);

                }
                break;

            case "NAK":
                _logger.LogWarning("Capabilities rejected: {Caps}", capList);
                await SendRawAsync(new IrcMessage("CAP", ["END"]), ct);

                break;

            case "NEW":
                foreach (var cap in ParseCapList(capList))
                {
                    _capabilityManager.AddNewCapability(cap.Key, cap.Value);
                }
                var newCaps = ParseCapList(capList)
                    .Where(c => DesiredCapabilities.Contains(c.Key, StringComparer.OrdinalIgnoreCase))
                    .Select(c => c.Key)
                    .ToList();
                if (newCaps.Count > 0)
                {
                    await SendRawAsync(new IrcMessage("CAP", ["REQ", string.Join(' ', newCaps)]), ct);
                }

                var capChangedNew = CreateCapChangedEvent(message);
                await FanOutEventAsync(capChangedNew, ct);
                break;

            case "DEL":
                foreach (var capName in capList.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    _capabilityManager.RemoveCapability(capName);
                    _logger.LogDebug("Capability removed: {Cap}", capName);
                }

                var capChangedDel = CreateCapChangedEvent(message);
                await FanOutEventAsync(capChangedDel, ct);
                break;
        }
    }

    private async Task HandleAuthenticate(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count > 0 && message.Parameters[0] == "+")
        {
            // Server is ready for SASL PLAIN credentials
            var authString = $"{_config.SaslUser}\0{_config.SaslUser}\0{_config.SaslPassword}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
            await SendRawAsync(new IrcMessage("AUTHENTICATE", [encoded]), ct);
        }
    }

    private void HandleSaslSuccess(IrcMessage message)
    {
        if (message.Parameters.Count > 1)
        {
            _logger.LogInformation("SASL authentication successful: {Account}", message.Parameters[1]);
        }

        // 900 is also sent by some servers after NickServ identification
        if (_nickServPending)
        {
            _logger.LogInformation("NickServ identification confirmed via 900 numeric");
            _nickServPending = false;
            CheckAuthComplete();
        }
    }

    private async Task HandleSaslComplete(IrcMessage message, CancellationToken ct)
    {
        _saslInProgress = false;
        _logger.LogInformation("SASL authentication complete");
        await SendRawAsync(new IrcMessage("CAP", ["END"]), ct);
    }

    private async Task HandleSaslFailed(IrcMessage message, CancellationToken ct)
    {
        _saslInProgress = false;
        _logger.LogWarning("SASL authentication failed: {Numeric} {Message}",
            message.Command, message.Parameters.LastOrDefault() ?? "");
        await SendRawAsync(new IrcMessage("CAP", ["END"]), ct);
    }

    private void HandleOperSuccess(IrcMessage message)
    {
        _logger.LogInformation("IRC operator authentication successful");
        _operPending = false;
        CheckAuthComplete();
    }

    private void HandleOperFailed(IrcMessage message)
    {
        _logger.LogWarning("IRC operator authentication failed: {Numeric} {Message}",
            message.Command, message.Parameters.LastOrDefault() ?? "");
        _operPending = false;
        CheckAuthComplete();
    }

    private async Task HandleWelcome(IrcMessage message, CancellationToken ct)
    {
        // 001 gives us our actual nick in params[0]
        if (message.Parameters.Count > 0)
        {
            _currentNick = message.Parameters[0];
            _self.Nick = _currentNick;
        }

        _logger.LogInformation("Registered as {Nick}", _currentNick);
        _registrationTcs?.TrySetResult(true);

        var evt = CreateConnectedEvent(message);
        await FanOutEventAsync(evt, ct);
    }

    private void HandleISupport(IrcMessage message)
    {
        // 005 params: <nick> <token1> <token2> ... :are supported by this server
        for (var i = 1; i < message.Parameters.Count - 1; i++)
        {
            var token = message.Parameters[i];
            var eqIndex = token.IndexOf('=');
            if (eqIndex >= 0)
            {
                _serverInfo.SetToken(token[..eqIndex], token[(eqIndex + 1)..]);
            }
            else
            {
                _serverInfo.SetToken(token, null);
            }
        }
    }

    private async Task HandleEndOfMotd(IrcMessage message, CancellationToken ct)
    {
        // NickServ authentication if configured and SASL wasn't used
        if (!string.IsNullOrEmpty(_config.NickServPassword) && !_capabilityManager.IsNegotiated(Platform.Capabilities.Sasl))
        {
            _nickServPending = true;
            _logger.LogInformation("Authenticating to NickServ");
            await SendMessageAsync("NickServ", $"IDENTIFY {_config.NickServPassword}", ct);
        }

        // OPER authentication if configured
        if (!string.IsNullOrEmpty(_config.OperName) && !string.IsNullOrEmpty(_config.OperPassword))
        {
            _operPending = true;
            _logger.LogInformation("Authenticating as IRC operator: {OperName}", _config.OperName);
            await SendRawAsync(new IrcMessage("OPER", [_config.OperName, _config.OperPassword]), ct);
        }

        if (_nickServPending || _operPending)
        {
            // Wait for auth to complete (or timeout) in the background,
            // then fire ReadyEvent and join channels
            _ = CompleteAuthSequenceAsync(ct);
        }
        else
        {
            await FireReadyAndJoinAsync(message, ct);
        }
    }

    /// <summary>
    /// Waits for all pending auth steps to complete, then fires ReadyEvent and
    /// joins channels. If auth doesn't complete within the configured timeout, proceeds anyway.
    /// A timeout of 0 means wait indefinitely.
    /// </summary>
    private async Task CompleteAuthSequenceAsync(CancellationToken ct)
    {
        var timeoutSeconds = _config.AuthTimeoutSeconds;

        if (timeoutSeconds > 0)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await _readyTcs!.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Post-registration authentication timed out after {Seconds}s — proceeding", timeoutSeconds);
            }
        }
        else
        {
            await _readyTcs!.Task.WaitAsync(ct);
        }

        var syntheticMessage = new IrcMessage(null, null, "READY", []);
        await FireReadyAndJoinAsync(syntheticMessage, ct);
    }

    /// <summary>
    /// Fires the <see cref="ReadyEvent"/> and joins all configured channels.
    /// </summary>
    private async Task FireReadyAndJoinAsync(IrcMessage message, CancellationToken ct)
    {
        _readyTcs?.TrySetResult(true);

        // Set bot user mode if the server advertises the BOT ISUPPORT token.
        // The token value is the mode character (e.g. BOT=B).
        var botModeChar = _serverInfo.GetValue("BOT");
        if (!string.IsNullOrEmpty(botModeChar))
        {
            _logger.LogDebug("Setting bot mode +{Mode}", botModeChar);
            await SendRawAsync(new IrcMessage("MODE", [_currentNick, $"+{botModeChar}"]), ct);
        }

        // Apply any extra user modes from config (e.g. "+ix").
        if (!string.IsNullOrEmpty(_config.UserModes))
        {
            _logger.LogDebug("Setting configured user modes: {Modes}", _config.UserModes);
            await SendRawAsync(new IrcMessage("MODE", [_currentNick, _config.UserModes]), ct);
        }

        _logger.LogInformation("Bot is ready");
        var readyEvent = new ReadyEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = message
        };
        await FanOutEventAsync(readyEvent, ct);

        // Join configured channels in bulk
        if (_config.Channels.Count > 0)
        {
            _logger.LogInformation("Joining {Count} configured channel(s): {Channels}",
                _config.Channels.Count, string.Join(", ", _config.Channels));
            await JoinMultipleAsync(_config.Channels, ct);
        }
    }

    /// <summary>
    /// Called when a pending auth step completes. If all steps are done,
    /// signals the ready TCS.
    /// </summary>
    private void CheckAuthComplete()
    {
        if (!_nickServPending && !_operPending)
        {
            _readyTcs?.TrySetResult(true);
        }
    }

    private async Task HandlePrivmsg(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count < 2 || message.Source?.Nick is null) return;

        // Ignore our own messages echoed back via echo-message
        if (_users.Comparer.Equals(message.Source.Nick, _currentNick)) return;

        var target = message.Parameters[0];
        var text = message.Parameters[1];
        var sender = GetOrCreateUser(message.Source);

        // Check for CTCP
        if (text.StartsWith('\x01') && text.EndsWith('\x01'))
        {
            var ctcpContent = text[1..^1];
            await HandleCtcp(message, sender, target, ctcpContent, ct);
            return;
        }

        var isChannel = IsChannelName(target);
        IrcChannel? channel = isChannel ? GetChannel(target) : null;

        var evt = new MessageEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            Channel = channel,
            Sender = sender,
            Text = text,
            ReplyTo = message.Tags.GetValueOrDefault("+reply")
        };

        await FanOutEventAsync(evt, ct);
    }

    private async Task HandleCtcp(IrcMessage message, IrcUser sender, string target, string ctcpContent, CancellationToken ct)
    {
        var spaceIdx = ctcpContent.IndexOf(' ');
        var command = spaceIdx >= 0 ? ctcpContent[..spaceIdx].ToUpperInvariant() : ctcpContent.ToUpperInvariant();
        var args = spaceIdx >= 0 ? ctcpContent[(spaceIdx + 1)..] : null;

        switch (command)
        {
            case "ACTION":
                {
                    var isChannel = IsChannelName(target);
                    var channel = isChannel ? GetChannel(target) : null;
                    var evt = new ActionEvent
                    {
                        Timestamp = GetTimestamp(message),
                        RawMessage = message,
                        MessageId = message.Tags.GetValueOrDefault("msgid"),
                        BatchId = message.Tags.GetValueOrDefault("batch"),
                        Channel = channel,
                        Sender = sender,
                        Text = args ?? ""
                    };
                    await FanOutEventAsync(evt, ct);
                    break;
                }
            case "VERSION":
                await SendRawAsync(new IrcMessage("NOTICE", [sender.Nick,
                    $"\x01VERSION Marv IRC Bot {MarvVersion.Current}\x01"]), ct);
                break;
            case "PING":
                await SendRawAsync(new IrcMessage("NOTICE", [sender.Nick,
                    $"\x01PING {args ?? ""}\x01"]), ct);
                break;
            case "TIME":
                await SendRawAsync(new IrcMessage("NOTICE", [sender.Nick,
                    $"\x01TIME {DateTimeOffset.UtcNow:R}\x01"]), ct);
                break;
            default:
                {
                    var evt = new CtcpEvent
                    {
                        Timestamp = GetTimestamp(message),
                        RawMessage = message,
                        MessageId = message.Tags.GetValueOrDefault("msgid"),
                        BatchId = message.Tags.GetValueOrDefault("batch"),
                        Sender = sender,
                        Command = command,
                        Args = args,
                        IsDirect = !IsChannelName(target)
                    };
                    await FanOutEventAsync(evt, ct);
                    break;
                }
        }
    }

    // Phrases that indicate successful NickServ identification, covering
    // Atheme ("You are now identified"), Anope ("Password accepted"),
    // and other common services packages.
    private static readonly string[] NickServSuccessPhrases =
    [
        "you are now identified",
        "password accepted",
        "you are now recognized",
        "you are now logged in",
        "you are already identified"
    ];

    private async Task HandleNotice(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count < 2) return;

        // During registration, notices may come from the server (no nick)
        if (message.Source?.Nick is null) return;

        // Ignore our own notices echoed back via echo-message
        if (_users.Comparer.Equals(message.Source.Nick, _currentNick)) return;

        var target = message.Parameters[0];
        var text = message.Parameters[1];
        var sender = GetOrCreateUser(message.Source);

        // Detect NickServ identification response
        if (_nickServPending
            && message.Source.Nick.Equals("NickServ", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var phrase in NickServSuccessPhrases)
            {
                if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("NickServ identification successful");
                    _nickServPending = false;
                    CheckAuthComplete();
                    break;
                }
            }
        }

        var isChannel = IsChannelName(target);
        var channel = isChannel ? GetChannel(target) : null;

        var evt = new NoticeEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            Channel = channel,
            Sender = sender,
            Text = text
        };

        await FanOutEventAsync(evt, ct);
    }

    private async Task HandleJoin(IrcMessage message, CancellationToken ct)
    {
        if (message.Source?.Nick is null || message.Parameters.Count < 1) return;

        var channelName = message.Parameters[0];
        var user = GetOrCreateUser(message.Source);

        // extended-join: JOIN #channel account :realname
        string? account = null;
        if (_capabilityManager.IsNegotiated(Platform.Capabilities.ExtendedJoin) && message.Parameters.Count >= 2)
        {
            account = message.Parameters[1] == "*" ? null : message.Parameters[1];
            if (account is not null) user.Account = account;
            if (message.Parameters.Count >= 3)
                user.RealName = message.Parameters[2];
        }

        // Account from account-tag
        if (message.Tags.TryGetValue("account", out var acctTag) && acctTag is not null)
        {
            account = acctTag == "*" ? null : acctTag;
            if (account is not null) user.Account = account;
        }

        var comparer = CaseMapping.GetComparer(_serverInfo.CaseMapping);

        if (comparer.Equals(user.Nick, _currentNick))
        {
            // Bot is joining a channel
            var channel = new IrcChannel(channelName, comparer);
            channel.AddMember(user);
            _channels[channelName] = channel;
            _self = user;

            var evt = new UserJoinedEvent
            {
                Timestamp = GetTimestamp(message),
                RawMessage = message,
                MessageId = message.Tags.GetValueOrDefault("msgid"),
                BatchId = message.Tags.GetValueOrDefault("batch"),
                Channel = channel,
                User = user,
                Account = account
            };
            await FanOutEventAsync(evt, ct);
        }
        else
        {
            var channel = GetChannel(channelName);
            if (channel is not null)
            {
                channel.AddMember(user);
                user.AddChannel(channel);

                var evt = new UserJoinedEvent
                {
                    Timestamp = GetTimestamp(message),
                    RawMessage = message,
                    MessageId = message.Tags.GetValueOrDefault("msgid"),
                    BatchId = message.Tags.GetValueOrDefault("batch"),
                    Channel = channel,
                    User = user,
                    Account = account
                };
                await FanOutEventAsync(evt, ct);
            }
        }
    }

    private async Task HandlePart(IrcMessage message, CancellationToken ct)
    {
        if (message.Source?.Nick is null || message.Parameters.Count < 1) return;

        var channelName = message.Parameters[0];
        var reason = message.Parameters.Count > 1 ? message.Parameters[1] : null;
        var user = GetOrCreateUser(message.Source);
        var channel = GetChannel(channelName);
        if (channel is null) return;

        var comparer = CaseMapping.GetComparer(_serverInfo.CaseMapping);

        var evt = new UserPartedEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            Channel = channel,
            User = user,
            Reason = reason
        };
        await FanOutEventAsync(evt, ct);

        if (comparer.Equals(user.Nick, _currentNick))
        {
            // Bot left the channel
            _channels.TryRemove(channelName, out _);
        }
        else
        {
            channel.RemoveMember(user.Nick);
            user.RemoveChannel(channelName);
            MaybeForgetUser(user);
        }
    }

    private async Task HandleKick(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count < 2 || message.Source?.Nick is null) return;

        var channelName = message.Parameters[0];
        var kickedNick = message.Parameters[1];
        var reason = message.Parameters.Count > 2 ? message.Parameters[2] : null;
        var channel = GetChannel(channelName);
        if (channel is null) return;

        var kicker = GetOrCreateUser(message.Source);
        var kicked = GetOrCreateUserByNick(kickedNick);

        var comparer = CaseMapping.GetComparer(_serverInfo.CaseMapping);

        var evt = new UserKickedEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            Channel = channel,
            Kicker = kicker,
            Kicked = kicked,
            Reason = reason
        };
        await FanOutEventAsync(evt, ct);

        if (comparer.Equals(kickedNick, _currentNick))
        {
            _channels.TryRemove(channelName, out _);
        }
        else
        {
            channel.RemoveMember(kickedNick);
            kicked.RemoveChannel(channelName);
            MaybeForgetUser(kicked);
        }
    }

    private async Task HandleQuit(IrcMessage message, CancellationToken ct)
    {
        if (message.Source?.Nick is null) return;

        var reason = message.Parameters.Count > 0 ? message.Parameters[0] : null;
        var user = GetOrCreateUser(message.Source);

        var affectedChannels = _channels.Values
            .Where(c => c.HasMember(user.Nick))
            .Cast<IChannel>()
            .ToList();

        var evt = new UserQuitEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            User = user,
            Reason = reason,
            AffectedChannels = affectedChannels
        };
        await FanOutEventAsync(evt, ct);

        // Remove user from all channels
        foreach (var channel in _channels.Values)
        {
            channel.RemoveMember(user.Nick);
        }
        _users.TryRemove(user.Nick, out _);
    }

    private async Task HandleNick(IrcMessage message, CancellationToken ct)
    {
        if (message.Source?.Nick is null || message.Parameters.Count < 1) return;

        var oldNick = message.Source.Nick;
        var newNick = message.Parameters[0];

        var comparer = CaseMapping.GetComparer(_serverInfo.CaseMapping);

        if (comparer.Equals(oldNick, _currentNick))
        {
            _currentNick = newNick;
        }

        if (_users.TryRemove(oldNick, out var user))
        {
            user.Nick = newNick;
            _users[newNick] = user;

            foreach (var channel in _channels.Values)
            {
                if (channel.HasMember(oldNick))
                {
                    channel.RenameMember(oldNick, newNick, user);
                }
            }

            var evt = new NickChangedEvent
            {
                Timestamp = GetTimestamp(message),
                RawMessage = message,
                MessageId = message.Tags.GetValueOrDefault("msgid"),
                BatchId = message.Tags.GetValueOrDefault("batch"),
                User = user,
                OldNick = oldNick,
                NewNick = newNick
            };
            await FanOutEventAsync(evt, ct);
        }
    }

    private async Task HandleMode(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count < 2) return;

        var target = message.Parameters[0];
        if (!IsChannelName(target)) return; // User mode, ignore for now

        var channel = GetChannel(target);
        if (channel is null || message.Source?.Nick is null) return;

        var setter = GetOrCreateUser(message.Source);
        var modeString = message.Parameters[1];
        var changes = ParseModeChanges(modeString, message.Parameters, 2, channel);

        foreach (var change in changes)
        {
            ApplyModeChange(channel, change);
        }

        var evt = new ModeChangedEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            Channel = channel,
            SetBy = setter,
            Changes = changes
        };
        await FanOutEventAsync(evt, ct);
    }

    private async Task HandleTopic(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count < 2 || message.Source?.Nick is null) return;

        var channelName = message.Parameters[0];
        var newTopic = message.Parameters[1];
        var channel = GetChannel(channelName);
        if (channel is null) return;

        var setter = GetOrCreateUser(message.Source);
        channel.Topic = newTopic;
        channel.TopicSetBy = message.Source.ToString();
        channel.TopicSetAt = GetTimestamp(message);

        var evt = new TopicChangedEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            Channel = channel,
            SetBy = setter,
            NewTopic = newTopic
        };
        await FanOutEventAsync(evt, ct);
    }

    private async Task HandleInvite(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count < 2 || message.Source?.Nick is null) return;

        var channelName = message.Parameters[1];
        var inviter = GetOrCreateUser(message.Source);

        var evt = new InviteReceivedEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            Channel = channelName,
            InvitedBy = inviter
        };
        await FanOutEventAsync(evt, ct);
    }

    private async Task HandleAccount(IrcMessage message, CancellationToken ct)
    {
        if (message.Source?.Nick is null || message.Parameters.Count < 1) return;

        var user = GetOrCreateUser(message.Source);
        var oldAccount = user.Account;
        var newAccount = message.Parameters[0] == "*" ? null : message.Parameters[0];
        user.Account = newAccount;

        var evt = new AccountChangedEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            User = user,
            OldAccount = oldAccount,
            NewAccount = newAccount
        };
        await FanOutEventAsync(evt, ct);
    }

    private async Task HandleAway(IrcMessage message, CancellationToken ct)
    {
        if (message.Source?.Nick is null) return;

        var user = GetOrCreateUser(message.Source);
        var awayMsg = message.Parameters.Count > 0 ? message.Parameters[0] : null;
        var isAway = awayMsg is not null;

        user.IsAway = isAway;
        user.AwayMessage = awayMsg;

        var evt = new AwayChangedEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            User = user,
            IsAway = isAway,
            Message = awayMsg
        };
        await FanOutEventAsync(evt, ct);
    }

    private async Task HandleChghost(IrcMessage message, CancellationToken ct)
    {
        if (message.Source?.Nick is null || message.Parameters.Count < 2) return;

        var user = GetOrCreateUser(message.Source);
        var oldHost = user.Host ?? "";
        var newUser = message.Parameters[0];
        var newHost = message.Parameters[1];

        user.User = newUser;
        user.Host = newHost;

        var evt = new HostChangedEvent
        {
            Timestamp = GetTimestamp(message),
            RawMessage = message,
            MessageId = message.Tags.GetValueOrDefault("msgid"),
            BatchId = message.Tags.GetValueOrDefault("batch"),
            User = user,
            OldHost = oldHost,
            NewHost = newHost
        };
        await FanOutEventAsync(evt, ct);
    }

    private async Task HandleBatch(IrcMessage message, CancellationToken ct)
    {
        if (message.Parameters.Count < 1) return;

        var refTag = message.Parameters[0];
        if (refTag.StartsWith('+'))
        {
            var batchId = refTag[1..];
            var batchType = message.Parameters.Count > 1 ? message.Parameters[1] : "";
            var batchParams = message.Parameters.Count > 2
                ? message.Parameters.Skip(2).ToList()
                : new List<string>();

            var evt = new BatchStartEvent
            {
                Timestamp = GetTimestamp(message),
                RawMessage = message,
                MessageId = message.Tags.GetValueOrDefault("msgid"),
                BatchId = message.Tags.GetValueOrDefault("batch"),
                BatchRefTag = batchId,
                Type = batchType,
                Parameters = batchParams
            };
            await FanOutEventAsync(evt, ct);
        }
        else if (refTag.StartsWith('-'))
        {
            var batchId = refTag[1..];

            var evt = new BatchEndEvent
            {
                Timestamp = GetTimestamp(message),
                RawMessage = message,
                MessageId = message.Tags.GetValueOrDefault("msgid"),
                BatchId = message.Tags.GetValueOrDefault("batch"),
                BatchRefTag = batchId
            };
            await FanOutEventAsync(evt, ct);
        }
    }

    private void HandleNamesReply(IrcMessage message)
    {
        // 353 <nick> <type> <channel> :<names>
        if (message.Parameters.Count < 4) return;

        var channelName = message.Parameters[2];
        var channel = GetChannel(channelName);
        if (channel is null) return;

        var names = message.Parameters[3].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var prefixChars = _serverInfo.Prefix.Prefixes;

        foreach (var name in names)
        {
            string nick;
            string? ident = null, host = null;
            var prefixes = new List<char>();

            var entry = name.AsSpan();

            // Strip prefix characters
            var i = 0;
            while (i < entry.Length && prefixChars.Contains(entry[i]))
            {
                prefixes.Add(entry[i]);
                i++;
            }
            var rest = entry[i..].ToString();

            // userhost-in-names: nick!user@host
            if (_capabilityManager.IsNegotiated(Platform.Capabilities.UserhostInNames))
            {
                var bangIdx = rest.IndexOf('!');
                var atIdx = rest.IndexOf('@');
                if (bangIdx >= 0 && atIdx > bangIdx)
                {
                    nick = rest[..bangIdx];
                    ident = rest[(bangIdx + 1)..atIdx];
                    host = rest[(atIdx + 1)..];
                }
                else
                {
                    nick = rest;
                }
            }
            else
            {
                nick = rest;
            }

            var user = GetOrCreateUserByNick(nick);
            if (ident is not null) user.User = ident;
            if (host is not null) user.Host = host;

            channel.AddMember(user, prefixes);
            user.AddChannel(channel);
        }
    }

    private void HandleTopicReply(IrcMessage message)
    {
        // 332 <nick> <channel> :<topic>
        if (message.Parameters.Count < 3) return;

        var channel = GetChannel(message.Parameters[1]);
        if (channel is not null)
        {
            channel.Topic = message.Parameters[2];
        }
    }

    private void HandleTopicWhoTime(IrcMessage message)
    {
        // 333 <nick> <channel> <setter> <timestamp>
        if (message.Parameters.Count < 4) return;

        var channel = GetChannel(message.Parameters[1]);
        if (channel is not null)
        {
            channel.TopicSetBy = message.Parameters[2];
            if (long.TryParse(message.Parameters[3], out var ts))
                channel.TopicSetAt = DateTimeOffset.FromUnixTimeSeconds(ts);
        }
    }

    private void HandleCreationTime(IrcMessage message)
    {
        // 329 <nick> <channel> <timestamp>
        if (message.Parameters.Count < 3) return;

        var channel = GetChannel(message.Parameters[1]);
        if (channel is not null && long.TryParse(message.Parameters[2], out var ts))
        {
            channel.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(ts);
        }
    }

    private async Task HandleNickError(IrcMessage message, CancellationToken ct)
    {
        // Try alternative nick by appending underscore
        _currentNick += "_";
        _logger.LogWarning("Nick unavailable, trying: {Nick}", _currentNick);
        await SendRawAsync(new IrcMessage("NICK", [_currentNick]), ct);
    }

    // --- Helpers ---

    private RawMessageEvent CreateRawEvent(IrcMessage message) => new()
    {
        Timestamp = GetTimestamp(message),
        RawMessage = message,
        MessageId = message.Tags.GetValueOrDefault("msgid"),
        BatchId = message.Tags.GetValueOrDefault("batch")
    };

    private ConnectedEvent CreateConnectedEvent(IrcMessage message) => new()
    {
        Timestamp = GetTimestamp(message),
        RawMessage = message,
        MessageId = message.Tags.GetValueOrDefault("msgid"),
        BatchId = message.Tags.GetValueOrDefault("batch")
    };

    private CapabilitiesChangedEvent CreateCapChangedEvent(IrcMessage message) => new()
    {
        Timestamp = GetTimestamp(message),
        RawMessage = message,
        MessageId = message.Tags.GetValueOrDefault("msgid"),
        BatchId = message.Tags.GetValueOrDefault("batch")
    };

    private DateTimeOffset GetTimestamp(IrcMessage message)
    {
        if (message.Tags.TryGetValue("time", out var timeStr) && timeStr is not null)
        {
            if (DateTimeOffset.TryParse(timeStr, out var ts))
                return ts;
        }
        return DateTimeOffset.UtcNow;
    }

    private bool IsChannelName(string target)
    {
        return target.Length > 0 && _serverInfo.ChannelTypes.Contains(target[0]);
    }

    private IrcChannel? GetChannel(string name)
    {
        _channels.TryGetValue(name, out var channel);
        return channel;
    }

    private IrcUser GetOrCreateUser(MessageSource source)
    {
        var nick = source.Nick!;
        var user = _users.GetOrAdd(nick, n =>
            new IrcUser(n, CaseMapping.GetComparer(_serverInfo.CaseMapping)));

        if (source.User is not null) user.User = source.User;
        if (source.Host is not null) user.Host = source.Host;

        return user;
    }

    private IrcUser GetOrCreateUserByNick(string nick)
    {
        return _users.GetOrAdd(nick, n =>
            new IrcUser(n, CaseMapping.GetComparer(_serverInfo.CaseMapping)));
    }

    /// <summary>
    /// Removes a user from tracking if they share no channels with the bot.
    /// </summary>
    private void MaybeForgetUser(IrcUser user)
    {
        var comparer = CaseMapping.GetComparer(_serverInfo.CaseMapping);
        if (comparer.Equals(user.Nick, _currentNick)) return;

        var inAnyChannel = _channels.Values.Any(c => c.HasMember(user.Nick));
        if (!inAnyChannel)
        {
            _users.TryRemove(user.Nick, out _);
        }
    }

    private async Task FanOutEventAsync(MarvEvent evt, CancellationToken ct)
    {
        foreach (var writer in _eventWriters)
        {
            try
            {
                await writer.WriteAsync(evt, ct);
            }
            catch (ChannelClosedException)
            {
                // Plugin task ended
            }
        }
    }

    private static List<KeyValuePair<string, string?>> ParseCapList(string capList)
    {
        var result = new List<KeyValuePair<string, string?>>();
        foreach (var entry in capList.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = entry.IndexOf('=');
            if (eqIdx >= 0)
                result.Add(new(entry[..eqIdx], entry[(eqIdx + 1)..]));
            else
                result.Add(new(entry, null));
        }
        return result;
    }

    private List<ModeChange> ParseModeChanges(string modeString, IReadOnlyList<string> parameters, int paramStart, IrcChannel channel)
    {
        var changes = new List<ModeChange>();
        var adding = true;
        var paramIdx = paramStart;

        foreach (var c in modeString)
        {
            if (c == '+') { adding = true; continue; }
            if (c == '-') { adding = false; continue; }

            string? param = null;
            var modeType = GetModeType(c);

            // Type A: always has parameter
            // Type B: always has parameter
            // Type C: has parameter when setting, not when unsetting
            // Type D: never has parameter
            // Prefix modes: always have parameter (the nick)
            if (_serverInfo.Prefix.HasMode(c))
            {
                if (paramIdx < parameters.Count)
                    param = parameters[paramIdx++];
            }
            else if (modeType is 'A' or 'B')
            {
                if (paramIdx < parameters.Count)
                    param = parameters[paramIdx++];
            }
            else if (modeType == 'C' && adding)
            {
                if (paramIdx < parameters.Count)
                    param = parameters[paramIdx++];
            }

            changes.Add(new ModeChange { IsSet = adding, Mode = c, Parameter = param });
        }

        return changes;
    }

    private char GetModeType(char mode)
    {
        var chanModes = _serverInfo.ChannelModes;
        if (chanModes.TypeA.Contains(mode)) return 'A';
        if (chanModes.TypeB.Contains(mode)) return 'B';
        if (chanModes.TypeC.Contains(mode)) return 'C';
        if (chanModes.TypeD.Contains(mode)) return 'D';
        return 'D'; // Default to no-parameter
    }

    private void ApplyModeChange(IrcChannel channel, ModeChange change)
    {
        // Prefix modes affect user status in channel
        if (_serverInfo.Prefix.HasMode(change.Mode) && change.Parameter is not null)
        {
            var prefix = _serverInfo.Prefix.GetPrefix(change.Mode);
            if (prefix is not null)
            {
                if (change.IsSet)
                    channel.AddPrefix(change.Parameter, prefix.Value);
                else
                    channel.RemovePrefix(change.Parameter, prefix.Value);
            }
            return;
        }

        if (change.IsSet)
            channel.SetMode(change.Mode, change.Parameter);
        else
            channel.UnsetMode(change.Mode);
    }

    /// <summary>
    /// Waits for IRC registration (001) to complete. Times out after 30 seconds.
    /// </summary>
    public async Task WaitForRegistrationAsync(CancellationToken ct)
    {
        if (_registrationTcs is null) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        await _registrationTcs.Task.WaitAsync(cts.Token);
    }

    /// <summary>
    /// Waits for the bot to be fully ready (registration + all post-registration
    /// auth complete). Times out after 60 seconds.
    /// </summary>
    public async Task WaitForReadyAsync(CancellationToken ct)
    {
        if (_readyTcs is null) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        await _readyTcs.Task.WaitAsync(cts.Token);
    }
}
