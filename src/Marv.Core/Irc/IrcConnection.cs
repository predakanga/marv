using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Marv.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace Marv.Core.Irc;

/// <summary>
/// Manages the TCP/TLS connection to an IRC server. Provides inbound and outbound
/// channels for parsed IRC messages, with a rate limiter on outbound sends.
/// </summary>
internal sealed class IrcConnection : IAsyncDisposable
{
    private readonly ILogger _logger;
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private CancellationTokenSource? _connectionCts;
    private Task? _readTask;
    private Task? _writeTask;

    private Channel<IrcMessage>? _inboundChannel;
    private Channel<IrcMessage>? _outboundChannel;

    // Rate limiter: token bucket — 5 messages burst, refill 1 per 2 seconds
    private const int BurstLimit = 5;
    private const double RefillRatePerSecond = 0.5;
    private double _tokens = BurstLimit;
    private DateTimeOffset _lastRefill = DateTimeOffset.UtcNow;
    private readonly object _rateLock = new();

    public IrcConnection(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Reader for inbound parsed IRC messages from the server.</summary>
    public ChannelReader<IrcMessage> Inbound => _inboundChannel?.Reader
        ?? throw new InvalidOperationException("Not connected.");

    /// <summary>Writer for outbound IRC messages to the server.</summary>
    public ChannelWriter<IrcMessage> Outbound => _outboundChannel?.Writer
        ?? throw new InvalidOperationException("Not connected.");

    /// <summary>True if the connection is currently established.</summary>
    public bool IsConnected => _tcpClient?.Connected == true;

    /// <summary>
    /// Establishes a TCP (optionally TLS) connection to the IRC server and starts
    /// the read/write loop tasks.
    /// </summary>
    public async Task ConnectAsync(string host, int port, bool useTls, CancellationToken ct)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, ct);

        Stream stream = _tcpClient.GetStream();

        if (useTls)
        {
            var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsClientAsync(host);
            stream = sslStream;
        }

        _stream = stream;
        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _inboundChannel = Channel.CreateUnbounded<IrcMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _outboundChannel = Channel.CreateBounded<IrcMessage>(new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        _tokens = BurstLimit;
        _lastRefill = DateTimeOffset.UtcNow;

        var loopCt = _connectionCts.Token;
        _readTask = Task.Run(() => ReadLoopAsync(loopCt), loopCt);
        _writeTask = Task.Run(() => WriteLoopAsync(loopCt), loopCt);

        _logger.LogInformation("Connected to {Host}:{Port} (TLS: {UseTls})", host, port, useTls);
    }

    /// <summary>
    /// Closes the connection and stops the read/write loops.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_connectionCts is not null)
        {
            await _connectionCts.CancelAsync();
        }

        _outboundChannel?.Writer.TryComplete();
        _inboundChannel?.Writer.TryComplete();

        if (_readTask is not null)
        {
            try { await _readTask; } catch (OperationCanceledException) { }
        }

        if (_writeTask is not null)
        {
            try { await _writeTask; } catch (OperationCanceledException) { }
        }

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        _tcpClient?.Dispose();
        _tcpClient = null;

        _connectionCts?.Dispose();
        _connectionCts = null;

        _logger.LogInformation("Disconnected");
    }

    /// <summary>
    /// Reads lines from the TCP stream, parses them into IrcMessage, and writes
    /// to the inbound channel. Runs until cancelled or the stream closes.
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var writer = _inboundChannel!.Writer;

        try
        {
            using var reader = new StreamReader(_stream!, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096, leaveOpen: true);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                {
                    _logger.LogInformation("Server closed the connection");
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                _logger.LogTrace("<< {Line}", line);

                var message = IrcParser.Parse(line);
                if (message is not null)
                {
                    await writer.WriteAsync(message, ct);
                }
                else
                {
                    _logger.LogWarning("Failed to parse IRC message: {Line}", line);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Read loop terminated due to I/O error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read loop terminated unexpectedly");
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Reads from the outbound channel, applies rate limiting, serializes, and
    /// writes to the TCP stream. Runs until cancelled or the channel completes.
    /// </summary>
    private async Task WriteLoopAsync(CancellationToken ct)
    {
        var reader = _outboundChannel!.Reader;

        try
        {
            await foreach (var message in reader.ReadAllAsync(ct))
            {
                await WaitForTokenAsync(ct);

                var line = IrcSerializer.Serialize(message);
                _logger.LogTrace(">> {Line}", line);

                var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
                await _stream!.WriteAsync(bytes, ct);
                await _stream.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Write loop terminated due to I/O error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Write loop terminated unexpectedly");
        }
    }

    /// <summary>
    /// Token bucket rate limiter. Waits until a token is available before allowing a send.
    /// </summary>
    private async Task WaitForTokenAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_rateLock)
            {
                var now = DateTimeOffset.UtcNow;
                var elapsed = (now - _lastRefill).TotalSeconds;
                _tokens = Math.Min(BurstLimit, _tokens + elapsed * RefillRatePerSecond);
                _lastRefill = now;

                if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    return;
                }
            }

            await Task.Delay(100, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
