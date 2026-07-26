using System.Text;
using System.Text.Json;
using CarbonFootprint.Application.Factors;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Web.Services;

public sealed class MoenvFactorSourceOptions
{
    public const string SectionName = "ExternalFactorSources:Moenv";

    public string ApiKey { get; set; } = string.Empty;
}

public sealed record MoenvFactorDownload(
    IReadOnlyList<MoenvFactorRecord> Records,
    int SkippedCount);

public sealed class MoenvFactorClient
{
    public const string DatasetReference = "https://data.moenv.gov.tw/dataset/detail/CFP_P_02";

    private const string ApiEndpoint = "https://data.moenv.gov.tw/api/v2/CFP_P_02";
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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<MoenvFactorDownload> DownloadAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("尚未設定環境部資料開放平臺 API Key。");
        }

        var records = new List<MoenvFactorRecord>();
        var skippedCount = 0;
        var downloadCompleted = false;
        for (var page = 0; page < MaximumPages; page++)
        {
            var offset = page * PageSize;
            var uri = $"{ApiEndpoint}?api_key={Uri.EscapeDataString(_options.ApiKey.Trim())}&limit={PageSize}&offset={offset}&sort=ImportDate%20desc&format=json";
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
}
