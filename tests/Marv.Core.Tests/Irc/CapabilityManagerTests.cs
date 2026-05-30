using Xunit;
using Marv.Core.Irc;

namespace Marv.Core.Tests.Irc;

/// <summary>
/// Tests for <see cref="CapabilityManager"/> capability negotiation state management.
/// </summary>
public class CapabilityManagerTests
{
    [Fact]
    public void SetAvailable_MakesCapAvailable()
    {
        var mgr = new CapabilityManager();
        mgr.SetAvailable("server-time", null);
        Assert.True(mgr.IsAvailable("server-time"));
        Assert.False(mgr.IsNegotiated("server-time"));
    }

    [Fact]
    public void SetNegotiated_MakesCapNegotiated()
    {
        var mgr = new CapabilityManager();
        mgr.SetAvailable("echo-message", null);
        mgr.SetNegotiated("echo-message");
        Assert.True(mgr.IsNegotiated("echo-message"));
    }

    [Fact]
    public void RemoveCapability_RemovesBothAvailableAndNegotiated()
    {
        var mgr = new CapabilityManager();
        mgr.SetAvailable("echo-message", null);
        mgr.SetNegotiated("echo-message");
        mgr.RemoveCapability("echo-message");
        Assert.False(mgr.IsAvailable("echo-message"));
        Assert.False(mgr.IsNegotiated("echo-message"));
    }

    [Fact]
    public void RemoveCapability_FiresChangedEvent()
    {
        var mgr = new CapabilityManager();
        mgr.SetAvailable("echo-message", null);
        var fired = false;
        mgr.CapabilitiesChanged += (_, _) => fired = true;
        mgr.RemoveCapability("echo-message");
        Assert.True(fired);
    }

    [Fact]
    public void AddNewCapability_FiresChangedEvent()
    {
        var mgr = new CapabilityManager();
        var fired = false;
        mgr.CapabilitiesChanged += (_, _) => fired = true;
        mgr.AddNewCapability("server-time", null);
        Assert.True(fired);
        Assert.True(mgr.IsAvailable("server-time"));
    }

    [Fact]
    public void AvailableCapabilities_IncludesValues()
    {
        var mgr = new CapabilityManager();
        mgr.SetAvailable("sasl", "PLAIN,EXTERNAL");
        Assert.Equal("PLAIN,EXTERNAL", mgr.AvailableCapabilities["sasl"]);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var mgr = new CapabilityManager();
        mgr.SetAvailable("echo-message", null);
        mgr.SetNegotiated("echo-message");
        mgr.Reset();
        Assert.False(mgr.IsAvailable("echo-message"));
        Assert.False(mgr.IsNegotiated("echo-message"));
        Assert.Empty(mgr.NegotiatedCapabilities);
    }

    [Fact]
    public void IsAvailable_CaseInsensitive()
    {
        var mgr = new CapabilityManager();
        mgr.SetAvailable("Server-Time", null);
        Assert.True(mgr.IsAvailable("server-time"));
    }
}
