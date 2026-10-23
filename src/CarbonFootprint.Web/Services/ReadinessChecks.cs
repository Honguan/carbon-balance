using CarbonFootprint.Infrastructure.Evidence;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarbonFootprint.Web.Services;

public sealed class ObjectStorageReadinessCheck(EvidenceStorageService storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await storage.IsAvailableAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}

public sealed class MalwareScannerReadinessCheck(ClamAvMalwareScanner scanner) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await scanner.IsAvailableAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}

public sealed class KeyRingReadinessCheck(IKeyManager keyManager, IConfiguration configuration, IHostEnvironment environment) : IHealthCheck
{
    private readonly object _gate = new();
    private Task<HealthCheckResult>? _inspection;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<HealthCheckResult> inspection;
        lock (_gate)
        {
            // ponytail: one in-flight filesystem read; OS I/O cannot be forcibly cancelled. Use a local persistent volume.
            if (_inspection is null || _inspection.IsCompleted)
            {
                _inspection = Task.Run(Inspect);
            }
            inspection = _inspection;
        }
        return await inspection.WaitAsync(cancellationToken);
    }

    private HealthCheckResult Inspect()
    {
        var path = configuration["DataProtection:KeyPath"];
        if (string.IsNullOrWhiteSpace(path) && environment.IsDevelopment())
        {
            return HealthCheckResult.Healthy();
        }
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return HealthCheckResult.Unhealthy();
            }
            var now = DateTimeOffset.UtcNow;
            // Read the repository, rather than the provider's cached key ring. Never create/rotate keys on GET.
            var keys = keyManager.GetAllKeys();
            foreach (var file in Directory.EnumerateFiles(path, "key-*.xml"))
            {
                var element = XElement.Load(file);
                if (element.Name != "key" || !Guid.TryParse(element.Attribute("id")?.Value, out var id)
                    || !keys.Any(key => key.KeyId == id))
                {
                    return HealthCheckResult.Unhealthy();
                }
            }
            foreach (var key in keys)
            {
                if (!key.IsRevoked && key.CreateEncryptor() is null)
                {
                    return HealthCheckResult.Unhealthy();
                }
            }
            return keys.Any(key => !key.IsRevoked && key.ActivationDate <= now && key.ExpirationDate > now)
                ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy();
        }
    }

    public static void Initialize(IServiceProvider services, string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using (var probe = new FileStream(Path.Combine(path, $".startup-probe-{Guid.NewGuid():N}"),
                       FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                probe.WriteByte(1);
                probe.Position = 0;
                if (probe.ReadByte() != 1)
                {
                    throw new IOException();
                }
            }
            // Initialization is an explicit startup operation; an empty persistent ring is supported.
            var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("CarbonFootprint.StartupKeyRingProbe.v1");
            if (protector.Unprotect(protector.Protect("startup-probe")) != "startup-probe")
            {
                throw new InvalidOperationException();
            }
            if (services.GetRequiredService<KeyRingReadinessCheck>().Inspect().Status != HealthStatus.Healthy)
            {
                throw new InvalidOperationException();
            }
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Data Protection 持久化金鑰路徑或金鑰不可用。");
        }
    }
}
