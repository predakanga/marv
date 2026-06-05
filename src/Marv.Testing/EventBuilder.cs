using Marv.Core.Events;
using Marv.Core.Protocol;

namespace Marv.Testing;

/// <summary>
/// Builder for <see cref="MarvEvent"/> instances that fills in boilerplate
/// properties (<see cref="MarvEvent.RawMessage"/>, <see cref="MarvEvent.Timestamp"/>)
/// so tests only need to specify event-specific properties.
/// </summary>
/// <remarks>
/// Events use <c>required init</c> properties, so this builder accepts a factory
/// function that constructs the event via object initializer syntax. The builder
/// fills in <see cref="MarvEvent.Timestamp"/> (to <see cref="DateTimeOffset.UtcNow"/>)
/// if not explicitly set, and provides <see cref="DummyIrcMessage.Empty"/> as a
/// convenience for the required <see cref="MarvEvent.RawMessage"/> property.
/// </remarks>
/// <example>
/// <code>
/// // Simple events with no required properties beyond RawMessage:
/// var evt = EventBuilder&lt;ConnectedEvent&gt;.Create(raw =&gt; new ConnectedEvent
/// {
///     RawMessage = raw
/// }).Build();
///
/// // Events with required properties:
/// var msg = EventBuilder&lt;MessageEvent&gt;.Create(raw =&gt; new MessageEvent
/// {
///     Sender = MockUser.Create("alice"),
///     Text = "hello",
///     RawMessage = raw
/// }).Build();
/// </code>
/// </example>
/// <typeparam name="T">The concrete event type.</typeparam>
public sealed class EventBuilder<T> where T : MarvEvent
{
    private readonly Func<IrcMessage, T> _factory;
    private IrcMessage? _rawMessage;
    private DateTimeOffset? _timestamp;

    private EventBuilder(Func<IrcMessage, T> factory) => _factory = factory;

    /// <summary>
    /// Creates a new builder with a factory that receives a default
    /// <see cref="IrcMessage"/> to use as <see cref="MarvEvent.RawMessage"/>.
    /// </summary>
    /// <param name="factory">
    /// A factory that creates the event. The <see cref="IrcMessage"/> parameter
    /// is the default raw message — use it for <see cref="MarvEvent.RawMessage"/>
    /// unless your test needs a specific value.
    /// </param>
    public static EventBuilder<T> Create(Func<IrcMessage, T> factory) => new(factory);

    /// <summary>
    /// Overrides the default raw message passed to the factory.
    /// </summary>
    public EventBuilder<T> WithRawMessage(IrcMessage rawMessage)
    {
        _rawMessage = rawMessage;
        return this;
    }

    /// <summary>
    /// Sets the event timestamp. Defaults to <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    public EventBuilder<T> At(DateTimeOffset timestamp)
    {
        _timestamp = timestamp;
        return this;
    }

    /// <summary>
    /// Builds the event, filling in <see cref="MarvEvent.Timestamp"/> with
    /// <see cref="DateTimeOffset.UtcNow"/> if not explicitly set.
    /// </summary>
    public T Build()
    {
        var raw = _rawMessage ?? DummyIrcMessage.Empty;
        var evt = _factory(raw);

        if (evt.Timestamp == default && _timestamp.HasValue)
            SetTimestamp(evt, _timestamp.Value);
        else if (evt.Timestamp == default)
            SetTimestamp(evt, DateTimeOffset.UtcNow);

        return evt;
    }

    // init-only properties can be set via reflection at runtime.
    private static void SetTimestamp(MarvEvent evt, DateTimeOffset value)
    {
        var prop = typeof(MarvEvent).GetProperty(nameof(MarvEvent.Timestamp))!;
        prop.SetValue(evt, value);
    }
}
