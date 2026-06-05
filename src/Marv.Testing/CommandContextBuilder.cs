using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;

namespace Marv.Testing;

/// <summary>
/// Fluent builder for <see cref="CommandContext"/> instances. Reduces test setup
/// from ~15 lines of mock boilerplate to 2-3 lines.
/// </summary>
/// <example>
/// <code>
/// var ctx = CommandContextBuilder.Create("hello", "world")
///     .InChannel("#test")
///     .From("alice")
///     .Build();
/// </code>
/// </example>
public sealed class CommandContextBuilder
{
    private readonly string _command;
    private readonly string _argString;
    private string? _channelName;
    private string _senderNick = "testuser";
    private string? _senderAccount;
    private IBot? _bot;

    private CommandContextBuilder(string command, string argString)
    {
        _command = command;
        _argString = argString;
    }

    /// <summary>
    /// Creates a new builder for a command context.
    /// </summary>
    /// <param name="command">The command name (without prefix).</param>
    /// <param name="args">The argument string after the command. Defaults to empty.</param>
    public static CommandContextBuilder Create(string command, string args = "") =>
        new(command, args);

    /// <summary>Sets the channel context. Omit for a direct message.</summary>
    public CommandContextBuilder InChannel(string channelName)
    {
        _channelName = channelName;
        return this;
    }

    /// <summary>Marks this as a direct (private) message. This is the default.</summary>
    public CommandContextBuilder AsDirect()
    {
        _channelName = null;
        return this;
    }

    /// <summary>Sets the sender's nick and optional services account.</summary>
    public CommandContextBuilder From(string nick, string? account = null)
    {
        _senderNick = nick;
        _senderAccount = account;
        return this;
    }

    /// <summary>
    /// Provides a custom <see cref="IBot"/> mock. If not called, a default
    /// mock is created with Self.Nick = "Marv" and CommandPrefix = "!".
    /// </summary>
    public CommandContextBuilder WithBot(IBot bot)
    {
        _bot = bot;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="CommandContext"/> with all configured values.
    /// </summary>
    public CommandContext Build()
    {
        var bot = _bot ?? MockBot.Create();
        var sender = MockUser.Create(_senderNick, _senderAccount);
        IChannel? channel = _channelName is not null ? MockChannel.Create(_channelName) : null;

        var args = string.IsNullOrEmpty(_argString)
            ? Array.Empty<string>()
            : _argString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var target = channel?.Name ?? bot.Self.Nick;
        var prefix = bot.CommandPrefix;
        var fullText = string.IsNullOrEmpty(_argString)
            ? $"{prefix}{_command}"
            : $"{prefix}{_command} {_argString}";

        var rawMessage = DummyIrcMessage.PrivmsgFrom(_senderNick, target, fullText);

        return new CommandContext
        {
            Command = _command,
            Args = args,
            ArgString = _argString,
            Channel = channel,
            Sender = sender,
            RawMessage = rawMessage,
            Bot = bot
        };
    }
}
