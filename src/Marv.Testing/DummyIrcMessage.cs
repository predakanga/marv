using Marv.Core.Protocol;

namespace Marv.Testing;

/// <summary>
/// Provides dummy <see cref="IrcMessage"/> instances for test setup where the
/// raw message content is irrelevant to the test.
/// </summary>
public static class DummyIrcMessage
{
    /// <summary>
    /// A PRIVMSG to #test with text "test".
    /// </summary>
    public static IrcMessage Privmsg { get; } = new(
        null,
        new MessageSource("testuser", "user", "host.example.com"),
        "PRIVMSG",
        ["#test", "test"]);

    /// <summary>
    /// A NOTICE to #test with text "test".
    /// </summary>
    public static IrcMessage Notice { get; } = new(
        null,
        new MessageSource("testuser", "user", "host.example.com"),
        "NOTICE",
        ["#test", "test"]);

    /// <summary>
    /// Creates a PRIVMSG from the specified sender to the specified target.
    /// </summary>
    public static IrcMessage PrivmsgFrom(string nick, string target, string text) => new(
        null,
        new MessageSource(nick, "user", "host.example.com"),
        "PRIVMSG",
        [target, text]);

    /// <summary>
    /// A minimal IRC message with no meaningful content, for events where
    /// the raw message is required but irrelevant.
    /// </summary>
    public static IrcMessage Empty { get; } = new("PING", ["dummy"]);
}
