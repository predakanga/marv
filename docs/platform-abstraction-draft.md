# Platform Abstraction Draft

This document describes the core concepts that Marv models and
presents to plugins. Although Marv only targets IRC, these
abstractions exist to give plugins a clean, typed API rather than
forcing them to work with raw protocol strings.

---

## Messages

### Raw Message: `IrcMessage`

The lowest-level representation. An immutable record used for both
inbound and outbound messages:

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
  may have a source with only a hostname (no nick/user). For outbound
  messages, `Source` is null.
- The same type is used for both directions. The structure is
  identical; only the presence of `Source` differs.

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
├── Members: IReadOnlyCollection<IUser>
├── GetPrefixes(string nick): IReadOnlySet<char>   ('@', '+', etc.)
├── GetJoinTime(string nick): DateTimeOffset?
├── HasMember(string nick): bool
├── IsOp(string nick): bool                (shorthand: has '@' prefix)
├── IsVoiced(string nick): bool             (shorthand: has '+' prefix)
└── CreatedAt: DateTimeOffset?
```

Per-user channel state (prefixes and join time) is stored on the
channel itself rather than in a separate `IChannelMember` relation.
These are the only two per-user-per-channel properties, and a
dedicated join type would add API surface without meaningful benefit.
Plugins query prefix/join state through the `IChannel` methods.

### Channel Comparisons

Channel names are compared using the server's advertised `CASEMAPPING`
(from ISUPPORT). The `IChannel` and related types use this
automatically — plugins do not need to handle case mapping themselves.

All channel collections are keyed using case-mapped comparison so that
`#Channel` and `#channel` resolve to the same entry under ASCII case
mapping.

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

### Concurrency Model

`IUser` and `IChannel` objects are mutable — properties are updated
in place by the message processor when state changes occur (NICK,
CHGHOST, AWAY, MODE, etc.). Plugins holding a reference to an
`IUser` or `IChannel` see live updates.

Thread safety guarantees:

- **Individual property reads are atomic.** Reading a single property
  (e.g., `user.Nick`) always returns a consistent value — you will
  never see a partial write.
- **Cross-property consistency is not guaranteed.** If you read
  `user.Nick` and then `user.Host` in the same handler, a NICK or
  CHGHOST change could land between the two reads. In practice this
  is rare and usually harmless. Plugins that need strict consistency
  across multiple properties should copy the values they need into
  locals at the start of their handler.
- **Collection enumeration is safe.** Backing collections use
  `ConcurrentDictionary`, which safely handles concurrent reads and
  writes. Iterating `Channel.Members` while the message processor
  adds or removes a member will not throw.

The user store uses `ConcurrentDictionary<string, IUser>` with
case-mapped keys. The channel store is similarly structured. On
disconnection, all state stores are cleared — plugins should treat
any cached references as stale after `OnDisconnectedAsync`.

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

Message events use a unified type with an `IsDirect` flag rather than
separate channel/private variants. This halves the event type count
and means a handler that doesn't care about the distinction (which is
common) needs only one subscription.

| Event | Key Properties |
|---|---|
| `MessageEvent` | `Channel?`, `Sender`, `Text`, `IsDirect`, `ReplyTo?` |
| `NoticeEvent` | `Channel?`, `Sender`, `Text`, `IsDirect` |
| `ActionEvent` | `Channel?`, `Sender`, `Text`, `IsDirect` |
| `CtcpEvent` | `Sender`, `Command`, `Args?`, `IsDirect` |

When `IsDirect` is true, `Channel` is null (the message was sent
directly to the bot). When `IsDirect` is false, `Channel` identifies
the channel the message was sent to.

**CTCP handling**: The core handles CTCP VERSION, PING, and TIME
automatically — responses are generated by the message processor
without plugin involvement. The VERSION response includes only the
bot's name and version string (no host or OS information). All other
CTCP queries (e.g., SOURCE, USERINFO, custom) are translated into
`CtcpEvent` for plugins to handle. `CtcpEvent.Command` is the CTCP
command name (e.g., `"SOURCE"`), and `CtcpEvent.Args` contains any
arguments after the command. ACTION is handled separately as
`ActionEvent` and is not dispatched as `CtcpEvent`.

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
├── MessageId: string?          (from msgid tag)
└── BatchId: string?            (from batch tag, null if not batched)
```

### Batched Messages

When the server sends a `BATCH` group, individual messages within the
batch are delivered as normal typed events — each carries a `BatchId`
property linking it to the batch. Plugins that don't care about
batching simply ignore `BatchId` and process events normally.

Plugins that need batch-aware processing (e.g., collecting all
netsplit QUITs atomically) can subscribe to the batch start/end
signals and collect events by `BatchId`:

| Event | Key Properties |
|---|---|
| `BatchStartEvent` | `BatchId`, `Type`, `Parameters` |
| `BatchEndEvent` | `BatchId` |

This approach avoids forcing all plugin authors to handle both
individual and batched event delivery. The default path (ignore
batching) works without any special handling.

### Event Ordering

Events are dispatched per-plugin (each plugin has its own channel and
task — see `architecture.md`). The ordering guarantees are:

1. State tracking updates happen before events are fanned out
2. `RawMessageEvent` is dispatched before the corresponding typed
   event
3. Within a single plugin, events arrive in the order the server
   sent them
4. Across plugins, events are delivered concurrently — there is no
   ordering guarantee between different plugins' handling of the same
   event
