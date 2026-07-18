using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarbonFootprint.Domain.Modules.LegacyImport;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace CarbonFootprint.Infrastructure.LegacyImport;

public sealed record LegacyImportReport(
    Guid BatchId,
    string SourceFileSha256,
    int ParsedRows,
    int InvalidRows,
    int ConflictRows);

public sealed class LegacyFactorCsvImporter
{
    private const long MaximumSourceBytes = 50 * 1024 * 1024;
    private static readonly string[] RequiredColumns =
        ["name", "value", "denominator_unit", "source_version", "license_code"];
    private readonly CarbonFootprintDbContext _dbContext;

    public LegacyFactorCsvImporter(CarbonFootprintDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LegacyImportReport> ImportAsync(
        Guid organizationId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(sourcePath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("找不到 legacy CSV 來源。", sourcePath);
        }

        if (file.Length is <= 0 or > MaximumSourceBytes)
        {
            throw new InvalidOperationException($"Legacy CSV 大小必須介於 1 與 {MaximumSourceBytes} bytes。");
        }

        var sourceSha256 = await ComputeFileSha256Async(file.FullName, cancellationToken);
        if (await _dbContext.LegacyImportBatches.AnyAsync(
                item => item.SourceFileSha256 == sourceSha256 && item.EntityType == "EmissionFactor",
                cancellationToken))
        {
            throw new InvalidOperationException("相同 checksum 的係數來源已匯入 staging，禁止重複。");
        }

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var content = await File.ReadAllTextAsync(file.FullName, strictUtf8, cancellationToken);
        using var parser = new TextFieldParser(new StringReader(content))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        if (parser.EndOfData)
        {
            throw new InvalidOperationException("Legacy CSV 缺少 header。");
        }

        var headers = parser.ReadFields() ?? throw new InvalidOperationException("Legacy CSV header 無法解析。");
        var headerMap = headers
            .Select((header, index) => (Name: header.Trim(), Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var missingColumns = RequiredColumns.Where(column => !headerMap.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidOperationException($"Legacy CSV 缺少欄位：{string.Join(", ", missingColumns)}。");
        }

        var knownUnits = await _dbContext.Units.AsNoTracking()
            .Select(item => item.Code)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);
        var batchId = Guid.NewGuid();
        var batch = new LegacyImportBatchRecord
        {
            Id = batchId,
            OrganizationId = organizationId,
            SourceFileName = file.Name,
            SourceFileSha256 = sourceSha256,
            EntityType = "EmissionFactor",
            Status = "Completed",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.LegacyImportBatches.Add(batch);

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long rowNumber = 1;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            var fields = parser.ReadFields() ?? [];
            var payload = headers
                .Select((header, index) => new KeyValuePair<string, string?>(
                    header,
                    index < fields.Length ? fields[index] : null))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            var rawPayloadJson = JsonSerializer.Serialize(payload);
            var rowId = Guid.NewGuid();
            var errors = Validate(payload, headerMap, knownUnits);
            var conflictKey = errors.Count == 0
                ? $"{Value(payload, headerMap, "name")}|{Value(payload, headerMap, "source_version")}|{Value(payload, headerMap, "denominator_unit")}".Trim()
                : null;
            var isConflict = conflictKey is not null && !seenKeys.Add(conflictKey);
            var status = isConflict
                ? LegacyParseStatus.Conflict
                : errors.Count > 0
                    ? LegacyParseStatus.Invalid
                    : LegacyParseStatus.Parsed;
            _dbContext.LegacyStagingRows.Add(new LegacyStagingRowRecord
            {
                Id = rowId,
                OrganizationId = organizationId,
                ImportBatchId = batchId,
                SourceRowNumber = rowNumber,
                RawPayloadJson = rawPayloadJson,
                RawSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawPayloadJson))),
                ParseStatus = status.ToString(),
                ValidationErrorsJson = errors.Count == 0 ? null : JsonSerializer.Serialize(errors)
            });

            if (isConflict)
            {
                _dbContext.LegacyImportConflicts.Add(new LegacyImportConflictRecord
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    ImportBatchId = batchId,
                    StagingRowId = rowId,
                    ConflictKey = conflictKey!,
                    DetailsJson = JsonSerializer.Serialize(new { code = "DUPLICATE_IN_SOURCE" })
                });
                batch.ConflictRows++;
            }
            else if (errors.Count > 0)
            {
                batch.InvalidRows++;
            }
            else
            {
                batch.ParsedRows++;
            }
        }

        batch.CompletedAt = DateTimeOffset.UtcNow;
        _dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = null,
            OrganizationId = organizationId,
            Action = "legacy.factor-staging.imported",
            ResourceType = "LegacyImportBatch",
            ResourceId = batchId,
            BeforeHash = null,
            AfterHash = sourceSha256,
            CorrelationId = batchId.ToString("N"),
            MetadataJson = JsonSerializer.Serialize(new
            {
                batch.ParsedRows,
                batch.InvalidRows,
                batch.ConflictRows
            })
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new LegacyImportReport(
            batchId,
            sourceSha256,
            batch.ParsedRows,
            batch.InvalidRows,
            batch.ConflictRows);
    }

    private static List<string> Validate(
        IReadOnlyDictionary<string, string?> payload,
        IReadOnlyDictionary<string, int> headerMap,
        IReadOnlySet<string> knownUnits)
    {
        var errors = RequiredColumns
            .Where(column => string.IsNullOrWhiteSpace(Value(payload, headerMap, column)))
            .Select(column => $"REQUIRED:{column}")
            .ToList();
        if (!decimal.TryParse(
                Value(payload, headerMap, "value"),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var factorValue)
            || factorValue < 0m)
        {
            errors.Add("INVALID:value");
        }

        var unit = Value(payload, headerMap, "denominator_unit");
        if (!string.IsNullOrWhiteSpace(unit) && !knownUnits.Contains(unit))
        {
            errors.Add("UNKNOWN_UNIT:denominator_unit");
        }

        return errors;
    }

    private static string? Value(
        IReadOnlyDictionary<string, string?> payload,
        IReadOnlyDictionary<string, int> headerMap,
        string name)
    {
        var header = headerMap.Keys.Single(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return payload.GetValueOrDefault(header)?.Trim();
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
