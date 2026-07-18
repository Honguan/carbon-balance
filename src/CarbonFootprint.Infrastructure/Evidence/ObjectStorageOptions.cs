namespace CarbonFootprint.Infrastructure.Evidence;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Endpoint { get; set; } = "http://localhost:9000";

    public string AccessKey { get; set; } = "carbon_minio";

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "carbon-evidence";

    public long MaximumFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}

public sealed class MalwareScannerOptions
{
    public const string SectionName = "MalwareScanner";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 3310;
}
