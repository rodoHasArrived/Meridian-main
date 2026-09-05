using System.Text.Json;
using Meridian.Contracts.Integrity;

namespace Meridian.Ledger;

/// <summary>Immutable input facts retained with each security/account valuation journal.</summary>
public sealed record ValuationMarkEvidence(
    string FundId,
    string Symbol,
    Guid? SecurityId,
    string? FinancialAccountId,
    DateOnly ValuationDate,
    DateOnly? ObservedOn,
    decimal Price,
    DailyPortfolioPriceConfidence Confidence,
    int MaximumAgeDays,
    DailyPortfolioPriceConfidence MinimumConfidence,
    string PolicyVersion,
    string EvidenceReference);

/// <summary>
/// Reassesses retained inputs at submit, approve and post. Review notes and arbitrary override
/// tags never replace a dated mark. Historical valuations are compared to their valuation date.
/// </summary>
public static class ValuationMarkEvidenceGuard
{
    public const string EvidenceTag = "valuation.markEvidence.v1";
    public const string DigestTag = "valuation.markEvidence.sha256";

    public static bool IsValuation(string? idempotencyKey)
        => idempotencyKey?.StartsWith("fair-value|", StringComparison.Ordinal) == true;

    public static string? Validate(string? json, string fundId, DateOnly valuationDate,
        Guid? securityId, string? financialAccountId, string? symbol = null, string? expectedDigest = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "Valuation mark evidence is missing; rerun valuation with dated source evidence.";
        if (!Sha256Digest.IsCanonical(expectedDigest) ||
            !Sha256Digest.FixedEquals(expectedDigest, Sha256Digest.ComputeUtf8(json)))
            return "Valuation mark evidence or policy differs from the retained server assessment; rerun valuation.";
        ValuationMarkEvidence[]? evidence;
        try { evidence = JsonSerializer.Deserialize<ValuationMarkEvidence[]>(json); }
        catch (JsonException) { return "Valuation mark evidence is invalid; rerun valuation."; }
        if (evidence is null || evidence.Length == 0)
            return "Valuation mark evidence is empty; rerun valuation.";
        foreach (var mark in evidence)
        {
            if (mark is null || mark.FundId != fundId || mark.ValuationDate != valuationDate ||
                mark.SecurityId != securityId ||
                !string.Equals(mark.FinancialAccountId ?? "", financialAccountId ?? "", StringComparison.Ordinal) ||
                (symbol is not null && !string.Equals(mark.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
                return "Valuation mark evidence does not match the fund, position or valuation date.";
            if (string.IsNullOrWhiteSpace(mark.PolicyVersion) || string.IsNullOrWhiteSpace(mark.EvidenceReference) ||
                mark.MaximumAgeDays < 0 || !Enum.IsDefined(mark.MinimumConfidence))
                return "Valuation mark evidence has no valid policy version or retained source reference.";
            var policy = new ValuationFreshnessPolicy(mark.MaximumAgeDays, mark.MinimumConfidence, mark.PolicyVersion);
            var assessment = policy.Assess(mark.Symbol, mark.SecurityId, mark.FinancialAccountId,
                valuationDate, mark.ObservedOn, mark.Confidence, mark.Price);
            if (assessment.BlockReason is { } reason)
                return $"{mark.Symbol}: {reason}";
        }
        return null;
    }

    public static void EnsureValid(AutomatedJournalDraft draft)
    {
        if (!IsValuation(draft.Metadata.IdempotencyKey) && draft.Metadata.ActivityType != "fair-value-mark")
            return;
        var json = draft.Metadata.Tags?.GetValueOrDefault(EvidenceTag);
        var reason = Validate(json, draft.Metadata.Tags?.GetValueOrDefault("valuation.fundId") ?? "",
            draft.Metadata.EffectiveDate ?? DateOnly.FromDateTime(draft.Event.Timestamp.UtcDateTime),
            draft.Event.SecurityId, draft.Event.FinancialAccountId, draft.Event.Symbol,
            draft.Metadata.Tags?.GetValueOrDefault(DigestTag));
        if (reason is not null)
            throw new InvalidOperationException(reason);
    }
}
