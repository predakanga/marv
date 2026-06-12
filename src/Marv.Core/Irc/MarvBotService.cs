using Marv.Core.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marv.Core.Irc;

/// <summary>
/// Hosted service that orchestrates the bot's lifecycle: plugin loading,
/// IRC connection, message processing, reconnection, and graceful shutdown.
/// Each connection iteration creates a new DI scope so that connection-scoped
/// services (IrcBot, ServerInfo, CapabilityManager, etc.) receive fresh instances.
/// </summary>
internal sealed class MarvBotService : BackgroundService
{
    private readonly ILogger<MarvBotService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PluginManager _pluginManager;
    private readonly IReadOnlyList<PluginDescriptor> _descriptors;
    private readonly MarvConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;

    // Reconnection backoff
    private const int InitialBackoffSeconds = 5;
    private const int MaxBackoffSeconds = 300;

    public MarvBotService(
        ILogger<MarvBotService> logger,
        IServiceScopeFactory scopeFactory,
        PluginManager pluginManager,
        IReadOnlyList<PluginDescriptor> descriptors,
        IOptions<MarvConfiguration> config,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _pluginManager = pluginManager;
        _descriptors = descriptors;
        _config = config.Value;
        _loggerFactory = loggerFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = InitialBackoffSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var connectionScope = _scopeFactory.CreateAsyncScope();
            var scopedProvider = connectionScope.ServiceProvider;

            try
            {
                // Phase 1: Instantiate and load plugins (using scoped provider)
                _pluginManager.InstantiatePlugins(_descriptors, scopedProvider);
                await _pluginManager.LoadPluginsAsync(stoppingToken);
                _pluginManager.LogDiagnostics();

                // Phase 2: Resolve the scoped bot and connect to IRC
                var bot = scopedProvider.GetRequiredService<IrcBot>();
                var connection = new IrcConnection(
                    _loggerFactory.CreateLogger<IrcConnection>(),
                    _config.RateLimitEnabled,
                    _config.RateLimitBurst,
                    _config.RateLimitRefillRate);
                await using (connection)
                {
                    await connection.ConnectAsync(
                        _config.Server, _config.Port, _config.UseTls, stoppingToken,
                        _config.TlsSkipCertificateValidation, _config.TlsCaCertFile);

                    // Phase 3: Notify plugins and start event dispatchers
                    await _pluginManager.NotifyConnectedAsync(stoppingToken);
                    var eventWriters = _pluginManager.StartEventDispatchers(stoppingToken);

                    // Phase 4: Run the message processor (blocks until disconnect)
                    backoff = InitialBackoffSeconds; // Reset backoff on successful connection
                    await bot.RunAsync(connection, _config, eventWriters, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Bot shutdown requested");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bot disconnected unexpectedly");
            }

            // Cleanup after disconnect
            try
            {
                await _pluginManager.NotifyDisconnectedAsync();
                await _pluginManager.UnloadPluginsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disconnect cleanup");
            }

            if (stoppingToken.IsCancellationRequested) break;

            // Exponential backoff before reconnection
            _logger.LogInformation("Reconnecting in {Seconds} seconds...", backoff);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(backoff), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = Math.Min(backoff * 2, MaxBackoffSeconds);
        }

        // Final cleanup
        try
        {
            await _pluginManager.NotifyDisconnectedAsync();
            await _pluginManager.UnloadPluginsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during final shutdown cleanup");
        }

        _logger.LogInformation("Bot stopped");
    }
}
