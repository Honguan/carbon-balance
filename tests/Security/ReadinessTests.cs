using System.Net;
using System.Net.Sockets;
using System.Text;
using CarbonFootprint.Infrastructure.Evidence;
using CarbonFootprint.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Security.Tests;

public sealed class ReadinessTests
{
    [Fact]
    public async Task RequiredDependenciesFailAndRecoverOnSameApp_LivenessAndOptionalSourceRemainIndependent()
    {
        var storageStatus = 200;
        var scannerHealthy = true;
        await using var storage = new ProbeServer(stream => ReplyS3Async(stream, storageStatus));
        await using var scanner = new ProbeServer(stream => ReplyClamAsync(stream, scannerHealthy ? "PONG\0" : "FAIL\0"));
        var path = Path.Combine(Path.GetTempPath(), $"carbon-readiness-{Guid.NewGuid():N}");
        using var factory = new Factory(storage.Port, scanner.Port, path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var initial = await client.GetAsync("/health/ready");
        Assert.True(initial.StatusCode == HttpStatusCode.OK, await initial.Content.ReadAsStringAsync());
        var files = Directory.GetFiles(path);
        Assert.NotEmpty(files);
        var before = files.ToDictionary(file => file, file => (File.ReadAllText(file), File.GetLastWriteTimeUtc(file)));
        foreach (var component in new[] { "object-storage", "malware-scanner", "postgresql", "keyring" })
        {
            var key = files.First(file => Path.GetFileName(file).StartsWith("key-", StringComparison.Ordinal));
            if (component == "object-storage") storageStatus = 403;
            if (component == "malware-scanner") scannerHealthy = false;
            if (component == "postgresql") factory.DatabaseHealthy = false;
            if (component == "keyring") await File.WriteAllTextAsync(key, "<malformed");
            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            var body = await ready.Content.ReadAsStringAsync();
            Assert.Contains($"\"{component}\":\"Unhealthy\"", body);
            Assert.DoesNotContain("sensitive", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("127.0.0.1", body);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            storageStatus = 200;
            scannerHealthy = true;
            factory.DatabaseHealthy = true;
            if (component == "keyring")
            {
                await File.WriteAllTextAsync(key, before[key].Item1);
                File.SetLastWriteTimeUtc(key, before[key].Item2);
            }
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        }
        Assert.Equal(0, factory.OptionalSourceCalls);
        Assert.Equal(files.Order(), Directory.GetFiles(path).Order());
        foreach (var file in files)
        {
            Assert.Equal(before[file], (File.ReadAllText(file), File.GetLastWriteTimeUtc(file)));
        }
        Assert.All(storage.Commands, command => Assert.True(command.StartsWith("HEAD ", StringComparison.Ordinal)
            || command.StartsWith("GET ", StringComparison.Ordinal)));
        Assert.All(scanner.Commands, command => Assert.Equal("zPING\0", command));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    public async Task StorageProbeRejectsUnavailableBucket(int status)
    {
        await using var server = new ProbeServer(stream => ReplyS3Async(stream, status));
        var storage = CreateStorage(server.Port);
        var check = new ObjectStorageReadinessCheck(storage);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new(), deadline.Token)).Status);
        Assert.All(server.Commands, command => Assert.DoesNotContain("PUT ", command));
    }

    [Theory]
    [InlineData("PONG\0", true)]
    [InlineData("PONG\n", false)]
    [InlineData("FAIL\0", false)]
    [InlineData("PON", false)]
    public async Task ScannerProbeRequiresCompleteFramedPong(string response, bool healthy)
    {
        await using var server = new ProbeServer(stream => ReplyClamAsync(stream, response));
        var check = new MalwareScannerReadinessCheck(CreateScanner(server.Port));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Assert.Equal(healthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
            (await check.CheckHealthAsync(new(), deadline.Token)).Status);
    }

