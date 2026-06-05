using Marv.Core.Events;
using Xunit;

namespace Marv.Testing.Tests;

public class EventBuilderTests
{
    [Fact]
    public void Build_SimpleEvent_FillsTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var evt = EventBuilder<ConnectedEvent>.Create(raw => new ConnectedEvent
        {
            RawMessage = raw
        }).Build();

        Assert.NotEqual(default, evt.Timestamp);
        Assert.True(evt.Timestamp >= before);
    }

    [Fact]
    public void Build_SimpleEvent_FillsRawMessage()
    {
        var evt = EventBuilder<ConnectedEvent>.Create(raw => new ConnectedEvent
        {
            RawMessage = raw
        }).Build();

        Assert.NotNull(evt.RawMessage);
        Assert.Same(DummyIrcMessage.Empty, evt.RawMessage);
    }

    [Fact]
    public void Build_WithExplicitTimestamp_UsesProvidedValue()
    {
        var ts = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var evt = EventBuilder<ConnectedEvent>.Create(raw => new ConnectedEvent
        {
            RawMessage = raw
        }).At(ts).Build();

        Assert.Equal(ts, evt.Timestamp);
    }

    [Fact]
    public void Build_WithCustomRawMessage_PassesToFactory()
    {
        var custom = DummyIrcMessage.Privmsg;
        var evt = EventBuilder<ConnectedEvent>.Create(raw => new ConnectedEvent
        {
            RawMessage = raw
        }).WithRawMessage(custom).Build();

        Assert.Same(custom, evt.RawMessage);
    }

    [Fact]
    public void Build_MessageEvent_WithRequiredProperties()
    {
        var sender = MockUser.Create("alice");
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Sender = sender,
            Text = "hello",
            RawMessage = raw
        }).Build();

        Assert.Equal("hello", evt.Text);
        Assert.Equal("alice", evt.Sender.Nick);
        Assert.NotEqual(default, evt.Timestamp);
    }

    [Fact]
    public void Build_MessageEvent_WithChannel()
    {
        var channel = MockChannel.Create("#general");
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Sender = MockUser.Create("alice"),
            Text = "hello",
            Channel = channel,
            RawMessage = raw
        }).Build();

        Assert.NotNull(evt.Channel);
        Assert.Equal("#general", evt.Channel!.Name);
        Assert.False(evt.IsDirect);
    }

    [Fact]
    public void Build_PreservesExplicitTimestampFromFactory()
    {
        var ts = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var evt = EventBuilder<ConnectedEvent>.Create(raw => new ConnectedEvent
        {
            RawMessage = raw,
            Timestamp = ts
        }).Build();

        Assert.Equal(ts, evt.Timestamp);
    }
}
