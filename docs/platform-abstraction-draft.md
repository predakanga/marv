# Platform Abstraction Draft

This document describes the core concepts that Marv models and
presents to plugins. Although Marv only targets IRC, these
abstractions exist to give plugins a clean, typed API rather than
forcing them to work with raw protocol strings.

---

## Messages

### Raw Message: `IrcMessage`

The lowest-level representation. An immutable record produced by the
parser:

```
IrcMessage
├── Tags: IReadOnlyDictionary<string, string?>
├── Source: MessageSource?  (nick, user, host — any component may be absent)
├── Command: string         ("PRIVMSG", "001", "CAP", etc.)
└── Parameters: IReadOnlyList<string>
```

**Design notes**:

- The trailing parameter (after `:`) is always folded into
  `Parameters` as the last element. There is no separate `Trailing`
  property — this avoids the class of bugs described in research
  section 5.1.
- `Command` is always uppercase for consistency, regardless of how the
  server sent it.
- `Tags` values are already unescaped. The parser handles `\:`, `\s`,
  `\\`, `\r`, `\n`, and invalid escape sequences per the IRCv3 spec.
- `Source` is parsed into its components. Server-originated messages
  may have a source with only a hostname (no nick/user).

### Message Metadata

Common tags are surfaced as strongly-typed properties on event objects
so plugins don't need to dig into the tag dictionary:

| Property | Tag | Type | Availability |
|---|---|---|---|
| `Timestamp` | `time` | `DateTimeOffset` | When `server-time` is negotiated |
| `MessageId` | `msgid` | `string?` | When server assigns one |
| `Account` | `account` | `string?` | When `account-tag` is negotiated |
| `IsBot` | `bot` | `bool` | When `bot` tag is present |
| `Label` | `label` | `string?` | Internal use for labeled-response |
| `BatchId` | `batch` | `string?` | When message is part of a batch |

Plugins can always access the full tag dictionary via `RawMessage.Tags`
for non-standard or vendor-specific tags.

### High-Level Message Types

Plugins typically work with typed event objects rather than raw
`IrcMessage`. See the Events section below. The event objects contain
the relevant `IrcMessage` for plugins that need protocol-level access.

---

## Channels

### `IChannel`

Represents a channel the bot is currently a member of.

```
IChannel
├── Name: string                            ("#channel")
├── Topic: string?
├── TopicSetBy: string?                     (nick or mask)
├── TopicSetAt: DateTimeOffset?
├── Modes: IReadOnlyDictionary<char, string?> (mode → parameter)
├── Members: IReadOnlyCollection<IChannelMember>
├── HasMember(string nick): bool
├── GetMember(string nick): IChannelMember?
└── CreatedAt: DateTimeOffset?
```

### `IChannelMember`

A user's presence within a specific channel:

```
IChannelMember
├── User: IUser
├── Prefixes: IReadOnlySet<char>   ('@', '+', '%', etc.)
├── HasPrefix(char prefix): bool
├── IsOp: bool                     (shorthand for '@')
├── IsVoiced: bool                 (shorthand for '+')
└── JoinedAt: DateTimeOffset?      (if server-time available)
```

### Channel Comparisons

Channel names are compared using the server's advertised `CASEMAPPING`
(from ISUPPORT). The `IChannel` and related types use this
automatically — plugins do not need to handle case mapping themselves.

All channel collections (`IChannelStore`) are keyed using
case-mapped comparison so that `#Channel` and `#channel` resolve to
the same entry under ASCII case mapping.

---

## Users

### `IUser`

Represents a user the bot is aware of (through shared channels or
direct interaction):

```
IUser
├── Nick: string
├── User: string?            (ident / username)
├── Host: string?
├── Account: string?         (services account, from account-tag/extended-join/WHOX)
├── RealName: string?        (from extended-join or WHOIS)
├── IsAway: bool             (from away-notify)
├── AwayMessage: string?
├── IsBot: bool              (from bot tag or bot mode)
├── Channels: IReadOnlyCollection<IChannel>  (shared channels)
└── Hostmask: string         ("nick!user@host")
```

### User Identity

Users are identified primarily by nick (case-mapped per CASEMAPPING).
When `account-tag` is negotiated, the `Account` property provides a
stable identity across nick changes.

The user store tracks nick changes: when user A renames to B, the
`IUser` object is updated in place (same reference) with the new nick.
Plugins holding a reference to the `IUser` see the updated nick
automatically.

### The Bot's Own Identity

The bot itself is represented as a special `IUser` accessible via
`IBot.Self`. This allows plugins to check the bot's current nick,
modes, and other properties.

---

## Capabilities

### `ICapabilityManager`

Plugins can query which IRCv3 capabilities the server supports and
which have been successfully negotiated:

```
ICapabilityManager
├── IsNegotiated(string cap): bool       ("Is echo-message active?")
├── IsAvailable(string cap): bool        ("Does the server support it?")
├── NegotiatedCapabilities: IReadOnlySet<string>
├── AvailableCapabilities: IReadOnlyDictionary<string, string?>  (cap → value)
└── event CapabilitiesChanged            (from cap-notify)
```