    [Fact]
    public async Task StalledNetworkChecksRespectCancellation()
    {
        await using var server = new ProbeServer(async stream =>
        {
            var buffer = new byte[1];
            while (await stream.ReadAsync(buffer) != 0) { }
        });
        foreach (var check in new IHealthCheck[]
                 { new ObjectStorageReadinessCheck(CreateStorage(server.Port)), new MalwareScannerReadinessCheck(CreateScanner(server.Port)) })
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var started = System.Diagnostics.Stopwatch.StartNew();
            Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new(), deadline.Token)).Status);
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void StartupRejectsKeyPathThatIsAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"carbon-invalid-keypath-{Guid.NewGuid():N}");
        File.WriteAllText(path, "fixture");
        using var factory = new Factory(1, 1, path);
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Equal("fixture", File.ReadAllText(path));
    }

    [Fact]
    public async Task HttpReadinessBoundsStalledScanner()
    {
        await using var storage = new ProbeServer(stream => ReplyS3Async(stream, 200));
        await using var scanner = new ProbeServer(async stream =>
        {
            await stream.ReadExactlyAsync(new byte[6]);
            _ = await stream.ReadAsync(new byte[1]);
        });
        using var factory = new Factory(storage.Port, scanner.Port,
            Path.Combine(Path.GetTempPath(), $"carbon-readiness-deadline-{Guid.NewGuid():N}"));
        using var client = factory.CreateClient();
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(6));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    private static EvidenceStorageService CreateStorage(int port) => new(Options.Create(new ObjectStorageOptions
    {
        Endpoint = $"http://127.0.0.1:{port}",
        Bucket = "readiness-evidence",
        AccessKey = "fixture-access",
        SecretKey = "sensitive-fixture-secret"
    }), CreateScanner(1));

    private static ClamAvMalwareScanner CreateScanner(int port) => new(Options.Create(new MalwareScannerOptions { Host = "127.0.0.1", Port = port }));

    private static async Task ReplyClamAsync(NetworkStream stream, string response)
    {
        var command = new byte[6];
        await stream.ReadExactlyAsync(command);
        ProbeServer.Record(stream, Encoding.ASCII.GetString(command));
        foreach (var value in Encoding.ASCII.GetBytes(response))
        {
            await stream.WriteAsync(new[] { value });
        }
    }

    private static async Task ReplyS3Async(NetworkStream stream, int status)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var command = await reader.ReadLineAsync() ?? string.Empty;
        ProbeServer.Record(stream, command);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
        var body = command.StartsWith("HEAD ", StringComparison.Ordinal) ? string.Empty
            : "<LocationConstraint xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">us-east-1</LocationConstraint>";
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Result\r\nContent-Length: {Encoding.ASCII.GetByteCount(body)}\r\nContent-Type: application/xml\r\nx-amz-bucket-region: us-east-1\r\nConnection: close\r\n\r\n{body}"));
    }

    private sealed class Factory(int storagePort, int scannerPort, string keyPath) : WebApplicationFactory<Program>
    {
        public volatile bool DatabaseHealthy = true;
        public int OptionalSourceCalls;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DataProtection:KeyPath", keyPath);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=unused_readiness_test",
                ["ObjectStorage:Endpoint"] = $"http://127.0.0.1:{storagePort}",
                ["ObjectStorage:Bucket"] = "readiness-evidence",
                ["ObjectStorage:AccessKey"] = "fixture-access",
                ["ObjectStorage:SecretKey"] = "sensitive-fixture-secret",
                ["MalwareScanner:Host"] = "127.0.0.1",
                ["MalwareScanner:Port"] = scannerPort.ToString(),
                ["DataProtection:KeyPath"] = keyPath
            }));
            builder.ConfigureServices(services =>
            {
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    options.Registrations.Remove(options.Registrations.Single(check => check.Name == "postgresql"));
                    options.Registrations.Add(new HealthCheckRegistration("postgresql", new DatabaseCheck(this),
                        HealthStatus.Unhealthy, ["ready"], TimeSpan.FromSeconds(3)));
                });
                services.AddHealthChecks().AddCheck("optional-moenv", () =>
                {
                    Interlocked.Increment(ref OptionalSourceCalls);
                    return HealthCheckResult.Unhealthy("sensitive optional topology");
                }, tags: ["optional"]);
            });
        }
    }

    private sealed class DatabaseCheck(Factory factory) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(factory.DatabaseHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("sensitive database topology"));
    }

    private sealed class ProbeServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<NetworkStream, ProbeServer> Owners = new();
        public readonly System.Collections.Concurrent.ConcurrentBag<string> Commands = new();
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public ProbeServer(Func<NetworkStream, Task> respond)
        {
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        using var stream = client.GetStream();
                        Owners[stream] = this;
                        try { await respond(stream).WaitAsync(_stop.Token); }
                        finally { Owners.TryRemove(stream, out _); }
                    }
                    catch (Exception) when (_stop.IsCancellationRequested) { break; }
                }
            });
        }
        public static void Record(NetworkStream stream, string command) => Owners[stream].Commands.Add(command);
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop.WaitAsync(TimeSpan.FromSeconds(3));
            _stop.Dispose();
        }
    }
}
