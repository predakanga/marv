using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marv.Testing;

/// <summary>
/// Creates a plugin instance with a mocked <see cref="IBot"/>, a real
/// <see cref="IPluginActivator"/> backed by a connection-scoped
/// <see cref="IServiceProvider"/>, and a <see cref="NullLoggerFactory"/>.
/// Reduces plugin test setup to a single call.
/// </summary>
/// <remarks>
/// <para>
/// Services are registered as scoped to match the production lifetime semantics,
/// where core services are scoped to each IRC connection.
/// </para>
/// <para>
/// <see cref="MarvPlugin.OnLoadAsync"/> is NOT called automatically — tests may need
/// to set up additional state before loading. Call <see cref="LoadAsync"/> explicitly.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var harness = PluginTestHarness&lt;GreetPlugin&gt;.Create();
/// await harness.LoadAsync();
/// // exercise the plugin...
/// </code>
/// </example>
/// <typeparam name="TPlugin">The concrete plugin type to test.</typeparam>
public sealed class PluginTestHarness<TPlugin> : IDisposable where TPlugin : MarvPlugin
{
    private readonly ServiceProvider _rootProvider;
    private readonly IServiceScope _scope;

    /// <summary>The plugin instance under test.</summary>
    public TPlugin Plugin { get; }

    /// <summary>The mocked <see cref="IBot"/> injected into the plugin.</summary>
    public IBot Bot { get; }

    /// <summary>The scoped service provider backing the plugin activator.</summary>
    public IServiceProvider Services { get; }

    private PluginTestHarness(TPlugin plugin, IBot bot, ServiceProvider rootProvider, IServiceScope scope)
    {
        Plugin = plugin;
        Bot = bot;
        _rootProvider = rootProvider;
        _scope = scope;
        Services = scope.ServiceProvider;
    }

    /// <summary>
    /// Creates a new test harness for the specified plugin type.
    /// </summary>
    /// <param name="configureServices">
    /// Optional callback to register additional services (e.g. configuration objects,
    /// inter-plugin service mocks) before the plugin is constructed.
    /// </param>
    /// <param name="bot">
    /// Optional custom <see cref="IBot"/> mock. If null, <see cref="MockBot.Create()"/>
    /// is used.
    /// </param>
    public static PluginTestHarness<TPlugin> Create(
        Action<IServiceCollection>? configureServices = null,
        IBot? bot = null)
    {
        bot ??= MockBot.Create();

        var services = new ServiceCollection();
        services.AddScoped<IBot>(_ => bot);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddScoped<IPluginActivator, TestPluginActivator>();

        configureServices?.Invoke(services);

        var rootProvider = services.BuildServiceProvider();
        var scope = rootProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;

        var activator = scopedProvider.GetRequiredService<IPluginActivator>();
        var plugin = activator.CreateInstance<TPlugin>();
        return new PluginTestHarness<TPlugin>(plugin, bot, rootProvider, scope);
    }

    /// <summary>
    /// Calls <see cref="MarvPlugin.OnLoadAsync"/> on the plugin.
    /// Call this explicitly after any additional setup.
    /// </summary>
    public Task LoadAsync(CancellationToken ct = default) =>
        Plugin.OnLoadAsync(ct);

    /// <summary>
    /// Calls <see cref="MarvPlugin.OnConnectedAsync"/> on the plugin.
    /// </summary>
    public Task ConnectedAsync(CancellationToken ct = default) =>
        Plugin.OnConnectedAsync(ct);

    /// <summary>
    /// Dispatches an event to the plugin's handler pipeline.
    /// </summary>
    public Task HandleEventAsync(Core.Events.MarvEvent evt, CancellationToken ct = default) =>
        Plugin.HandleEventAsync(evt, ct);

    /// <inheritdoc />
    public void Dispose()
    {
        _scope.Dispose();
        _rootProvider.Dispose();
    }

    /// <summary>
    /// Activator implementation that uses the test service provider.
    /// Uses <see cref="ActivatorUtilities"/> for constructor injection,
    /// matching the production <c>PluginActivator</c> behavior.
    /// </summary>
    private sealed class TestPluginActivator(IServiceProvider serviceProvider) : IPluginActivator
    {
        public T CreateInstance<T>(params object[] parameters) =>
            ActivatorUtilities.CreateInstance<T>(serviceProvider, parameters);
    }
}
