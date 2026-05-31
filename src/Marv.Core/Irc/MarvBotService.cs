using Marv.Core.Events;
using Marv.Core.Plugin;
using Marv.Core.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marv.Core.Irc;

/// <summary>
/// Hosted service that orchestrates the bot's lifecycle: plugin loading,
/// IRC connection, message processing, reconnection, and graceful shutdown.
/// </summary>
internal sealed class MarvBotService : BackgroundService
{
    private readonly ILogger<MarvBotService> _logger;
    private readonly IrcBot _bot;
    private readonly PluginManager _pluginManager;
    private readonly IReadOnlyList<PluginDescriptor> _descriptors;
    private readonly IrcConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;

    // Reconnection backoff
    private const int InitialBackoffSeconds = 5;
    private const int MaxBackoffSeconds = 300;

    public MarvBotService(
        ILogger<MarvBotService> logger,
        IrcBot bot,
        PluginManager pluginManager,
        IReadOnlyList<PluginDescriptor> descriptors,
        IOptions<IrcConfiguration> config,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _bot = bot;
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
            try
            {
                // Phase 1: Instantiate and load plugins
                _pluginManager.InstantiatePlugins(_descriptors);
                await _pluginManager.LoadPluginsAsync(stoppingToken);
                _pluginManager.LogDiagnostics();

                // Phase 2: Connect to IRC
                var connection = new IrcConnection(_loggerFactory.CreateLogger<IrcConnection>());
                await using (connection)
                {
                    await connection.ConnectAsync(
                        _config.Server, _config.Port, _config.UseTls, stoppingToken);

                    // Phase 3: Notify plugins and start event dispatchers
                    await _pluginManager.NotifyConnectedAsync(stoppingToken);
                    var eventWriters = _pluginManager.StartEventDispatchers(stoppingToken);

                    // Phase 4: Run the message processor (blocks until disconnect)
                    backoff = InitialBackoffSeconds; // Reset backoff on successful connection
                    await _bot.RunAsync(connection, _config, eventWriters, stoppingToken);
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
                var disconnectMessage = new IrcMessage(null, null, "DISCONNECT", []);
                var disconnectedEvent = new DisconnectedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    RawMessage = disconnectMessage
                };

                await _pluginManager.NotifyDisconnectedAsync();
                await _pluginManager.UnloadPluginsAsync();
                _bot.ResetState();
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
