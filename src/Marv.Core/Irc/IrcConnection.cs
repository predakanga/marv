using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
    private readonly TokenBucketRateLimiter _rateLimiter;
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private CancellationTokenSource? _connectionCts;
    private Task? _readTask;
    private Task? _writeTask;

    private Channel<IrcMessage>? _inboundChannel;
    private Channel<IrcMessage>? _outboundChannel;

    /// <summary>
    /// Creates a new IRC connection with the specified rate limiting settings.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="rateLimitEnabled">Whether to enable outbound rate limiting.</param>
    /// <param name="burstLimit">Maximum messages in a burst before throttling.</param>
    /// <param name="refillRatePerSecond">Tokens replenished per second.</param>
    public IrcConnection(ILogger logger, bool rateLimitEnabled = true, int burstLimit = 5, double refillRatePerSecond = 0.5)
    {
        _logger = logger;
        _rateLimiter = new TokenBucketRateLimiter(rateLimitEnabled, burstLimit, refillRatePerSecond);
    }

    /// <summary>Reader for inbound parsed IRC messages from the server.</summary>
    public ChannelReader<IrcMessage> Inbound => _inboundChannel?.Reader
        ?? throw new InvalidOperationException("Not connected.");

    /// <summary>Writer for outbound IRC messages to the server.</summary>
    public ChannelWriter<IrcMessage> Outbound => _outboundChannel?.Writer
        ?? throw new InvalidOperationException("Not connected.");

    /// <summary>True if the connection is currently established.</summary>
    public bool IsConnected => _tcpClient?.Connected == true;

    /// <summary>Statistics object for recording byte counts. Set by IrcBot before processing begins.</summary>
    internal BotStatistics? Statistics { get; set; }

    /// <summary>Number of items waiting in the outbound channel.</summary>
    public int OutboundQueueCount => _outboundChannel?.Reader.Count ?? 0;

    /// <summary>Drains all pending messages from the outbound channel.</summary>
    public int DrainOutboundQueue()
    {
        if (_outboundChannel is null) return 0;
        var drained = 0;
        while (_outboundChannel.Reader.TryRead(out _))
            drained++;
        return drained;
    }

    /// <summary>
    /// Establishes a TCP (optionally TLS) connection to the IRC server and starts
    /// the read/write loop tasks.
    /// </summary>
    /// <param name="host">Server hostname.</param>
    /// <param name="port">Server port.</param>
    /// <param name="useTls">Whether to upgrade the connection to TLS.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="skipCertValidation">When true, accepts any server certificate.</param>
    /// <param name="caCertFile">Optional PEM CA certificate file for custom trust.</param>
    public async Task ConnectAsync(
        string host, int port, bool useTls, CancellationToken ct,
        bool skipCertValidation = false, string? caCertFile = null)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, ct);

        Stream stream = _tcpClient.GetStream();

        if (useTls)
        {
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = host,
            };

            if (skipCertValidation)
            {
                _logger.LogWarning("TLS certificate validation is disabled — connection is not verified");
                sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }
            else if (caCertFile is not null)
            {
                var customCa = X509CertificateLoader.LoadCertificateFromFile(caCertFile);
                sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
                {
                    if (errors == SslPolicyErrors.None)
                        return true;

                    if (cert is null || chain is null)
                        return false;

                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(customCa);
                    return chain.Build(new X509Certificate2(cert));
                };
            }

            var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsClientAsync(sslOptions, ct);
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

        _rateLimiter.Reset();

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

                // Count bytes: line content + \r\n terminator
                Statistics?.AddBytesReceived(Encoding.UTF8.GetByteCount(line) + 2);

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
                await _rateLimiter.WaitAsync(ct);

                var line = IrcSerializer.Serialize(message);
                _logger.LogTrace(">> {Line}", line);

                var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
                await _stream!.WriteAsync(bytes, ct);
                await _stream.FlushAsync(ct);
                Statistics?.AddBytesSent(bytes.Length);
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

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
