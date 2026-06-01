using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Marv.Core.Irc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marv.Core.Tests.Irc;

/// <summary>
/// Tests for TLS certificate handling in <see cref="IrcConnection"/>.
/// Uses a local TLS listener with a self-signed certificate.
/// </summary>
public class IrcConnectionTlsTests : IAsyncLifetime
{
    private TcpListener _listener = null!;
    private int _port;
    private X509Certificate2 _selfSignedCert = null!;
    private CancellationTokenSource _cts = null!;

    public Task InitializeAsync()
    {
        _selfSignedCert = CreateSelfSignedCert("localhost");
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _listener.Stop();
        _selfSignedCert.Dispose();
        _cts.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ConnectAsync_SelfSignedCert_FailsWithoutSkip()
    {
        _ = AcceptAndAuthenticateAsync();

        var connection = new IrcConnection(NullLogger.Instance, rateLimitEnabled: false);
        await using (connection)
        {
            await Assert.ThrowsAsync<AuthenticationException>(() =>
                connection.ConnectAsync("localhost", _port, useTls: true, _cts.Token));
        }
    }

    [Fact]
    public async Task ConnectAsync_SelfSignedCert_SucceedsWithSkip()
    {
        _ = AcceptAndAuthenticateAsync();

        var connection = new IrcConnection(NullLogger.Instance, rateLimitEnabled: false);
        await using (connection)
        {
            await connection.ConnectAsync(
                "localhost", _port, useTls: true, _cts.Token,
                skipCertValidation: true);

            Assert.True(connection.IsConnected);
        }
    }

    [Fact]
    public async Task ConnectAsync_SelfSignedCert_SucceedsWithCaCertFile()
    {
        var caCertPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(caCertPath, ExportCertAsPem(_selfSignedCert), _cts.Token);

            _ = AcceptAndAuthenticateAsync();

            var connection = new IrcConnection(NullLogger.Instance, rateLimitEnabled: false);
            await using (connection)
            {
                await connection.ConnectAsync(
                    "localhost", _port, useTls: true, _cts.Token,
                    caCertFile: caCertPath);

                Assert.True(connection.IsConnected);
            }
        }
        finally
        {
            File.Delete(caCertPath);
        }
    }

    [Fact]
    public async Task ConnectAsync_SelfSignedCert_FailsWithWrongCaCertFile()
    {
        var unrelatedCert = CreateSelfSignedCert("unrelated");
        var caCertPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(caCertPath, ExportCertAsPem(unrelatedCert), _cts.Token);

            _ = AcceptAndAuthenticateAsync();

            var connection = new IrcConnection(NullLogger.Instance, rateLimitEnabled: false);
            await using (connection)
            {
                await Assert.ThrowsAsync<AuthenticationException>(() =>
                    connection.ConnectAsync(
                        "localhost", _port, useTls: true, _cts.Token,
                        caCertFile: caCertPath));
            }
        }
        finally
        {
            unrelatedCert.Dispose();
            File.Delete(caCertPath);
        }
    }

    private async Task AcceptAndAuthenticateAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = _selfSignedCert,
                    ClientCertificateRequired = false,
                },
                _cts.Token);

            // Keep connection alive until cancelled so client can verify IsConnected
            try { await sslStream.CopyToAsync(Stream.Null, _cts.Token); }
            catch (OperationCanceledException) { }
        }
        catch (Exception)
        {
            // Client may reject the handshake — that's expected in failure tests
        }
        finally
        {
            await sslStream.DisposeAsync();
        }
    }

    private static X509Certificate2 CreateSelfSignedCert(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true, hasPathLengthConstraint: false,
                pathLengthConstraint: 0, critical: true));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(cn);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1));

        // Export and re-import to ensure private key is available on all platforms
        return X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx),
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable);
    }

    private static string ExportCertAsPem(X509Certificate2 cert)
    {
        return new string(PemEncoding.Write("CERTIFICATE", cert.RawData));
    }
}
