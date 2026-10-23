using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CarbonFootprint.Infrastructure.Evidence;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Security.Tests;

public sealed class EvidenceUploadTests : IDisposable
{
    private readonly string _quarantine = Path.Combine(Path.GetTempPath(), $"carbon-quarantine-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_quarantine))
        {
            Directory.Delete(_quarantine, recursive: true);
        }
    }

    [Fact]
    public void Gate_AdmitsConfiguredConcurrencyWithoutWaitingAndReleasesOnce()
    {
        var gate = new EvidenceUploadGate(Options.Create(new ObjectStorageOptions { MaximumConcurrentUploads = 2 }));
        var first = gate.TryEnter();
        var second = gate.TryEnter();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(gate.TryEnter());

        first!.Dispose();
        first.Dispose();
        var third = gate.TryEnter();
        Assert.NotNull(third);
        Assert.Null(gate.TryEnter());
        second!.Dispose();
        third!.Dispose();
    }

    [Fact]
    public async Task InfectedUpload_IsRejectedBeforeObjectStorageAndLeavesNoQuarantineFile()
    {
        await using var scanner = new ClamServer("stream: Eicar-Test-Signature FOUND\0");
        var service = CreateService(scanner.Port);

        var exception = await Assert.ThrowsAsync<EvidenceUploadException>(() => StoreAsync(service, new MemoryStream("evidence"u8.ToArray())));

        Assert.Contains("惡意程式", exception.Message);
        Assert.Empty(QuarantineFiles());
    }

    [Fact]
    public async Task IncompleteScannerFrame_IsNeverTreatedAsClean()
    {
        await using var scanner = new ClamServer("stream: OK");
        var service = CreateService(scanner.Port);

        var exception = await Assert.ThrowsAsync<EvidenceUploadException>(() => StoreAsync(service, new MemoryStream("evidence"u8.ToArray())));

        Assert.Contains("不完整", exception.Message);
        Assert.Empty(QuarantineFiles());
    }

    [Fact]
    public async Task OversizeAndEmptyUploads_AreRejectedWithoutQuarantineResidue()
    {
        var service = CreateService(1, maximumFileSizeBytes: 1024);

        await Assert.ThrowsAsync<EvidenceUploadException>(() => StoreAsync(service, new MemoryStream(new byte[2048])));
        await Assert.ThrowsAsync<EvidenceUploadException>(() => StoreAsync(service, new MemoryStream()));

        Assert.Empty(QuarantineFiles());
    }

    [Fact]
    public async Task SlowScanner_HitsTotalDeadlineWithSafeMessage()
    {
        await using var scanner = new ClamServer(reply: null);
        var service = CreateService(scanner.Port, timeoutSeconds: 1);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<EvidenceUploadException>(() => StoreAsync(service, new MemoryStream("evidence"u8.ToArray())));

        Assert.Contains("逾時", exception.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(8));
        Assert.Empty(QuarantineFiles());
    }

    [Fact]
    public async Task MaximumSizeUpload_DoesNotRetainFileInManagedMemory()
    {
        await using var scanner = new ClamServer("stream: Eicar FOUND\0");
        var service = CreateService(scanner.Port);
        await using var source = new PatternStream(10 * 1024 * 1024);

        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        await Assert.ThrowsAsync<EvidenceUploadException>(() => StoreAsync(service, source));
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.True(allocated < 4 * 1024 * 1024, $"allocated {allocated} bytes for a 10 MiB upload");
    }

    private EvidenceStorageService CreateService(int scannerPort, long maximumFileSizeBytes = 10 * 1024 * 1024, int timeoutSeconds = 30) =>
        new(
            Options.Create(new ObjectStorageOptions
            {
                Endpoint = "http://127.0.0.1:1",
                SecretKey = "fixture-secret",
                QuarantineDirectory = _quarantine,
                MaximumFileSizeBytes = maximumFileSizeBytes,
                UploadTimeoutSeconds = timeoutSeconds
            }),
            new ClamAvMalwareScanner(Options.Create(new MalwareScannerOptions { Host = "127.0.0.1", Port = scannerPort })));

    private static Task<StoredEvidence> StoreAsync(EvidenceStorageService service, Stream content) =>
        service.StoreAsync(Guid.NewGuid(), content, "evidence.bin", "application/octet-stream", CancellationToken.None);

    private string[] QuarantineFiles() => Directory.Exists(_quarantine) ? Directory.GetFiles(_quarantine) : [];

    private sealed class PatternStream(long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = (int)Math.Min(buffer.Length, length - _position);
            buffer.Span[..count].Fill(0x5A);
            _position += count;
            return ValueTask.FromResult(count);
        }
    }

    /// <summary>Minimal clamd INSTREAM peer: drains the stream, then sends the reply (or never replies when null).</summary>
    private sealed class ClamServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public ClamServer(string? reply)
        {
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        await using var stream = client.GetStream();
                        await stream.ReadExactlyAsync(new byte[10], _stop.Token);
                        var prefix = new byte[4];
                        var sink = new byte[64 * 1024];
                        while (true)
                        {
                            await stream.ReadExactlyAsync(prefix, _stop.Token);
                            var chunk = BinaryPrimitives.ReadInt32BigEndian(prefix);
                            if (chunk == 0)
                            {
                                break;
                            }

                            await stream.ReadExactlyAsync(sink.AsMemory(0, chunk), _stop.Token);
                        }

                        if (reply is null)
                        {
                            await Task.Delay(Timeout.Infinite, _stop.Token);
                        }

                        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply!), _stop.Token);
                    }
                }
                catch (Exception) when (_stop.IsCancellationRequested)
                {
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop.WaitAsync(TimeSpan.FromSeconds(3));
            _stop.Dispose();
        }
    }
}
