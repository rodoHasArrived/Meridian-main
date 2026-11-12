using System.Text.Json;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Serialization;

namespace Meridian.Ui.Shared.Services;

public sealed record PrivateCapitalLifecycleRead(
    IReadOnlyList<CapitalCommitmentDto> Commitments,
    IReadOnlyList<CapitalActivityDto> Activities,
    IReadOnlyList<string> Warnings);

/// <summary>A source supplies retained balances; the workstation does not manufacture or approve them.</summary>
public interface IPrivateCapitalLifecycleSource
{
    Task<PrivateCapitalLifecycleRead> ReadAsync(string familyOfficeId, IReadOnlySet<string> entityIds,
        DateOnly asOf, DateTimeOffset knownAt, CancellationToken ct = default);
}

/// <summary>
/// Reads a versioned retained source cut under the workstation data directory. Provisioners replace
/// the document atomically and retain prior snapshot revisions. No writes or approval authority.
/// </summary>
public sealed class FilePrivateCapitalLifecycleSource(string dataDirectory) : IPrivateCapitalLifecycleSource
{
    private readonly string _path = Path.Combine(Path.GetFullPath(dataDirectory), "family-office", "private-capital-lifecycle.json");
    private const long MaxBytes = 16 * 1024 * 1024;

    public async Task<PrivateCapitalLifecycleRead> ReadAsync(string familyOfficeId, IReadOnlySet<string> entityIds,
        DateOnly asOf, DateTimeOffset knownAt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
            return new([], [], ["No retained private-capital lifecycle source is configured; balances are unavailable."]);
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaxBytes)
                return Unavailable("Retained private-capital source exceeds the supported size.");
            var document = await JsonSerializer.DeserializeAsync(stream,
                FamilyOfficeJsonContext.Default.PrivateCapitalLifecycleDocumentDto, ct).ConfigureAwait(false);
            if (document is null || document.SchemaVersion != 1 || document.Snapshots is null || document.Activities is null)
                return Unavailable("Retained private-capital source has an unsupported or malformed schema.");
            return Project(document, familyOfficeId, entityIds, asOf, knownAt);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unavailable("Retained private-capital source could not be read or validated; no balances are published.");
        }
    }

    public static PrivateCapitalLifecycleRead Project(PrivateCapitalLifecycleDocumentDto document,
        string familyOfficeId, IReadOnlySet<string> entityIds, DateOnly asOf, DateTimeOffset knownAt)
    {
        if (document.SchemaVersion != 1 || document.Snapshots is null || document.Activities is null
            || document.Snapshots.Any(static row => row is null) || document.Activities.Any(static row => row is null))
            return Unavailable("Retained private-capital source schema is unsupported.");
        var source = document.Snapshots.Where(row => row is not null && row.FamilyOfficeId == familyOfficeId).ToArray();
        if (source.Any(row => !Valid(row, entityIds)))
            return Unavailable("Retained private-capital source contains invalid identity, dates, amounts, or source evidence.");
        if (source.GroupBy(row => row.SnapshotId, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || source.GroupBy(row => (row.Commitment.CommitmentId, row.Revision)).Any(group => group.Count() != 1))
            return Unavailable("Retained private-capital source has duplicate snapshot or revision identities.");
        // A changed entity or currency requires a different commitment identity, never a silent rebind.
        if (source.GroupBy(row => row.Commitment.CommitmentId, StringComparer.Ordinal)
            .Any(group => group.Select(row => (row.Commitment.EntityId, row.Commitment.Currency)).Distinct().Count() != 1))
            return Unavailable("Retained private-capital commitment identity changes entity or currency across revisions.");

        var eligible = source.Where(row => row.RecordedAtUtc <= knownAt && row.Commitment.AsOfDate <= asOf
            && (row.Commitment.ValuationDate is null || row.Commitment.ValuationDate <= asOf));
        var commitments = eligible.GroupBy(row => row.Commitment.CommitmentId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(row => row.Commitment.AsOfDate)
                .ThenByDescending(row => row.Revision).ThenByDescending(row => row.RecordedAtUtc).First())
            .Select(row => row.Commitment with { BalancesAvailable = true, SnapshotId = row.SnapshotId,
                SourceRevision = row.Revision, RecordedAtUtc = row.RecordedAtUtc,
                SourceContentSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(row, FamilyOfficeJsonContext.Default.PrivateCapitalSnapshotDto))),
                ExactBalances = new(Exact(row.Commitment.CommitmentAmount), Exact(row.Commitment.CalledAmount),
                    Exact(row.Commitment.UnfundedAmount), Exact(row.Commitment.DistributedAmount), Exact(row.Commitment.CurrentNav)) })
            .OrderBy(row => row.VehicleName, StringComparer.Ordinal).ToArray();
        var identities = commitments.ToDictionary(row => row.CommitmentId, StringComparer.Ordinal);
        var scopedActivity = document.Activities.Where(row => row is not null && row.FamilyOfficeId == familyOfficeId).ToArray();
        if (scopedActivity.Any(row => row.Activity is null || string.IsNullOrWhiteSpace(row.Activity.ActivityId)
            || string.IsNullOrWhiteSpace(row.Activity.SourceDocumentId) || string.IsNullOrWhiteSpace(row.Activity.SourceSystem)
            || string.IsNullOrWhiteSpace(row.Activity.Status) || row.Activity.AsOfDate == default || row.Activity.EffectiveDate == default
            || !entityIds.Contains(row.Activity.EntityId) || row.Activity.Amount < 0m || row.RecordedAtUtc == default)
            || scopedActivity.GroupBy(row => row.Activity.ActivityId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            return Unavailable("Retained private-capital activity has invalid or duplicate source identities.");
        var activities = scopedActivity.Where(row => row.RecordedAtUtc <= knownAt && row.Activity.EffectiveDate <= asOf
            && row.Activity.AsOfDate <= asOf && identities.TryGetValue(row.Activity.CommitmentId, out var commitment)
            && row.Activity.EntityId == commitment.EntityId && row.Activity.Currency == commitment.Currency)
            .Select(row => row.Activity).OrderByDescending(row => row.EffectiveDate).ThenBy(row => row.ActivityId, StringComparer.Ordinal).ToArray();
        var warnings = new List<string>();
        if (source.Length != commitments.Length)
            warnings.Add("Private-capital source uses the latest eligible retained revision per commitment; future or superseded snapshots are excluded.");
        if (scopedActivity.Any(row => !identities.ContainsKey(row.Activity.CommitmentId)))
            warnings.Add("Private-capital activity without an eligible retained commitment is excluded; no activity-to-vehicle association is inferred.");
        if (scopedActivity.Any(row => identities.TryGetValue(row.Activity.CommitmentId, out var commitment)
            && (row.Activity.EntityId != commitment.EntityId || row.Activity.Currency != commitment.Currency)))
            return Unavailable("Retained private-capital activity does not match its commitment entity and currency.");
        return new(commitments, activities, warnings);
    }

    private static bool Valid(PrivateCapitalSnapshotDto row, IReadOnlySet<string> entityIds)
    {
        var value = row.Commitment;
        return value is not null && !string.IsNullOrWhiteSpace(row.SnapshotId) && row.Revision > 0
            && row.RecordedAtUtc != default && !string.IsNullOrWhiteSpace(value.CommitmentId)
            && entityIds.Contains(value.EntityId) && !string.IsNullOrWhiteSpace(value.VehicleName)
            && !string.IsNullOrWhiteSpace(value.Strategy) && value.Currency is { Length: 3 }
            && value.Currency.All(char.IsAsciiLetterUpper) && !string.IsNullOrWhiteSpace(value.SourceSystem)
            && !string.IsNullOrWhiteSpace(value.SourceDocumentId) && value.AsOfDate != default
            && value.AsOfDate <= DateOnly.FromDateTime(row.RecordedAtUtc.UtcDateTime)
            && (value.ValuationDate is null || value.ValuationDate <= value.AsOfDate)
            && value.CommitmentAmount >= 0m && value.CalledAmount >= 0m && value.UnfundedAmount >= 0m
            && value.DistributedAmount >= 0m && value.BalancesAvailable
            && value.EvidenceCompleteness is "Complete" or "Partial"
            && value.BalanceStatus is "Retained" or "Provisional" or "Settled"
            && value.VintageYear is null or >= 1900 and <= 9999;
    }

    private static PrivateCapitalLifecycleRead Unavailable(string reason) => new([], [], [reason]);
    private static string Exact(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
