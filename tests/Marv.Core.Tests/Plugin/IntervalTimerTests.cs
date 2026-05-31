using Marv.Core.Platform;
using Marv.Core.Plugin;
using NSubstitute;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests that [OnInterval] handlers run on a background timer,
/// independent of the event stream.
/// </summary>
public class IntervalTimerTests
{
    /// <summary>
    /// Minimal plugin with a single [OnInterval] handler that records invocations.
    /// </summary>
    private sealed class IntervalTestPlugin : MarvPlugin
    {
        public int InvocationCount;

        public IntervalTestPlugin(IBot bot, IPluginActivator activator) : base(bot, activator) { }

        [OnInterval(Seconds = 0.1)]
        public void Tick()
        {
            Interlocked.Increment(ref InvocationCount);
        }
    }

    private static IntervalTestPlugin CreatePlugin()
    {
        var bot = Substitute.For<IBot>();
        var activator = Substitute.For<IPluginActivator>();
        return new IntervalTestPlugin(bot, activator);
    }

    [Fact]
    public async Task IntervalHandler_FiresWithoutEvents()
    {
        var plugin = CreatePlugin();

        await plugin.OnLoadAsync(CancellationToken.None);

        // Wait enough time for several ticks (100ms interval)
        await Task.Delay(350);

        await plugin.OnUnloadAsync();

        // Should have fired at least twice in 350ms with a 100ms interval
        Assert.True(plugin.InvocationCount >= 2,
            $"Expected at least 2 invocations but got {plugin.InvocationCount}");
    }

    [Fact]
    public async Task IntervalHandler_StopsOnUnload()
    {
        var plugin = CreatePlugin();

        await plugin.OnLoadAsync(CancellationToken.None);
        await Task.Delay(250);
        await plugin.OnUnloadAsync();

        var countAfterStop = plugin.InvocationCount;
        Assert.True(countAfterStop >= 1,
            $"Expected at least 1 invocation but got {countAfterStop}");

        // Wait and verify no more ticks
        await Task.Delay(250);
        Assert.Equal(countAfterStop, plugin.InvocationCount);
    }

    [Fact]
    public async Task IntervalHandler_ResetsOnReload()
    {
        var plugin = CreatePlugin();

        await plugin.OnLoadAsync(CancellationToken.None);
        await Task.Delay(250);
        await plugin.OnUnloadAsync();

        var firstRunCount = plugin.InvocationCount;
        Assert.True(firstRunCount >= 1);

        // Reload — timers should start again
        plugin.InvocationCount = 0;
        await plugin.OnLoadAsync(CancellationToken.None);
        await Task.Delay(250);
        await plugin.OnUnloadAsync();

        Assert.True(plugin.InvocationCount >= 1,
            $"Expected ticks after reload but got {plugin.InvocationCount}");
    }

    /// <summary>
    /// Plugin with no interval handlers — verifies no background task is started.
    /// </summary>
    private sealed class NoIntervalPlugin : MarvPlugin
    {
        public NoIntervalPlugin(IBot bot, IPluginActivator activator) : base(bot, activator) { }
    }

    [Fact]
    public async Task NoIntervalHandlers_DoesNotStartTimer()
    {
        var bot = Substitute.For<IBot>();
        var activator = Substitute.For<IPluginActivator>();
        var plugin = new NoIntervalPlugin(bot, activator);

        // Should not throw or start any background task
        await plugin.OnLoadAsync(CancellationToken.None);
        await plugin.OnUnloadAsync();
    }
}
