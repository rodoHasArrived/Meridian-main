using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Storage.Archival;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Meridian.Storage.Services;

public interface IQualityTrendStore
{
    Task AppendAsync(QualityTrendPoint point, CancellationToken ct = default);
    Task<IReadOnlyList<QualityTrendPoint>> GetPointsAsync(string symbol, DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken ct = default);
}

/// <summary>
/// Append-only JSONL trend store keyed by symbol/date/provider.
/// </summary>
public sealed partial class FileQualityTrendStore : IQualityTrendStore
{
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(25);
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string LockPath => _filePath + ".lock";
    private string ChainHeadPath => _filePath + ".head";
    private string PendingAppendPath => _filePath + ".pending";

    public FileQualityTrendStore(StorageOptions options)
    {
        var qualityDir = Path.Combine(options.RootPath, "quality");
        Directory.CreateDirectory(qualityDir);
        _filePath = Path.Combine(qualityDir, "trend-points.jsonl");
    }

    public async Task AppendAsync(QualityTrendPoint point, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateNewPoint(point);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(ct).ConfigureAwait(false);
            var records = await LoadAndValidateRecordsAsync(ct).ConfigureAwait(false);
            var retained = records.FirstOrDefault(record =>
                string.Equals(record.Point.EvaluationId, point.EvaluationId, StringComparison.Ordinal));
            if (retained is not null)
            {
                if (!string.Equals(
                        SerializePoint(retained.Point),
                        SerializePoint(point),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Quality evaluation '{point.EvaluationId}' is already retained with different evidence.");
                }

                return;
            }

            await AppendRecordAsync(point, records, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<QualityTrendPoint>> GetPointsAsync(string symbol, DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(ct).ConfigureAwait(false);
            var records = await LoadAndValidateRecordsAsync(ct).ConfigureAwait(false);
            return records
                .Select(static record => record.Point)
                .Where(point => string.Equals(point.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Where(point => point.ScoredAt >= fromInclusive && point.ScoredAt <= toInclusive)
                .OrderBy(static point => point.ScoredAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileStream> AcquireProcessLockAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    LockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(LockRetryDelay, ct).ConfigureAwait(false);
            }
        }
    }

    private static void ValidateNewPoint(QualityTrendPoint point)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(point.Symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(point.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(point.EvaluationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(point.RulesetVersion);
        if (point.ScoredAt == default || point.Date != DateOnly.FromDateTime(point.ScoredAt.UtcDateTime))
            throw new ArgumentException("Quality history date must match the UTC scoring date.", nameof(point));
        if (!double.IsFinite(point.OverallScore) ||
            point.OverallScore is < 0 or > 1 ||
            point.DimensionScores is null ||
            point.DimensionScores.Count == 0 ||
            point.DimensionScores.Any(static pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                !double.IsFinite(pair.Value) ||
                pair.Value is < 0 or > 1))
        {
            throw new ArgumentException("Quality scores must be named values between zero and one.", nameof(point));
        }
        if (point.InputHashSha256 is not { Length: 64 } ||
            !point.InputHashSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Quality history requires the retained input SHA-256 hash.", nameof(point));
        }
        if (point.ResultHashSha256 is not { Length: 64 } ||
            !point.ResultHashSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Quality history requires the canonical result SHA-256 hash.", nameof(point));
        }

        var expectedResultHash = QualityTrendResultHash.Compute(
            point.EvaluationId,
            point.RulesetVersion,
            point.InputHashSha256,
            point.Symbol,
            point.Date,
            point.Provider,
            point.ScoredAt,
            point.OverallScore,
            point.DimensionScores);
        if (!string.Equals(point.ResultHashSha256, expectedResultHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Quality history result hash does not match its retained scores and identity.",
                nameof(point));
        }
        if (point.Outcome is null)
            throw new ArgumentException("Quality history requires a verified terminal outcome.", nameof(point));

        VerifiedOperationOutcomeValidator.ValidateAndThrow(point.Outcome);
        if (!string.Equals(point.EvaluationId, point.Outcome.OperationId, StringComparison.Ordinal) ||
            !string.Equals(
                point.InputHashSha256,
                point.Outcome.InputHashSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Quality history identity and input hash must match its verified terminal outcome.",
                nameof(point));
        }
        if (!point.Outcome.Evidence.Any(evidence =>
                string.Equals(evidence.Kind, "quality-result", StringComparison.Ordinal) &&
                string.Equals(evidence.ContentHashSha256, point.ResultHashSha256, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Quality history result hash must be retained as verified outcome evidence.",
                nameof(point));
        }
    }
}

public sealed record QualityTrendPoint(
    string Symbol,
    DateOnly Date,
    string Provider,
    DateTimeOffset ScoredAt,
    double OverallScore,
    IReadOnlyDictionary<string, double> DimensionScores)
{
    public string? EvaluationId { get; init; }
    public string? InputHashSha256 { get; init; }
    public string? ResultHashSha256 { get; init; }
    public string? RulesetVersion { get; init; }
    public VerifiedOperationOutcome? Outcome { get; init; }
}

public static class QualityTrendResultHash
{
    public static string Compute(
        string evaluationId,
        string rulesetVersion,
        string inputHashSha256,
        string symbol,
        DateOnly date,
        string provider,
        DateTimeOffset scoredAt,
        double overallScore,
        IReadOnlyDictionary<string, double> dimensionScores)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesetVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputHashSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(dimensionScores);
        if (inputHashSha256.Length != 64 || !inputHashSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Input hash must be a SHA-256 hex digest.", nameof(inputHashSha256));

        var canonical = new StringBuilder()
            .Append("meridian.quality-trend-result.v2").Append('\n')
            .Append(evaluationId.Trim()).Append('\n')
            .Append(rulesetVersion.Trim()).Append('\n')
            .Append(inputHashSha256.Trim().ToLowerInvariant()).Append('\n')
            .Append(symbol.Trim().ToUpperInvariant()).Append('\n')
            .Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n')
            .Append(provider.Trim().ToUpperInvariant()).Append('\n')
            .Append(scoredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(overallScore.ToString("R", CultureInfo.InvariantCulture));
        foreach (var (name, value) in dimensionScores
                     .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            canonical
                .Append('\n')
                .Append(name.Trim().ToUpperInvariant())
                .Append('=')
                .Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        return Sha256Digest.ComputeUtf8(canonical.ToString());
    }
}
