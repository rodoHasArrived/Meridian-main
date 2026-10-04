using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;

namespace Meridian.Ledger;

/// <summary>
/// Converts a <see cref="DailyPortfolioPricingProjection"/> into governed
/// <see cref="AutomatedJournalDraft"/> instances so daily fair-value marks flow through
/// <see cref="AutomatedJournalApproval"/> before posting to the books.
/// </summary>
public static class DailyPortfolioPricingDraftBuilder
{
    /// <summary>
    /// Builds one balanced draft per security/account scope. Splitting the valuation batch keeps
    /// Security Master identity unambiguous for posting controls and gives each corrected mark its
    /// own deterministic idempotency key.
    /// </summary>
    public static IReadOnlyList<AutomatedJournalDraft> BuildDrafts(DailyPortfolioPricingProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (!projection.IsBalanced)
            throw new InvalidOperationException("Daily portfolio pricing projection produced unbalanced journal lines.");

        var groups = projection.Lines
            .Where(static line => line.MarkAdjustment != 0m)
            .GroupBy(static line => new DraftScope(
                line.SecurityId,
                line.Symbol,
                line.FinancialAccountId ?? string.Empty))
            .OrderBy(static group => group.Key.SecurityId)
            .ThenBy(static group => group.Key.Symbol, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.FinancialAccountId, StringComparer.Ordinal)
            .ToArray();

        if (groups.Length == 0)
            return [];

        var drafts = new List<AutomatedJournalDraft>(groups.Length);
        foreach (var group in groups)
        {
            var lines = group
                .OrderBy(static line => line.EvidenceReference, StringComparer.Ordinal)
                .ThenBy(static line => line.Quantity)
                .ThenBy(static line => line.CostPrice)
                .ThenBy(static line => line.MarkPrice)
                .ToArray();
            drafts.Add(BuildDraft(projection, group.Key, lines));
        }

        return drafts;
    }

    /// <summary>
    /// Compatibility helper for a single-security projection. Multi-security projections fail
    /// closed so a caller cannot silently discard drafts; use <see cref="BuildDrafts"/> instead.
    /// </summary>
    public static AutomatedJournalDraft? BuildDraft(DailyPortfolioPricingProjection projection)
    {
        var drafts = BuildDrafts(projection);
        return drafts.Count switch
        {
            0 => null,
            1 => drafts[0],
            _ => throw new InvalidOperationException(
                "Daily portfolio pricing produced multiple security/account drafts; use BuildDrafts to retain the full batch.")
        };
    }

