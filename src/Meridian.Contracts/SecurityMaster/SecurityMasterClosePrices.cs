using System.Text.Json;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>A supported manual adjustment; the original selected source evidence remains retained.</summary>
public sealed record SecurityClosePriceOverrideDto(decimal Price, SecurityPriceUnit Unit, string Rationale, string EvidenceReference);
public sealed record SecurityClosePriceInputDto(Guid SecurityId, SecurityClosePriceOverrideDto? Override = null);

/// <summary>The date uses UTC; account and book identities are exact, case-sensitive scopes.</summary>
public sealed record CreateSecurityClosePriceSetRequest(
    string AccountId, string BookId, DateOnly ValuationDate, DateTimeOffset EvaluatedAsOf,
    IReadOnlyList<SecurityClosePriceInputDto> Securities, decimal MaxSourceDispersionPercent,
    string CreatedBy, Guid? CorrectsSnapshotId = null, string? CorrectionRationale = null,
    DateTimeOffset? KnowledgeAsOf = null);

public sealed record ApproveSecurityClosePriceSetRequest(
    string AccountId, string BookId, DateOnly ValuationDate, Guid SnapshotId, string Reviewer, string Rationale);

public sealed record SecurityClosePriceLineDto(
    Guid SecurityId, SecurityPriceGoldenCopyDto? Selection, IReadOnlyList<SecurityRawPriceDto> Observations,
    SecurityClosePriceOverrideDto? Override, decimal? ClosePrice, SecurityPriceUnit Unit);
public sealed record SecurityClosePriceBlockerDto(Guid SecurityId, string Code, string Reason);

/// <summary>An immutable review input. Corrections append a contiguous linked version.</summary>
public sealed record SecurityClosePriceSnapshotDto(
    Guid SnapshotId, string AccountId, string BookId, DateOnly ValuationDate, int Version,
    Guid? CorrectsSnapshotId, string? CorrectionRationale, DateTimeOffset EvaluatedAsOf,
    DateTimeOffset KnowledgeAsOf, decimal MaxSourceDispersionPercent, string CreatedBy,
    DateTimeOffset CreatedAt, IReadOnlyList<SecurityClosePriceLineDto> Lines,
    IReadOnlyList<SecurityClosePriceBlockerDto> Blockers);

/// <summary>Independent review bound to the exact immutable snapshot digest.</summary>
public sealed record SecurityClosePriceApprovalDto(
    Guid SnapshotId, string SnapshotSha256, string Reviewer, string Rationale, DateTimeOffset ApprovedAt);
public sealed record SecurityClosePriceSetDto(SecurityClosePriceSnapshotDto Snapshot, SecurityClosePriceApprovalDto? Approval);

