using System.Security.Cryptography;
using CarbonFootprint.Domain.Modules.Evidence;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace CarbonFootprint.Infrastructure.Evidence;

public sealed record StoredEvidence(
    Guid Id,
    string ObjectKey,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    MalwareScanStatus ScanStatus);

public sealed class EvidenceStorageService
{
    private readonly ObjectStorageOptions _options;
    private readonly ClamAvMalwareScanner _malwareScanner;

    public EvidenceStorageService(
        IOptions<ObjectStorageOptions> options,
        ClamAvMalwareScanner malwareScanner)
    {
        _options = options.Value;
        _malwareScanner = malwareScanner;
    }

    public async Task<StoredEvidence> StoreAsync(
        Guid organizationId,
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        await using var buffered = new MemoryStream();
        var copyBuffer = new byte[64 * 1024];
        int bytesRead;
        while ((bytesRead = await content.ReadAsync(copyBuffer, cancellationToken)) > 0)
        {
            if (buffered.Length + bytesRead > _options.MaximumFileSizeBytes)
            {
                throw new InvalidOperationException($"Evidence 檔案大小不得超過 {_options.MaximumFileSizeBytes} bytes。");
            }

            await buffered.WriteAsync(copyBuffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (buffered.Length <= 0)
        {
            throw new InvalidOperationException("Evidence 檔案不可為空白。");
        }

        buffered.Position = 0;
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffered, cancellationToken));
        buffered.Position = 0;
        await _malwareScanner.ScanAsync(buffered, cancellationToken);

        using var minioClient = CreateMinioClient();
        var bucketExists = await minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_options.Bucket),
            cancellationToken);
        if (!bucketExists)
        {
            await minioClient.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_options.Bucket),
                cancellationToken);
        }

        var evidenceId = Guid.NewGuid();
        var objectKey = $"organizations/{organizationId:N}/evidence/{evidenceId:N}";
        buffered.Position = 0;
        await minioClient.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectKey)
                .WithStreamData(buffered)
                .WithObjectSize(buffered.Length)
                .WithContentType(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType),
            cancellationToken);

        return new StoredEvidence(
            evidenceId,
            objectKey,
            Path.GetFileName(originalFileName),
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            buffered.Length,
            sha256,
            MalwareScanStatus.Clean);
    }

    private IMinioClient CreateMinioClient()
    {
        if (string.IsNullOrWhiteSpace(_options.AccessKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("ObjectStorage access key 與 secret key 尚未設定。");
        }

        var endpoint = new Uri(_options.Endpoint, UriKind.Absolute);
        return new MinioClient()
            .WithEndpoint(endpoint.Host, endpoint.Port)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps)
            .Build();
    }
}