    private static AutomatedJournalDraft BuildDraft(
        DailyPortfolioPricingProjection projection,
        DraftScope scope,
        IReadOnlyList<DailyPortfolioPricingLine> pricingLines)
    {
        var input = projection.Input;
        var effectiveDate = DateOnly.FromDateTime(input.AsOf.UtcDateTime);
        var fingerprint = BuildFingerprint(input, scope, pricingLines);
        var idempotencyKey = FormattableString.Invariant(
            $"fair-value|fund={Escape(input.Policy.FundId)}|period={Escape(input.PeriodId)}|date={effectiveDate:yyyy-MM-dd}|security={Escape(SecurityKey(scope))}|account={Escape(AccountKey(scope))}|fp={fingerprint[..32]}");

        var journalLines = projection.JournalLines
            .Where(line => MatchesScope(line.PricingLine, scope))
            .Select(static line => (line.account, line.debit, line.credit, line.dimensions))
            .ToArray();
        var totalDebits = journalLines.Sum(static line => line.debit);
        var totalCredits = journalLines.Sum(static line => line.credit);
        if (journalLines.Length == 0 || totalDebits != totalCredits)
        {
            throw new InvalidOperationException(
                $"Daily portfolio pricing draft for {scope.Symbol} produced unbalanced journal lines.");
        }

        var evidence = BuildEvidence(input, scope, pricingLines, fingerprint);
        var marketValue = pricingLines.Sum(static line => line.MarketValue);
        var costBasis = pricingLines.Sum(static line => line.CostBasis);
        var priorCarryingValue = pricingLines.Sum(static line => line.PriorCarryingValue);
        var unrealizedGainOrLoss = pricingLines.Sum(static line => line.UnrealizedGainOrLoss);
        var markAdjustment = pricingLines.Sum(static line => line.MarkAdjustment);
        var securityLabel = scope.SecurityId?.ToString("D") ?? scope.Symbol;
        var accountLabel = string.IsNullOrEmpty(scope.FinancialAccountId)
            ? "unscoped account"
            : $"account {scope.FinancialAccountId}";
        var description = FormattableString.Invariant(
            $"Daily fair-value mark for {scope.Symbol} ({securityLabel}), {accountLabel}, fund {input.Policy.FundId}, period {input.PeriodId} as of {effectiveDate:yyyy-MM-dd}");

        var journalEvent = new AutomatedJournalEvent(
            AutomatedJournalEventKind.FairValueMarkAdjustment,
            scope.Symbol,
            markAdjustment,
            input.AsOf,
            FinancialAccountId: NullIfEmpty(scope.FinancialAccountId),
            Description: description,
            SecurityId: scope.SecurityId,
            SourceEventId: $"fair-value-mark:{fingerprint}",
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            EvidenceReferences: evidence);

        var fairValueLevels = string.Join(
            ",",
            pricingLines
                .Select(static line => line.FairValueLevel)
                .Distinct()
                .OrderBy(static level => level)
                .Select(static level => level.ToString()));
        var stalePricedCount = pricingLines.Count(static line => line.IsStalePriced);
        // A draft that mixes real and fabricated marks is never reported as entirely real.
        var draftProvenance = DataProvenanceExtensions.Strongest(
            pricingLines.Select(static line => line.Provenance));

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ValuationMarkEvidenceGuard.EvidenceTag] = JsonSerializer.Serialize(pricingLines.Select(line =>
                new ValuationMarkEvidence(input.Policy.FundId, line.Symbol, line.SecurityId,
                    line.FinancialAccountId, effectiveDate, line.PriceObservedOn, line.MarkPrice,
                    line.Confidence, input.Policy.FreshnessPolicy.MaximumAgeDays,
                    input.Policy.FreshnessPolicy.MinimumConfidence, input.Policy.FreshnessPolicy.Version,
                    line.EvidenceReference)).ToArray()),
            ["valuation.freshnessPolicyVersion"] = input.Policy.FreshnessPolicy.Version,
            ["valuation.policyId"] = input.Policy.PolicyId,
            ["valuation.method"] = input.Policy.ValuationMethod,
            ["valuation.periodId"] = input.PeriodId,
            ["valuation.baseCurrency"] = input.BaseCurrency,
            ["valuation.fundId"] = input.Policy.FundId,
            ["valuation.symbol"] = scope.Symbol,
            ["valuation.securityId"] = scope.SecurityId?.ToString("D") ?? string.Empty,
            ["valuation.financialAccountId"] = scope.FinancialAccountId,
            ["valuation.totalCostBasis"] = Format(costBasis),
            ["valuation.totalMarketValue"] = Format(marketValue),
            ["valuation.priorCarryingValue"] = Format(priorCarryingValue),
            ["valuation.hasPriorCarryingValue"] = pricingLines.All(static line => line.HasPriorCarryingValue).ToString(CultureInfo.InvariantCulture),
            ["valuation.netUnrealizedGainOrLoss"] = Format(unrealizedGainOrLoss),
            ["valuation.markAdjustment"] = Format(markAdjustment),
            ["valuation.markFingerprintSha256"] = fingerprint,
            ["valuation.fairValueLevels"] = fairValueLevels,
            [ValuationProvenanceTag.Key] = draftProvenance.Token(),
            ["valuation.stalePricedCount"] = stalePricedCount.ToString(CultureInfo.InvariantCulture),
            ["valuation.carryingValueSources"] = string.Join(",", pricingLines
                .Select(static line => line.CarryingValueSource)
                .Where(static source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal))
        };

        tags[ValuationMarkEvidenceGuard.DigestTag] = Sha256Digest.ComputeUtf8(tags[ValuationMarkEvidenceGuard.EvidenceTag]);

        var metadata = new JournalEntryMetadata(
            ActivityType: "fair-value-mark",
            Symbol: scope.Symbol,
            SecurityId: scope.SecurityId,
            FinancialAccountId: NullIfEmpty(scope.FinancialAccountId),
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            Tags: tags,
            EvidenceReferences: evidence);

        return new AutomatedJournalDraft(journalEvent, description, journalLines, metadata);
    }

    private static IReadOnlyList<JournalEvidenceReference> BuildEvidence(
        DailyPortfolioPricingInput input,
        DraftScope scope,
        IReadOnlyList<DailyPortfolioPricingLine> lines,
        string fingerprint)
    {
        var evidence = new List<JournalEvidenceReference>(lines.Count * 2);
        var index = 0;
        foreach (var line in lines)
        {
            evidence.Add(new JournalEvidenceReference(
                EvidenceId: FormattableString.Invariant(
                    $"fair-value-mark:{SecurityKey(scope)}:{input.PeriodId}:{fingerprint[..16]}:{index++}"),
                Uri: line.EvidenceReference,
                Kind: "price-mark",
                SourceSystem: line.PriceSource,
                RetainedAtUtc: input.AsOf,
                RetainedBy: input.Policy.ApprovedBy,
                SubjectId: scope.SecurityId?.ToString("D") ?? line.Symbol,
                Description: FormattableString.Invariant(
                    $"{line.Symbol} marked at {Format(line.MarkPrice)} via {line.PriceSource}; observed {(line.PriceObservedOn.HasValue ? line.PriceObservedOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "unknown")}; confidence {line.Confidence}; origin {line.Provenance.Token()}")).Normalize());

            if (!string.IsNullOrWhiteSpace(line.CarryingValueEvidenceReference))
            {
                evidence.Add(new JournalEvidenceReference(
                    EvidenceId: FormattableString.Invariant(
                        $"fair-value-carrying:{SecurityKey(scope)}:{input.PeriodId}:{fingerprint[..16]}:{index++}"),
                    Uri: line.CarryingValueEvidenceReference,
                    Kind: "prior-carrying-value",
                    SourceSystem: line.CarryingValueSource ?? "ledger",
                    RetainedAtUtc: line.CarryingValueCapturedAtUtc ?? input.AsOf,
                    RetainedBy: input.Policy.ApprovedBy,
                    SubjectId: scope.SecurityId?.ToString("D") ?? line.Symbol,
                    Description: FormattableString.Invariant(
                        $"Prior carrying value {Format(line.PriorCarryingValue)} for {line.Symbol}; account {(line.HasPriorCarryingValue ? "present" : "absent")}")).Normalize());
            }
        }

        return evidence;
    }

    private static string BuildFingerprint(
        DailyPortfolioPricingInput input,
        DraftScope scope,
        IReadOnlyList<DailyPortfolioPricingLine> lines)
    {
        var canonical = new StringBuilder(512)
            .Append("v1|")
            .Append(input.Policy.FundId).Append('|')
            .Append(input.Policy.PolicyId).Append('|')
            .Append(input.Policy.FreshnessPolicy.Version).Append('|')
            .Append(input.Policy.FreshnessPolicy.MaximumAgeDays).Append('|')
            .Append(input.Policy.FreshnessPolicy.MinimumConfidence).Append('|')
            .Append(input.Policy.ValuationMethod).Append('|')
            .Append(input.PeriodId).Append('|')
            .Append(DateOnly.FromDateTime(input.AsOf.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
            .Append(input.BaseCurrency).Append('|')
            .Append(scope.SecurityId?.ToString("D") ?? "none").Append('|')
            .Append(scope.Symbol).Append('|')
            .Append(scope.FinancialAccountId);

        foreach (var line in lines)
        {
            canonical
                .Append("||").Append(Format(line.Quantity))
                .Append('|').Append(Format(line.CostPrice))
                .Append('|').Append(Format(line.MarkPrice))
                .Append('|').Append(Format(line.CostBasis))
                .Append('|').Append(Format(line.MarketValue))
                .Append('|').Append(Format(line.PriorCarryingValue))
                .Append('|').Append(line.HasPriorCarryingValue ? "present" : "absent")
                .Append('|').Append(Format(line.MarkAdjustment))
                .Append('|').Append(line.PriceSource)
                .Append('|').Append(line.EvidenceReference)
                .Append('|').Append(line.PriceObservedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown")
                .Append('|').Append(line.Confidence)
                .Append('|').Append(line.FairValueLevel)
                .Append('|').Append(line.IsStalePriced ? "stale" : "current")
                .Append('|').Append(line.CarryingValueSource ?? "unknown")
                .Append('|').Append(line.CarryingValueEvidenceReference ?? "none")
                // Origin participates in the fingerprint so a simulated valuation run can never
                // share an idempotency key with the real run for the same scope and period.
                .Append('|').Append(line.Provenance.Token());
        }

        return Sha256Digest.ComputeUtf8(canonical.ToString());
    }

    private static bool MatchesScope(DailyPortfolioPricingLine line, DraftScope scope)
        => line.SecurityId == scope.SecurityId
            && string.Equals(line.Symbol, scope.Symbol, StringComparison.Ordinal)
            && string.Equals(line.FinancialAccountId ?? string.Empty, scope.FinancialAccountId, StringComparison.Ordinal);

    private static string SecurityKey(DraftScope scope)
        => scope.SecurityId?.ToString("D") ?? scope.Symbol;

    private static string AccountKey(DraftScope scope)
        => string.IsNullOrEmpty(scope.FinancialAccountId) ? "unscoped" : scope.FinancialAccountId;

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string Format(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private readonly record struct DraftScope(
        Guid? SecurityId,
        string Symbol,
        string FinancialAccountId);
}
