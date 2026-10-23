using Microsoft.Extensions.Options;

namespace CarbonFootprint.Infrastructure.Evidence;

/// <summary>User-safe failure: the message may be shown to the uploader.</summary>
public sealed class EvidenceUploadException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>Process-wide, non-waiting admission for multipart evidence uploads.</summary>
public sealed class EvidenceUploadGate(IOptions<ObjectStorageOptions> options)
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.MaximumConcurrentUploads));

    public IDisposable? TryEnter() => _slots.Wait(0) ? new Lease(_slots) : null;

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}
