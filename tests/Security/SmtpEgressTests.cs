using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Security.Tests;

public sealed class SmtpEgressTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.1.2")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.2")]
    [InlineData("169.254.169.254")]
    [InlineData("100.100.100.200")]
    [InlineData("168.63.129.16")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("2001:4860:4860::8888%1")]
    public async Task ProductionRejectsInternalAndReservedDestinations(string address)
    {
        var policy = new SmtpEgressPolicy(new MailOptions(), false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ValidateAsync(address, 587, true, default));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public async Task PublicDestinationsRequireStrictTls(string address)
    {
        var policy = new SmtpEgressPolicy(new MailOptions(), false);
        var destination = await policy.ValidateAsync(address, 587, true, default);
        Assert.Equal(SecureSocketOptions.StartTls, destination.Security);
        Assert.Equal(SecureSocketOptions.SslOnConnect, (await policy.ValidateAsync(address, 465, true, default)).Security);
        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ValidateAsync(address, 587, false, default));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("169.254.169.254")]
    public async Task MixedDnsAnswersRejectEntireDestination(string internalAddress)
    {
        var policy = new SmtpEgressPolicy(new MailOptions(), false,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse(internalAddress) }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ValidateAsync("smtp.example.test", 587, true, default));
    }

    [Fact]
    public async Task TrustedRelayRequiresMatchingHostPortAndRange()
    {
        var options = new MailOptions
        {
            TrustedRelays = [new SmtpRelayOptions { Host = "relay.example.test", Port = 2525, AddressRanges = ["10.1.0.0/16"], AllowInsecure = true }]
        };
        var policy = new SmtpEgressPolicy(options, false, (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.1.2.3") }));
        Assert.Equal(SecureSocketOptions.None, (await policy.ValidateAsync("relay.example.test", 2525, false, default)).Security);
        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ValidateAsync("tenant.example.test", 2525, true, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ValidateAsync("relay.example.test", 25, true, default));
        var outsideRange = new SmtpEgressPolicy(options, false, (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.2.2.3") }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => outsideRange.ValidateAsync("relay.example.test", 2525, true, default));
    }

    [Fact]
    public async Task DevelopmentMailpitExceptionIsExplicitAndUnavailableInProduction()
    {
        var options = new MailOptions();
        var loopback = new SmtpEgressPolicy(options, true, (_, _) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }));
        Assert.Equal(SecureSocketOptions.None, (await loopback.ValidateAsync("localhost", 1025, false, default)).Security);
        await Assert.ThrowsAsync<InvalidOperationException>(() => loopback.ValidateAsync("localhost", 25, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => loopback.ValidateAsync("other.test", 1025, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SmtpEgressPolicy(options, false).ValidateAsync("localhost", 1025, false, default));

        options.DevelopmentMailpit = new SmtpRelayOptions { Host = "mailpit", Port = 1025, AddressRanges = ["172.16.0.0/12"], AllowInsecure = true };
        var compose = new SmtpEgressPolicy(options, true, (_, _) => Task.FromResult(new[] { IPAddress.Parse("172.18.0.4") }));
        Assert.Equal(SecureSocketOptions.None, (await compose.ValidateAsync("mailpit", 1025, false, default)).Security);
        await Assert.ThrowsAsync<InvalidOperationException>(() => compose.ValidateAsync("postgres", 1025, true, default));
    }

    [Theory]
    [InlineData(1025)]
    [InlineData(1)]
    public async Task BrowserCiProfilesPermitOnlyTheirConfiguredLoopbackEndpoint(int port)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:Host"] = "127.0.0.1",
            ["Mail:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Mail:DevelopmentMailpit:Host"] = "127.0.0.1",
            ["Mail:DevelopmentMailpit:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Mail:DevelopmentMailpit:AddressRanges:0"] = "127.0.0.1/32"
        }).Build();
        var options = configuration.GetSection("Mail").Get<MailOptions>()!;
        var development = new SmtpEgressPolicy(options, true);
        Assert.Equal(SecureSocketOptions.None, (await development.ValidateAsync(options.Host, options.Port, options.EnableSsl, default)).Security);
        await Assert.ThrowsAsync<InvalidOperationException>(() => development.ValidateAsync(options.Host, port + 1, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => development.ValidateAsync("127.0.0.2", port, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SmtpEgressPolicy(options, false).ValidateAsync(options.Host, port, false, default));
    }

    [Fact]
    public async Task BrowserCiOutageProfileReachesActualTransportFailure()
    {
        var options = new MailOptions
        {
            Host = "127.0.0.1",
            Port = 1,
            TimeoutSeconds = 5,
            DevelopmentMailpit = new SmtpRelayOptions { Host = "127.0.0.1", Port = 1, AddressRanges = ["127.0.0.1/32"], AllowInsecure = true }
        };
        using var db = CreateDb();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSender(options, db, new SmtpEgressPolicy(options, true)).SendTestMessageAsync("recipient@example.test").WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("SMTP 寄送失敗，請確認目的地、TLS 憑證與登入設定。", error.Message);
    }

    [Fact]
    public async Task ExistingGlobalSettingsAreValidatedBeforeOpeningSocket()
    {
        var options = new MailOptions { Host = "127.0.0.1", Port = 587, EnableSsl = true };
        using var db = CreateDb();
        var sender = CreateSender(options, db, new SmtpEgressPolicy(options, false));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendTestMessageAsync("recipient@example.test"));
        Assert.Contains("政策拒絕", error.Message);
    }

    [Fact]
    public async Task DnsDeadlineAndCallerCancellationAreHonored()
    {
        var options = new MailOptions { Host = "smtp.example.test", EnableSsl = true, TimeoutSeconds = 1 };
        var policy = new SmtpEgressPolicy(options, false, (_, _) => new TaskCompletionSource<IPAddress[]>().Task);
        using var db = CreateDb();
        var sender = CreateSender(options, db, policy);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendTestMessageAsync("recipient@example.test").WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("逾時", error.Message);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendTestMessageAsync("recipient@example.test", cancelled.Token));
    }

    [Fact]
    public async Task MailpitSendUsesValidatedIpWithoutResolvingHostnameAgain()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var options = LocalOptions(listener);
        var lookups = 0;
        var policy = new SmtpEgressPolicy(options, true, (_, _) =>
        {
            lookups++;
            return Task.FromResult(new[] { lookups == 1 ? IPAddress.Loopback : IPAddress.Parse("169.254.169.254") });
        });
        var server = ServePlaintextAsync(listener);
        using var db = CreateDb();
        await CreateSender(options, db, policy).SendTestMessageAsync("recipient@example.test").WaitAsync(TimeSpan.FromSeconds(5));
        var transcript = await server;
        Assert.Contains("MAIL FROM:", transcript);
        Assert.Contains("recipient@example.test", transcript);
        Assert.Contains("DATA", transcript);
        Assert.Equal(1, lookups);
    }

    [Fact]
    public async Task StartTlsUnavailableFailsBeforeAuthenticationOrMessage()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var options = LocalOptions(listener);
        options.EnableSsl = true;
        options.Username = "must-not-be-sent";
        options.Password = "secret-must-not-be-sent";
        var server = ServePlaintextAsync(listener);
        using var db = CreateDb();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSender(options, db, LocalPolicy(options)).SendTestMessageAsync("recipient@example.test"));
        var transcript = await server;
        Assert.DoesNotContain("AUTH", transcript);
        Assert.DoesNotContain("MAIL FROM", transcript);
        Assert.DoesNotContain(options.Password, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledSmtpGreetingHonorsTotalDeadlineOrRequestAbort(bool requestAbort)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var options = LocalOptions(listener);
        options.TimeoutSeconds = 1;
        using var db = CreateDb();
        using var cancelled = new CancellationTokenSource();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestAborted = cancelled.Token } };
        var send = CreateSender(options, db, LocalPolicy(options), accessor).SendTestMessageAsync("recipient@example.test");
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (requestAbort)
        {
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("逾時", error.Message);
        }
    }

    [Fact]
    public async Task SocketTlsValidatesOriginalHostnameAndRejectsCertificateMismatch()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=wrong.example.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("wrong.example.test");
        request.CertificateExtensions.Add(san.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(deadline.Token);
            using var tls = new SslStream(accepted.GetStream());
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, deadline.Token);
            }
            catch (Exception error) when (error is IOException or System.Security.Authentication.AuthenticationException) { }
        });
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
        using var client = new SmtpClient();
        var observedErrors = SslPolicyErrors.None;
        client.ServerCertificateValidationCallback = (_, _, _, errors) =>
        {
            observedErrors = errors;
            // Ignore the test certificate's unknown issuer only; hostname validation must still reject it.
            return (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0;
        };
        var error = await Assert.ThrowsAsync<SslHandshakeException>(() => client.ConnectAsync(socket, "smtp.example.test", ((IPEndPoint)listener.LocalEndpoint).Port, SecureSocketOptions.SslOnConnect, deadline.Token));
        Assert.True(observedErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch), error.ToString());
        await server;
    }

    [Theory]
    [InlineData("STARTTLS", false)]
    [InlineData("AUTH", false)]
    [InlineData("SEND", false)]
    [InlineData("STARTTLS", true)]
    [InlineData("AUTH", true)]
    [InlineData("SEND", true)]
    public async Task StalledTlsAuthenticationAndSendAreBoundedAndCancellable(string phase, bool cancel)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var options = LocalOptions(listener);
        options.TimeoutSeconds = 1;
        options.EnableSsl = phase == "STARTTLS";
        options.Username = phase == "AUTH" ? "fixture-user" : string.Empty;
        options.Password = "fixture-password";
        var reachedPhase = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopServer = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(stopServer.Token);
            using var stream = accepted.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 fixture SMTP");
            while (await reader.ReadLineAsync(stopServer.Token) is { } line)
            {
                if (line.StartsWith("EHLO", StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync(phase == "STARTTLS" ? "250-fixture\r\n250 STARTTLS" : "250-fixture\r\n250 AUTH PLAIN");
                }
                else if (line == "STARTTLS" || line.StartsWith("AUTH", StringComparison.Ordinal) || (phase == "SEND" && line == "."))
                {
                    if (phase == "STARTTLS") await writer.WriteLineAsync("220 begin TLS");
                    reachedPhase.SetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, stopServer.Token); }
                    catch (OperationCanceledException) { }
                    break;
                }
                else if (line == "DATA") await writer.WriteLineAsync("354 continue");
                else if (line.StartsWith("MAIL FROM:", StringComparison.Ordinal) || line.StartsWith("RCPT TO:", StringComparison.Ordinal)) await writer.WriteLineAsync("250 OK");
            }
        });
        using var db = CreateDb();
        using var cancellation = new CancellationTokenSource();
        var send = CreateSender(options, db, LocalPolicy(options)).SendTestMessageAsync("recipient@example.test", cancellation.Token);
        await reachedPhase.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        stopServer.Cancel();
        await server;
    }

    [Fact]
    public async Task SenderRejectsInvalidTlsCertificateAndRetainsHostnameForSni()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=wrong.example.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("wrong.example.test");
        request.CertificateExtensions.Add(san.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var options = LocalOptions(listener);
        options.EnableSsl = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string? sni = null;
        var server = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream = accepted.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 fixture SMTP");
            Assert.StartsWith("EHLO", await reader.ReadLineAsync(deadline.Token));
            await writer.WriteLineAsync("250-fixture\r\n250 STARTTLS");
            Assert.Equal("STARTTLS", await reader.ReadLineAsync(deadline.Token));
            await writer.WriteLineAsync("220 begin TLS");
            using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificateSelectionCallback = (_, host) => { sni = host; return certificate; }
                }, deadline.Token);
            }
            catch (Exception error) when (error is IOException or System.Security.Authentication.AuthenticationException) { }
        });
        using var db = CreateDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSender(options, db, LocalPolicy(options)).SendTestMessageAsync("recipient@example.test"));
        await server;
        Assert.Equal(options.Host, sni);
    }

    private static MailOptions LocalOptions(TcpListener listener)
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new MailOptions
        {
            Host = "mailpit.example.test",
            Port = port,
            DevelopmentMailpit = new SmtpRelayOptions { Host = "mailpit.example.test", Port = port, AddressRanges = ["127.0.0.1/32"], AllowInsecure = true }
        };
    }

    private static SmtpEgressPolicy LocalPolicy(MailOptions options) =>
        new(options, true, (_, _) => Task.FromResult(new[] { IPAddress.Loopback }));

    private static CarbonFootprintDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CarbonFootprintDbContext>().UseNpgsql("Host=localhost;Database=unused").Options,
        new UnscopedOrganizationScope());

    private static SmtpEmailSender CreateSender(MailOptions options, CarbonFootprintDbContext db, SmtpEgressPolicy policy, IHttpContextAccessor? accessor = null) =>
        new(Options.Create(options), db, new UnscopedOrganizationScope(), new EphemeralDataProtectionProvider(), policy, accessor ?? new HttpContextAccessor());

    private static async Task<string> ServePlaintextAsync(TcpListener listener)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var accepted = await listener.AcceptTcpClientAsync(deadline.Token);
        using var stream = accepted.GetStream();
        using var reader = new StreamReader(stream);
        using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
        var transcript = new List<string>();
        await writer.WriteLineAsync("220 fixture SMTP");
        var inData = false;
        while (await reader.ReadLineAsync(deadline.Token) is { } line)
        {
            transcript.Add(line);
            if (inData && line != ".") continue;
            if (line.StartsWith("EHLO", StringComparison.Ordinal)) await writer.WriteLineAsync("250 fixture");
            else if (line == "DATA") { inData = true; await writer.WriteLineAsync("354 continue"); }
            else if (line == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
            else { inData = false; await writer.WriteLineAsync("250 OK"); }
        }
        return string.Join('\n', transcript);
    }
}
