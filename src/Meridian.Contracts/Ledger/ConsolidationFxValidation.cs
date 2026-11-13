using System.Text.Json;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Ledger;

/// <summary>Shared invariants for approved FX evidence at every persistence boundary.</summary>
public static class ConsolidationFxValidation
{
    public static void Validate(ConsolidationFxRateSetDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.RateSetId == Guid.Empty || value.Revision < 1 || string.IsNullOrWhiteSpace(value.TenantId) ||
            string.IsNullOrWhiteSpace(value.CompanyId) || string.IsNullOrWhiteSpace(value.SourceEvidence) ||
            string.IsNullOrWhiteSpace(value.PreparedBy) || string.IsNullOrWhiteSpace(value.ApprovedBy) || value.ApprovedAt == default)
            throw new ArgumentException("Rate-set identity, scope, evidence, preparation and approval are required.");
        if (value.Rates is not { Count: > 0 } || value.Rates.Any(x => x is null) ||
            value.Rates.Select(x => x.RateId).Distinct().Count() != value.Rates.Count)
            throw new ArgumentException("A rate set requires uniquely identified rate observations.");
        foreach (var rate in value.Rates)
        {
            if (rate.RateId == Guid.Empty || rate.Rate <= 0m || !Currency(rate.FromCurrency) || !Currency(rate.ToCurrency) ||
                rate.FromCurrency == rate.ToCurrency || !Enum.IsDefined(rate.Kind) || string.IsNullOrWhiteSpace(rate.SourceEvidence))
                throw new ArgumentException("Every rate requires a positive value, explicit pair, kind and retained source.");
            if (rate.Kind == ConsolidationFxRateKindDto.PeriodAverage)
            {
                if (rate.PeriodStart is null || rate.PeriodEnd is null || rate.PeriodStart > rate.PeriodEnd || rate.RateDate < rate.PeriodEnd)
                    throw new ArgumentException("An average rate requires an exact completed reporting period.");
            }
            else if (rate.PeriodStart is not null || rate.PeriodEnd is not null)
                throw new ArgumentException("Only an average quote may declare an averaging period.");
        }
        if (value.Rates.GroupBy(x => (x.FromCurrency, x.ToCurrency, x.RateDate, x.Kind, x.PeriodStart, x.PeriodEnd)).Any(x => x.Count() != 1))
            throw new ArgumentException("A rate set cannot contain ambiguous observations for one pair, kind and date.");
        ValidateHash(value.Revision, value.PreviousHash);
        if (value.ContentHash is not null && !Sha256Digest.FixedEquals(value.ContentHash, Hash(value)))
            throw new ArgumentException("The rate-set content hash does not match the approved evidence.");
    }

    public static void Validate(ConsolidationFxPolicyDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value.PolicyId) || string.IsNullOrWhiteSpace(value.Revision) || !Currency(value.ReportingCurrency) ||
            value.DecimalPlaces != CurrencyMinorUnits.GetPrecision(value.ReportingCurrency) || string.IsNullOrWhiteSpace(value.TranslationReserveAccount))
            throw new ArgumentException("Translation policy identity, reporting currency, precision and reserve account are required.");
        var rules = value.AccountRules ?? [];
        var dates = value.ClosingRateDates ?? [];
        if (dates.Any(x => x is null || x.RateDate > x.AsOf || string.IsNullOrWhiteSpace(x.EvidenceReference)) ||
            dates.Select(x => x.AsOf).Distinct().Count() != dates.Count)
            throw new ArgumentException("Closing-date selections require unique as-of dates and evidenced dates no later than as-of.");
        if (rules.Any(x => x is null || string.IsNullOrWhiteSpace(x.AccountPath) || !Enum.IsDefined(x.Method)) ||
            rules.Select(x => x.AccountPath).Distinct(StringComparer.Ordinal).Count() != rules.Count)
            throw new ArgumentException("Translation account rules must resolve unambiguously.");
        foreach (var rule in rules)
            if ((rule.Method == ConsolidationFxRateKindDto.PeriodAverage &&
                 (rule.PeriodStart is null || rule.PeriodEnd is null || rule.PeriodStart > rule.PeriodEnd)) ||
                (rule.Method != ConsolidationFxRateKindDto.PeriodAverage && (rule.PeriodStart is not null || rule.PeriodEnd is not null)))
                throw new ArgumentException("Only an explicit average treatment declares a nonempty averaging period.");
    }

    public static void Validate(ConsolidationFxBookConfigurationDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.LedgerBookId == Guid.Empty || value.Revision < 1 || string.IsNullOrWhiteSpace(value.TenantId) ||
            string.IsNullOrWhiteSpace(value.CompanyId) || value.RateSetId == Guid.Empty || value.RateSetRevision < 1 ||
            string.IsNullOrWhiteSpace(value.ApprovedBy) || value.ApprovedAt == default ||
            (value.EffectiveTo is { } end && end <= value.EffectiveFrom))
            throw new ArgumentException("Book FX configuration requires exact scope, approval, rate-set revision and effective interval.");
        Validate(value.Policy);
        var facts = value.HistoricalFacts ?? [];
        if (facts.Any(x => x is null || x.LedgerBookId == Guid.Empty || x.JournalEntryId == Guid.Empty || x.LineId == Guid.Empty ||
                string.IsNullOrWhiteSpace(x.EvidenceReference) || (x.HistoricalDate is null && x.OpeningTranslatedSignedAmount is null)) ||
            facts.Select(x => (x.LedgerBookId, x.JournalEntryId, x.LineId)).Distinct().Count() != facts.Count)
            throw new ArgumentException("Historical and opening-balance facts require unique exact source identities and evidence.");
        ValidateHash(value.Revision, value.PreviousHash);
        if (value.ContentHash is not null && !Sha256Digest.FixedEquals(value.ContentHash, Hash(value)))
            throw new ArgumentException("The book FX configuration hash does not match its approved evidence.");
    }

    public static string Hash(ConsolidationFxRateSetDto value)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(value with { ContentHash = null }, ConsolidationFxJsonContext.Default.ConsolidationFxRateSetDto));
    public static string Hash(ConsolidationFxBookConfigurationDto value)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(value with { ContentHash = null }, ConsolidationFxJsonContext.Default.ConsolidationFxBookConfigurationDto));

    private static bool Currency(string? value)
        => value is { Length: 3 } && value.All(c => c is >= 'A' and <= 'Z');
    private static void ValidateHash(int revision, string? previousHash)
    {
        if ((revision == 1 && previousHash is not null) || (revision > 1 && !Sha256Digest.IsCanonical(previousHash)))
            throw new ArgumentException("The first revision has no predecessor; later revisions require its exact content hash.");
    }
}