/// <summary>Shared fail-closed evidence rules used at both application and durable write boundaries.</summary>
public static class SecurityClosePriceEvidence
{
    public static string Digest(SecurityClosePriceSnapshotDto snapshot)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(snapshot, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto));

    public static IReadOnlyList<SecurityClosePriceBlockerDto> Assess(
        IReadOnlyList<SecurityClosePriceLineDto> lines, decimal maxDispersionPercent)
    {
        var blockers = new List<SecurityClosePriceBlockerDto>();
        foreach (var line in lines)
        {
            void Block(string code, string reason) => blockers.Add(new(line.SecurityId, code, reason));
            if (line.Selection is not { } selected)
            {
                Block("UnsupportedPrice", "No supported retained source selection exists for this security and account.");
                continue;
            }
            if (selected.IsStaleFallback)
                Block("StalePrice", $"Selected source {selected.SelectedSource} is {selected.DaysStale} days old and exceeds its hierarchy limit.");
            if (selected.GoldenCopyPrice <= 0 || line.Observations.Any(p => p.Price <= 0))
                Block("UnsupportedPrice", "Retained source observations must contain positive supported prices; an override cannot repair unsupported source evidence.");
            if (line.ClosePrice is null or <= 0 || line.Unit == SecurityPriceUnit.Unspecified || !Enum.IsDefined(line.Unit))
                Block("UnsupportedPrice", "A positive close price with an explicit supported quote unit is required.");
            if (line.Observations.Any(p => p.Unit != line.Unit || p.Unit == SecurityPriceUnit.Unspecified || !Enum.IsDefined(p.Unit)))
                Block("IncomparableUnits", "Retained source observations have unsupported or different quote units; no undeclared conversion is permitted.");
            if (line.Override is { } manual && (string.IsNullOrWhiteSpace(manual.Rationale)
                || string.IsNullOrWhiteSpace(manual.EvidenceReference) || manual.Unit != selected.Unit))
                Block("UnsupportedOverride", "An override requires rationale, supporting evidence and the selected source's quote unit.");
            if (line.ClosePrice is > 0)
            {
                var comparable = line.Observations.Where(p => p.Unit == line.Unit).Select(p => p.Price)
                    .Append(line.ClosePrice.Value).ToArray();
                var dispersion = (comparable.Max() - comparable.Min()) / line.ClosePrice.Value * 100m;
                if (dispersion > maxDispersionPercent)
                    Block("SourceDispersion", FormattableString.Invariant($"Source dispersion {dispersion:0.####}% exceeds the retained {maxDispersionPercent:0.####}% limit."));
            }
        }
        return blockers;
    }

    public static void Validate(SecurityClosePriceSnapshotDto snapshot)
    {
        if (snapshot.SnapshotId == Guid.Empty || string.IsNullOrWhiteSpace(snapshot.AccountId)
            || snapshot.AccountId != snapshot.AccountId.Trim() || string.IsNullOrWhiteSpace(snapshot.BookId)
            || snapshot.BookId != snapshot.BookId.Trim() || snapshot.ValuationDate == default
            || System.Text.Encoding.UTF8.GetByteCount(snapshot.AccountId) > 256 || System.Text.Encoding.UTF8.GetByteCount(snapshot.BookId) > 256
            || snapshot.Version < 1 || snapshot.EvaluatedAsOf == default || snapshot.KnowledgeAsOf == default
            || DateOnly.FromDateTime(snapshot.EvaluatedAsOf.UtcDateTime) != snapshot.ValuationDate
            || snapshot.CreatedAt == default || string.IsNullOrWhiteSpace(snapshot.CreatedBy)
            || snapshot.CreatedBy != snapshot.CreatedBy.Trim() || snapshot.MaxSourceDispersionPercent < 0
            || (snapshot.Version == 1) != (snapshot.CorrectsSnapshotId is null)
            || snapshot.CorrectsSnapshotId == Guid.Empty || snapshot.CorrectsSnapshotId == snapshot.SnapshotId
            || (snapshot.CorrectsSnapshotId is not null && string.IsNullOrWhiteSpace(snapshot.CorrectionRationale))
            || snapshot.Lines.Count == 0 || snapshot.Lines.Any(l => l.SecurityId == Guid.Empty)
            || snapshot.Lines.Select(l => l.SecurityId).Distinct().Count() != snapshot.Lines.Count)
            throw new ArgumentException("A close snapshot requires exact account/book/date scope, distinct securities, actor, cutoffs and valid correction lineage.");
        foreach (var line in snapshot.Lines)
        {
            if (line.Selection is { } selection)
            {
                if (selection.SecurityId != line.SecurityId || selection.SelectionReceiptId is null)
                    throw new ArgumentException("A close line must reference its retained security selection.");
                if (selection.SelectionReceiptId == Guid.Empty || selection.EvaluatedAsOf != snapshot.EvaluatedAsOf
                    || selection.KnowledgeAsOf != snapshot.KnowledgeAsOf
                    || selection.HierarchySnapshot is not { } hierarchy || hierarchy.SecurityId != line.SecurityId
                    || hierarchy.AccountId?.Trim() != snapshot.AccountId
                    || line.ClosePrice != (line.Override?.Price ?? selection.GoldenCopyPrice)
                    || line.Unit != (line.Override?.Unit ?? selection.Unit))
                    throw new ArgumentException("Close selection evidence, units and final price must agree with the retained scope and cutoffs.");
                var observations = new[] { new SecurityRawPriceDto(selection.SelectedSource, selection.GoldenCopyPrice, selection.PriceAsOf, selection.Unit) }
                    .Concat(selection.ComparisonPrices.Select(p => new SecurityRawPriceDto(p.SourceId, p.Price, p.PriceAsOf, p.Unit)))
                    .OrderBy(p => p.SourceId, StringComparer.Ordinal).ToArray();
                if (!observations.SequenceEqual(line.Observations))
                    throw new ArgumentException("Close observations must retain the exact selection and comparison source rows.");
            }
            else if (line.ClosePrice is not null || line.Unit != SecurityPriceUnit.Unspecified)
                throw new ArgumentException("Unsupported source inputs cannot supply a certifiable close price.");
        }
        if (!Assess(snapshot.Lines, snapshot.MaxSourceDispersionPercent).SequenceEqual(snapshot.Blockers))
            throw new ArgumentException("Close snapshot blockers must agree with retained source evidence.");
    }

    public static void ValidateApproval(SecurityClosePriceSnapshotDto snapshot, SecurityClosePriceApprovalDto approval)
    {
        Validate(snapshot);
        if (snapshot.Blockers.Count > 0 || approval.SnapshotId != snapshot.SnapshotId
            || !Sha256Digest.FixedEquals(approval.SnapshotSha256, Digest(snapshot))
            || string.IsNullOrWhiteSpace(approval.Reviewer) || approval.Reviewer != approval.Reviewer.Trim()
            || string.Equals(approval.Reviewer, snapshot.CreatedBy, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(approval.Rationale) || approval.ApprovedAt < snapshot.CreatedAt)
            throw new InvalidOperationException("Close approval requires an independent reviewer, rationale, matching immutable evidence and no unresolved blockers.");
    }
}
