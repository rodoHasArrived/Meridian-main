using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Services;

public sealed partial class FileQualityTrendStore
{
    private const int CurrentSchemaVersion = 2;
    private const string ChainHeadSchema = "meridian.quality-trend-head.v2";

    private async Task<IReadOnlyList<QualityTrendChainRecord>> LoadAndValidateRecordsAsync(
        CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            if (File.Exists(ChainHeadPath))
            {
                throw new InvalidDataException(
                    "Quality history is missing while its chain head remains. The history cannot be trusted.");
            }

            TryDeletePendingAppend();

            return [];
        }

        var parsed = await ParseRecordsAsync(ct).ConfigureAwait(false);
        if (parsed.LegacyPoints is not null)
        {
            return await MigrateLegacyPointsAsync(parsed.LegacyPoints, ct).ConfigureAwait(false);
        }

        var records = parsed.Records!;
        ValidateRecordChain(records);
        await RecoverPendingAppendAsync(records, ct).ConfigureAwait(false);
        await ValidateChainHeadAsync(records, ct).ConfigureAwait(false);
        return records;
    }

    private async Task AppendRecordAsync(
        QualityTrendPoint point,
        IReadOnlyList<QualityTrendChainRecord> records,
        CancellationToken ct)
    {
        var sequence = records.Count == 0 ? 1 : checked(records[^1].Sequence + 1);
        var previousHash = records.Count == 0 ? null : records[^1].RecordHashSha256;
        var record = new QualityTrendChainRecord
        {
            SchemaVersion = CurrentSchemaVersion,
            Sequence = sequence,
            PreviousRecordHashSha256 = previousHash,
            Point = point,
            RecordHashSha256 = ComputeRecordHash(sequence, previousHash, point)
        };
        var pendingHead = CreateChainHead(record);
        var pendingJson = JsonSerializer.Serialize(
            pendingHead,
            QualityTrendStoreJsonContext.Default.QualityTrendChainHead);
        await AtomicFileWriter.WriteAsync(PendingAppendPath, pendingJson, ct).ConfigureAwait(false);

        Exception? appendFailure = null;
        try
        {
            var json = JsonSerializer.Serialize(
                record,
                QualityTrendStoreJsonContext.Default.QualityTrendChainRecord);
            await AtomicFileWriter.AppendLinesAsync(_filePath, [json], ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            appendFailure = exception;
        }

        var refreshed = (await ParseRecordsAsync(CancellationToken.None).ConfigureAwait(false)).Records;
        if (refreshed is null)
        {
            throw new InvalidDataException(
                "Quality history changed schema while an append was being committed.",
                appendFailure);
        }

        ValidateRecordChain(refreshed);
        var committed = refreshed.Count > 0 &&
                        refreshed[^1].Sequence == record.Sequence &&
                        string.Equals(
                            refreshed[^1].RecordHashSha256,
                            record.RecordHashSha256,
                            StringComparison.OrdinalIgnoreCase);
        if (!committed)
        {
            TryDeletePendingAppend();
            if (appendFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(appendFailure).Throw();

            throw new IOException("Quality history append did not retain the expected chain record.");
        }

        await PersistChainHeadAsync(pendingHead, CancellationToken.None).ConfigureAwait(false);
        TryDeletePendingAppend();
    }

    private async Task<ParsedQualityHistory> ParseRecordsAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new ParsedQualityHistory([], null);

        var lines = await File.ReadAllLinesAsync(_filePath, ct).ConfigureAwait(false);
        var records = new List<QualityTrendChainRecord>();
        var legacyPoints = new List<QualityTrendPoint>();
        bool? isLegacy = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var document = JsonDocument.Parse(line);
                var currentIsLegacy = !document.RootElement.TryGetProperty("schemaVersion", out _);
                if (isLegacy is not null && isLegacy != currentIsLegacy)
                {
                    throw new InvalidDataException(
                        $"Quality history line {index + 1} mixes legacy and chained schemas. The history cannot be trusted.");
                }

                isLegacy = currentIsLegacy;
                if (currentIsLegacy)
                {
                    var point = JsonSerializer.Deserialize(
                        line,
                        QualityTrendStoreJsonContext.Default.QualityTrendPoint)
                        ?? throw new InvalidDataException(
                            $"Quality history line {index + 1} deserialized to null. The history cannot be trusted.");
                    ValidatePointFromHistory(point, index);
                    legacyPoints.Add(point);
                }
                else
                {
                    var record = JsonSerializer.Deserialize(
                        line,
                        QualityTrendStoreJsonContext.Default.QualityTrendChainRecord)
                        ?? throw new InvalidDataException(
                            $"Quality history line {index + 1} deserialized to null. The history cannot be trusted.");
                    ValidatePointFromHistory(record.Point, index);
                    records.Add(record);
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Quality history line {index + 1} is malformed. The history cannot be trusted until it is repaired from retained evidence.",
                    exception);
            }
        }

        return isLegacy == true
            ? new ParsedQualityHistory(null, legacyPoints)
            : new ParsedQualityHistory(records, null);
    }

    private static void ValidatePointFromHistory(QualityTrendPoint point, int zeroBasedLine)
    {
        try
        {
            ValidateNewPoint(point);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"Quality history line {zeroBasedLine + 1} violates the verified-evidence contract. " +
                "The history cannot be trusted until it is repaired from retained evidence.",
                exception);
        }
    }

    private async Task<IReadOnlyList<QualityTrendChainRecord>> MigrateLegacyPointsAsync(
        IReadOnlyList<QualityTrendPoint> points,
        CancellationToken ct)
    {
        if (File.Exists(ChainHeadPath) || File.Exists(PendingAppendPath))
        {
            throw new InvalidDataException(
                "Legacy quality history has chained sidecars and cannot be migrated safely.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<QualityTrendChainRecord>(points.Count);
        string? previousHash = null;
        foreach (var point in points)
        {
            if (!seen.Add(point.EvaluationId!))
            {
                throw new InvalidDataException(
                    $"Legacy quality history repeats evaluation '{point.EvaluationId}' and cannot be migrated safely.");
            }

            var sequence = records.Count + 1L;
            var record = new QualityTrendChainRecord
            {
                SchemaVersion = CurrentSchemaVersion,
                Sequence = sequence,
                PreviousRecordHashSha256 = previousHash,
                Point = point,
                RecordHashSha256 = ComputeRecordHash(sequence, previousHash, point)
            };
            records.Add(record);
            previousHash = record.RecordHashSha256;
        }

        var content = string.Join(
            Environment.NewLine,
            records.Select(record => JsonSerializer.Serialize(
                record,
                QualityTrendStoreJsonContext.Default.QualityTrendChainRecord)));
        if (content.Length > 0)
            content += Environment.NewLine;
        await AtomicFileWriter.ReplaceAsync(
            _filePath,
            content,
            keepBackup: true,
            ct: ct).ConfigureAwait(false);
        if (records.Count > 0)
            await PersistChainHeadAsync(CreateChainHead(records[^1]), CancellationToken.None).ConfigureAwait(false);
        return records;
    }

    private async Task RecoverPendingAppendAsync(
        IReadOnlyList<QualityTrendChainRecord> records,
        CancellationToken ct)
    {
        if (!File.Exists(PendingAppendPath))
            return;

        var pending = await ReadChainHeadAsync(PendingAppendPath, ct).ConfigureAwait(false);
        var tailMatchesPending = records.Count > 0 &&
                                 records[^1].Sequence == pending.Sequence &&
                                 string.Equals(
                                     records[^1].RecordHashSha256,
                                     pending.RecordHashSha256,
                                     StringComparison.OrdinalIgnoreCase);
        if (tailMatchesPending)
        {
            await PersistChainHeadAsync(pending, CancellationToken.None).ConfigureAwait(false);
            TryDeletePendingAppend();
            return;
        }

        var retainedHead = File.Exists(ChainHeadPath)
            ? await ReadChainHeadAsync(ChainHeadPath, ct).ConfigureAwait(false)
            : null;
        var appendDidNotCommit =
            pending.Sequence == (records.Count == 0 ? 1 : records[^1].Sequence + 1) &&
            ((records.Count == 0 && retainedHead is null) ||
             (records.Count > 0 && retainedHead is not null &&
              retainedHead.Sequence == records[^1].Sequence &&
              string.Equals(
                  retainedHead.RecordHashSha256,
                  records[^1].RecordHashSha256,
                  StringComparison.OrdinalIgnoreCase)));
        if (!appendDidNotCommit)
        {
            throw new InvalidDataException(
                "Quality history has an unresolved pending append that does not match its retained chain.");
        }

        TryDeletePendingAppend();
    }

    private async Task ValidateChainHeadAsync(
        IReadOnlyList<QualityTrendChainRecord> records,
        CancellationToken ct)
    {
        if (records.Count == 0)
        {
            if (File.Exists(ChainHeadPath))
                throw new InvalidDataException("Empty quality history retains a non-empty chain head.");
            return;
        }

        if (!File.Exists(ChainHeadPath))
            throw new InvalidDataException("Quality history is missing its durable chain head.");

        var head = await ReadChainHeadAsync(ChainHeadPath, ct).ConfigureAwait(false);
        if (head.Sequence != records[^1].Sequence ||
            !string.Equals(head.RecordHashSha256, records[^1].RecordHashSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Quality history chain head does not match the retained tail; deletion or reordering was detected.");
        }
    }

    private static void ValidateRecordChain(IReadOnlyList<QualityTrendChainRecord> records)
    {
        var evaluationIds = new HashSet<string>(StringComparer.Ordinal);
        string? previousHash = null;
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var expectedSequence = index + 1L;
            if (record.SchemaVersion != CurrentSchemaVersion ||
                record.Sequence != expectedSequence ||
                !string.Equals(record.PreviousRecordHashSha256, previousHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    record.RecordHashSha256,
                    ComputeRecordHash(record.Sequence, record.PreviousRecordHashSha256, record.Point),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Quality history line {index + 1} breaks the sequence or predecessor hash chain.");
            }

            if (!evaluationIds.Add(record.Point.EvaluationId!))
            {
                throw new InvalidDataException(
                    $"Quality history repeats evaluation '{record.Point.EvaluationId}'.");
            }

            previousHash = record.RecordHashSha256;
        }
    }

    private async Task PersistChainHeadAsync(QualityTrendChainHead head, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(
            head,
            QualityTrendStoreJsonContext.Default.QualityTrendChainHead);
        await AtomicFileWriter.WriteAsync(ChainHeadPath, json, ct).ConfigureAwait(false);
    }

    private static async Task<QualityTrendChainHead> ReadChainHeadAsync(
        string path,
        CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var head = JsonSerializer.Deserialize(
                json,
                QualityTrendStoreJsonContext.Default.QualityTrendChainHead)
                ?? throw new InvalidDataException("Quality history chain head deserialized to null.");
            var expectedHash = ComputeHeadHash(head.Sequence, head.RecordHashSha256);
            if (!string.Equals(head.Schema, ChainHeadSchema, StringComparison.Ordinal) ||
                !string.Equals(head.HeadHashSha256, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Quality history chain head failed integrity validation.");
            }

            return head;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Quality history chain head is malformed.", exception);
        }
    }

    private static QualityTrendChainHead CreateChainHead(QualityTrendChainRecord record) => new()
    {
        Schema = ChainHeadSchema,
        Sequence = record.Sequence,
        RecordHashSha256 = record.RecordHashSha256,
        HeadHashSha256 = ComputeHeadHash(record.Sequence, record.RecordHashSha256)
    };

    private static string ComputeRecordHash(
        long sequence,
        string? previousHash,
        QualityTrendPoint point)
    {
        var pointJson = SerializePoint(point);
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"meridian.quality-trend-record.v2\n{sequence}\n{previousHash ?? string.Empty}\n{pointJson.Length}:{pointJson}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ComputeHeadHash(long sequence, string recordHash)
    {
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{ChainHeadSchema}\n{sequence}\n{recordHash.ToLowerInvariant()}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string SerializePoint(QualityTrendPoint point) =>
        JsonSerializer.Serialize(point, QualityTrendStoreJsonContext.Default.QualityTrendPoint);

    private void TryDeletePendingAppend()
    {
        try
        {
            File.Delete(PendingAppendPath);
        }
        catch (IOException)
        {
            // The pending marker is intentionally recoverable. A later load will validate the
            // retained tail and finish or discard the interrupted append deterministically.
        }
        catch (UnauthorizedAccessException)
        {
            // Same recovery behavior as an I/O failure; never turn a committed record into an
            // ambiguous caller-visible append failure solely because marker cleanup was denied.
        }
    }

    private sealed record ParsedQualityHistory(
        IReadOnlyList<QualityTrendChainRecord>? Records,
        IReadOnlyList<QualityTrendPoint>? LegacyPoints);
}

internal sealed record QualityTrendChainRecord
{
    public required int SchemaVersion { get; init; }
    public required long Sequence { get; init; }
    public string? PreviousRecordHashSha256 { get; init; }
    public required string RecordHashSha256 { get; init; }
    public required QualityTrendPoint Point { get; init; }
}

internal sealed record QualityTrendChainHead
{
    public required string Schema { get; init; }
    public required long Sequence { get; init; }
    public required string RecordHashSha256 { get; init; }
    public required string HeadHashSha256 { get; init; }
}
