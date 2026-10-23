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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.UploadTimeoutSeconds));
        var token = deadline.Token;
        var quarantinePath = Path.Combine(_options.QuarantineDirectory, $"{Guid.NewGuid():N}.quarantine");
        try
        {
            Directory.CreateDirectory(_options.QuarantineDirectory);
            var (sha256, size) = await SpoolAndHashAsync(content, quarantinePath, token);

            await using var quarantined = new FileStream(
                quarantinePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await _malwareScanner.ScanAsync(quarantined, token);

            var resolvedContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
            var evidenceId = Guid.NewGuid();
            var objectKey = $"organizations/{organizationId:N}/evidence/{evidenceId:N}";
            quarantined.Position = 0;
            using var minioClient = CreateMinioClient();
            await minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(_options.Bucket)
                    .WithObject(objectKey)
                    .WithStreamData(quarantined)
                    .WithObjectSize(size)
                    .WithContentType(resolvedContentType),
                token);

            return new StoredEvidence(
                evidenceId,
                objectKey,
                Path.GetFileName(originalFileName),
                resolvedContentType,
                size,
                sha256,
                MalwareScanStatus.Clean);
        }
        catch (EvidenceUploadException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EvidenceUploadException("Evidence 處理逾時，請稍後重試。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new EvidenceUploadException("Evidence 儲存服務暫時無法使用，請稍後重試。", exception);
        }
        finally
        {
            try
            {
                File.Delete(quarantinePath);
            }
            catch (IOException)
            {
                // Residual quarantine files are never served; an offline sweep removes them.
            }
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        // The SDK's stat/get calls issue a bucket-level HEAD first, which the least-privilege identity cannot
        // perform; a presigned one-byte ranged GET only needs Read on the provisioned sentinel object.
        using var client = CreateMinioClient();
        var url = await client.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(_options.ReadinessSentinelKey)
                .WithExpiry(60));
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<(string Sha256, long Size)> SpoolAndHashAsync(Stream content, string path, CancellationToken cancellationToken)
    {
        await using var spool = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long size = 0;
        int bytesRead;
        while ((bytesRead = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            size += bytesRead;
            if (size > _options.MaximumFileSizeBytes)
            {
                throw new EvidenceUploadException($"Evidence 檔案大小不得超過 {_options.MaximumFileSizeBytes} bytes。");
            }

            hash.AppendData(buffer, 0, bytesRead);
            await spool.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (size <= 0)
        {
            throw new EvidenceUploadException("Evidence 檔案不可為空白。");
        }

        return (Convert.ToHexStringLower(hash.GetHashAndReset()), size);
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
            .WithRegion(_options.Region)
            .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps)
            .Build();
    }
}
