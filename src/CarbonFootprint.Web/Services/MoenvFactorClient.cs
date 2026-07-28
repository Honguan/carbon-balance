using System.Text;
using System.Text.Json;
using CarbonFootprint.Application.Factors;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Web.Services;

public sealed class MoenvFactorSourceOptions
{
    public const string SectionName = "ExternalFactorSources:Moenv";

    public string ApiKey { get; set; } = string.Empty;

    public bool ImportOnDeployment { get; set; } = true;
}

public sealed record MoenvFactorDownload(
    IReadOnlyList<MoenvFactorRecord> Records,
    int SkippedCount);

public interface IMoenvFactorSource
{
    Task<MoenvFactorDownload> DownloadAsync(CancellationToken cancellationToken);
}

public sealed class MoenvFactorClient : IMoenvFactorSource
{
    public const string DatasetReference = "https://data.moenv.gov.tw/dataset/detail/CFP_P_02";

    private const string ApiEndpoint = "https://data.moenv.gov.tw/api/v2/CFP_P_02";
    private const string MetadataEndpoint = "https://data.gov.tw/api/v2/rest/dataset/28176";
    private const int PageSize = 1000;
    private const int MaximumPages = 20;
    private sealed record FactorIdentity(string Name, string UnitCode, string DepartmentName);

    private readonly HttpClient _httpClient;
    private readonly MoenvFactorSourceOptions _options;

    public MoenvFactorClient(HttpClient httpClient, IOptions<MoenvFactorSourceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<MoenvFactorDownload> DownloadAsync(CancellationToken cancellationToken)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        var records = new List<MoenvFactorRecord>();
        var skippedCount = 0;
        var downloadCompleted = false;
        for (var page = 0; page < MaximumPages; page++)
        {
            var offset = page * PageSize;
            var uri = $"{ApiEndpoint}?api_key={Uri.EscapeDataString(apiKey)}&limit={PageSize}&offset={offset}&sort=ImportDate%20desc&format=json";
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            MoenvFactorDataset dataset;
            try
            {
                dataset = MoenvFactorDatasetParser.Parse(Encoding.UTF8.GetString(bytes));
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("環境部係數資料格式無法解析。", exception);
            }
            records.AddRange(dataset.Records);
            skippedCount += dataset.SkippedCount;
            if (dataset.SourceRecordCount < PageSize)
            {
                downloadCompleted = true;
                break;
            }
        }

        if (!downloadCompleted)
        {
            throw new InvalidOperationException("環境部係數資料超過單次同步上限，未建立任何草稿；請由系統管理者確認資料範圍。");
        }

        var latestRecords = records
            .GroupBy(
                item => new FactorIdentity(
                    item.Name,
                    item.DenominatorUnitCode,
                    string.IsNullOrWhiteSpace(item.DepartmentName)
                        ? "環境部氣候變遷署"
                        : item.DepartmentName.Trim()),
                EqualityComparer<FactorIdentity>.Default)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.DenominatorUnitCode, StringComparer.Ordinal)
            .ToArray();
        return new MoenvFactorDownload(
            latestRecords,
            skippedCount);
    }

    private async Task<string> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey.Trim();
        }

        using var response = await _httpClient.GetAsync(
            MetadataEndpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var result = FindProperty(document.RootElement, "result");
            var distributions = result.HasValue ? FindProperty(result.Value, "distribution") : null;
            if (!distributions.HasValue || distributions.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("政府資料開放平臺未提供環境部係數下載資訊。");
            }

            foreach (var distribution in distributions.Value.EnumerateArray())
            {
                var format = ReadString(distribution, "resourceFormat");
                if (!string.Equals(format, "JSON", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var downloadUrl = ReadString(distribution, "resourceDownloadUrl");
                if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var resourceUri)
                    || !string.Equals(resourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(resourceUri.Host, "data.moenv.gov.tw", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(resourceUri.AbsolutePath, "/api/v2/cfp_p_02", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("政府資料開放平臺提供的環境部係數下載網址無效。");
                }

                var apiKey = ReadQueryParameter(resourceUri.Query, "api_key");
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    return apiKey;
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("政府資料開放平臺詮釋資料格式無法解析。", exception);
        }

        throw new InvalidOperationException("政府資料開放平臺未提供環境部係數 JSON 公開下載網址。");
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string ReadString(JsonElement element, string name)
    {
        var value = FindProperty(element, name);
        return value?.ValueKind == JsonValueKind.String
            ? value.Value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ReadQueryParameter(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return separator >= 0 ? Uri.UnescapeDataString(pair[(separator + 1)..]) : string.Empty;
        }

        return string.Empty;
    }
}
