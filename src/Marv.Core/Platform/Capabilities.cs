namespace Marv.Core.Platform;

/// <summary>
/// String constants for known IRCv3 capabilities, to avoid magic strings throughout the codebase.
/// </summary>
public static class Capabilities
{
    /// <summary>Allows receiving and sending arbitrary message tags.</summary>
    public const string MessageTags = "message-tags";

    /// <summary>Provides accurate timestamps on messages.</summary>
    public const string ServerTime = "server-time";

    /// <summary>Server echoes the bot's own PRIVMSG/NOTICE messages back.</summary>
    public const string EchoMessage = "echo-message";

    /// <summary>Every message carries the sender's services account name.</summary>
    public const string AccountTag = "account-tag";

    /// <summary>Correlates sent commands with server responses.</summary>
    public const string LabeledResponse = "labeled-response";

    /// <summary>Groups related messages together.</summary>
    public const string Batch = "batch";

    /// <summary>Server notifies when capabilities become available/unavailable.</summary>
    public const string CapNotify = "cap-notify";

    /// <summary>Secure authentication during connection registration.</summary>
    public const string Sasl = "sasl";

    /// <summary>Receive all prefix modes in NAMES/WHO responses.</summary>
    public const string MultiPrefix = "multi-prefix";

    /// <summary>JOIN messages include account name and realname.</summary>
    public const string ExtendedJoin = "extended-join";

    /// <summary>Notified when users change away status.</summary>
    public const string AwayNotify = "away-notify";

    /// <summary>Notified when users log in/out of services accounts.</summary>
    public const string AccountNotify = "account-notify";

    /// <summary>Notified when users are invited to channels.</summary>
    public const string InviteNotify = "invite-notify";

    /// <summary>Notified when a user's host/ident changes.</summary>
    public const string Chghost = "chghost";

    /// <summary>Notified when a user changes their realname.</summary>
    public const string Setname = "setname";

    /// <summary>NAMES replies include full nick!user@host masks.</summary>
    public const string UserhostInNames = "userhost-in-names";

    /// <summary>Standardized FAIL/WARN/NOTE server responses.</summary>
    public const string StandardReplies = "standard-replies";

    /// <summary>Watch for specific nicks coming online/offline.</summary>
    public const string Monitor = "monitor";
}
