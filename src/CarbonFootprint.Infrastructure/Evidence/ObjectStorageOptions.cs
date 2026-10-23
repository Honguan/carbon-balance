namespace CarbonFootprint.Infrastructure.Evidence;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Endpoint { get; set; } = "http://localhost:9000";

    public string AccessKey { get; set; } = "carbon_minio";

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "carbon-evidence";

    public string Region { get; set; } = "us-east-1";

    // Written once by deployment provisioning; the application identity may only HEAD it.
    public string ReadinessSentinelKey { get; set; } = "system/readiness-sentinel";

    public long MaximumFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public int MaximumConcurrentUploads { get; set; } = 2;

    public int UploadTimeoutSeconds { get; set; } = 60;

    public string QuarantineDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "carbon-evidence-quarantine");
}

public sealed class MalwareScannerOptions
{
    public const string SectionName = "MalwareScanner";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 3310;
}
