using System.Text.RegularExpressions;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;

namespace Marv.Testing;

/// <summary>
/// Fluent builder for <see cref="RegexMatchContext"/> instances.
/// </summary>
/// <example>
/// <code>
/// var ctx = RegexMatchContextBuilder.Create(@"hello (\w+)", "hello world")
///     .InChannel("#test")
///     .From("alice")
///     .Build();
/// </code>
/// </example>
public sealed class RegexMatchContextBuilder
{
    private readonly string _pattern;
    private readonly string _input;
    private string? _channelName;
    private string _senderNick = "testuser";
    private string? _senderAccount;
    private IBot? _bot;

    private RegexMatchContextBuilder(string pattern, string input)
    {
        _pattern = pattern;
        _input = input;
    }

    /// <summary>
    /// Creates a new builder for a regex match context.
    /// </summary>
    /// <param name="pattern">The regex pattern to match against <paramref name="input"/>.</param>
    /// <param name="input">The input text to match.</param>
    public static RegexMatchContextBuilder Create(string pattern, string input) =>
        new(pattern, input);

    /// <summary>Sets the channel context. Omit for a direct message.</summary>
    public RegexMatchContextBuilder InChannel(string channelName)
    {
        _channelName = channelName;
        return this;
    }

    /// <summary>Marks this as a direct (private) message. This is the default.</summary>
    public RegexMatchContextBuilder AsDirect()
    {
        _channelName = null;
        return this;
    }

    /// <summary>Sets the sender's nick and optional services account.</summary>
    public RegexMatchContextBuilder From(string nick, string? account = null)
    {
        _senderNick = nick;
        _senderAccount = account;
        return this;
    }

    /// <summary>
    /// Provides a custom <see cref="IBot"/> mock. If not called, a default
    /// mock is created with Self.Nick = "Marv" and CommandPrefix = "!".
    /// </summary>
    public RegexMatchContextBuilder WithBot(IBot bot)
    {
        _bot = bot;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="RegexMatchContext"/>. Throws <see cref="ArgumentException"/>
    /// if the pattern does not match the input.
    /// </summary>
    public RegexMatchContext Build()
    {
        var match = Regex.Match(_input, _pattern);
        if (!match.Success)
            throw new ArgumentException(
                $"Pattern '{_pattern}' did not match input '{_input}'.");

        var bot = _bot ?? MockBot.Create();
        var sender = MockUser.Create(_senderNick, _senderAccount);
        IChannel? channel = _channelName is not null ? MockChannel.Create(_channelName) : null;

        var target = channel?.Name ?? bot.Self.Nick;
        var rawMessage = DummyIrcMessage.PrivmsgFrom(_senderNick, target, _input);

        return new RegexMatchContext
        {
            Match = match,
            Channel = channel,
            Sender = sender,
            RawMessage = rawMessage,
            Bot = bot
        };
    }
}