### Usage Pattern

Plugins should check capability availability before relying on
cap-dependent features:

```csharp
if (capabilities.IsNegotiated("echo-message"))
{
    // We will see our own messages echoed back — no need to
    // self-track what we send.
}
else
{
    // We need to manually track our outgoing messages.
}
```

### Capability Constants

`Marv.Core` provides string constants for all known capabilities to
avoid magic strings:

```csharp
static class Capabilities
{
    public const string MessageTags = "message-tags";
    public const string ServerTime = "server-time";
    public const string EchoMessage = "echo-message";
    public const string AccountTag = "account-tag";
    public const string LabeledResponse = "labeled-response";
    // ... etc.
}
```

---

## ISUPPORT Parameters

### `IServerInfo`

The server advertises its configuration through ISUPPORT (005)
numerics. This information is available to plugins via `IServerInfo`:

```
IServerInfo
├── NetworkName: string?
├── CaseMapping: CaseMapping          (Rfc1459, StrictRfc1459, Ascii)
├── ChannelModes: ChannelModeTypes    (A/B/C/D mode classifications)
├── Prefix: PrefixMapping             (mode char → prefix char, e.g. o→@)
├── MaxChannels: int?
├── MaxNickLength: int?
├── MaxTopicLength: int?
├── MaxMessageLength: int             (default 512 minus overhead)
├── ChannelTypes: IReadOnlySet<char>  ('#', '&', etc.)
├── Supports(string token): bool
└── GetValue(string token): string?
```

This is updated live as the server sends ISUPPORT messages (including
resends after connection registration).

---

## Events

### Event Hierarchy

Events are strongly-typed classes organized by category. Each event
carries the relevant context and the raw `IrcMessage` for advanced use.

#### Connection Events

| Event | When |
|---|---|
| `ConnectedEvent` | IRC registration complete (001 received) |
| `DisconnectedEvent` | Connection lost or closed |
| `CapabilitiesChangedEvent` | Capabilities added/removed at runtime (cap-notify) |

#### Message Events

| Event | Key Properties |
|---|---|
| `ChannelMessageEvent` | `Channel`, `Sender`, `Text`, `ReplyTo?` |
| `PrivateMessageEvent` | `Sender`, `Text`, `ReplyTo?` |
| `ChannelNoticeEvent` | `Channel`, `Sender`, `Text` |
| `PrivateNoticeEvent` | `Sender`, `Text` |
| `ChannelActionEvent` | `Channel`, `Sender`, `Text` (CTCP ACTION) |
| `PrivateActionEvent` | `Sender`, `Text` |

#### Channel Events

| Event | Key Properties |
|---|---|
| `UserJoinedEvent` | `Channel`, `User`, `Account?` |
| `UserPartedEvent` | `Channel`, `User`, `Reason?` |
| `UserKickedEvent` | `Channel`, `Kicker`, `Kicked`, `Reason?` |
| `TopicChangedEvent` | `Channel`, `SetBy`, `NewTopic` |
| `ModeChangedEvent` | `Channel`, `SetBy`, `Changes` |
| `InviteReceivedEvent` | `Channel`, `InvitedBy` |

#### User Events

| Event | Key Properties |
|---|---|
| `UserQuitEvent` | `User`, `Reason?`, `AffectedChannels` |
| `NickChangedEvent` | `User`, `OldNick`, `NewNick` |
| `AccountChangedEvent` | `User`, `OldAccount?`, `NewAccount?` |
| `AwayChangedEvent` | `User`, `IsAway`, `Message?` |
| `HostChangedEvent` | `User`, `OldHost`, `NewHost` |

#### Raw Protocol Event

| Event | Key Properties |
|---|---|
| `RawMessageEvent` | `Message: IrcMessage` |

The `RawMessageEvent` fires for every inbound message, before any
higher-level event is dispatched. Plugins that need to handle protocol
messages not covered by the typed events can subscribe to this.

### Common Event Properties

All events inherit from `MarvEvent` and share:

```
MarvEvent
├── Timestamp: DateTimeOffset   (from server-time tag, or local clock)
├── RawMessage: IrcMessage      (the underlying protocol message)
└── MessageId: string?          (from msgid tag)
```

### Batch Events

When the server sends a `BATCH` group, the contained messages are
collected and delivered as a `BatchEvent` after the batch closes:

```
BatchEvent
├── Type: string         (e.g., "netsplit", "netjoin", "chathistory")
├── Parameters: IReadOnlyList<string>
├── Messages: IReadOnlyList<IrcMessage>
└── InnerEvents: IReadOnlyList<MarvEvent>
```

Individual messages within a batch are not dispatched as separate
events — they are only available within the `BatchEvent`. This
prevents plugins from seeing partial state during a netsplit/netjoin.

### Event Ordering

Events are dispatched sequentially on the message processor task:

1. `RawMessageEvent` fires first (all raw subscribers)
2. State tracking updates (channel/user stores)
3. The typed event fires (e.g., `UserJoinedEvent`)

Within each step, plugin handlers are called in plugin load order.
