using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Instruments.AssetOperations;

/// <summary>Source-backed account cash reconciliation using the existing shared cash ladder.</summary>
public static class AccountCashForecastEngine
{
    public const string EngineVersion = "account-cash-forecast-v1";

    public static AccountCashForecastSnapshotDto Build(AccountCashForecastRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputs = Canonicalize(request);
        var issues = new List<AccountCashForecastIssueDto>();
        var scope = inputs.Scope;
        if (!IsCompleteScope(scope))
            Add("cash-forecast.scope-required", "Explicit tenant, company, account, book and base-currency scope is required.");
        if (string.IsNullOrWhiteSpace(inputs.RunId))
            Add("cash-forecast.run-required", "A retained run identity is required.");
        var validPeriod = inputs.PeriodStart != default && inputs.PeriodEnd >= inputs.PeriodStart
            && inputs.PeriodEnd != DateOnly.MaxValue && inputs.PeriodEnd.DayNumber - inputs.PeriodStart.DayNumber < 3660;
        if (!validPeriod)
            Add("cash-forecast.period-invalid", "An inclusive cash period of at most ten years is required.");
        if (inputs.BucketDays is <= 0 or > 3660)
            Add("cash-forecast.bucket-invalid", "Cash bucket days must be between one and 3660.");
        if (inputs.RecordedAtUtc == default || inputs.RecordedAtUtc.Offset != TimeSpan.Zero)
            Add("cash-forecast.recorded-at-required", "A retained UTC recording time is required.");
        if (inputs.PreviousSnapshotId is not null && string.IsNullOrWhiteSpace(inputs.CorrectionRationale))
            Add("cash-forecast.correction-rationale-required", "A linked forecast revision requires a correction rationale.");

        ValidateObservation(inputs.OpeningCash, inputs.PeriodStart, "opening");
        if (inputs.ClosingCash is not null)
            ValidateObservation(inputs.ClosingCash, inputs.PeriodEnd, "closing");

        foreach (var duplicate in inputs.Flows.GroupBy(static row => (row.FlowId, row.Status)).Where(static group => group.Count() > 1))
            Add("cash-forecast.flow-ambiguous", "Only one expected and one actual revision of a stable flow may be selected per snapshot.", duplicate.Key.FlowId);
        foreach (var duplicate in inputs.Flows.GroupBy(static row => row.VersionId, StringComparer.Ordinal).Where(static group => group.Count() > 1))
            Add("cash-forecast.version-duplicate", "A source flow version may be selected only once.", duplicate.Key);
        foreach (var flow in inputs.Flows)
        {
            if (flow.AccountId != scope.AccountId || flow.BookId != scope.BookId)
                Add("cash-forecast.flow-scope-mismatch", "The source flow belongs to a different account or book.", flow.VersionId);
            if (string.IsNullOrWhiteSpace(flow.FlowId) || string.IsNullOrWhiteSpace(flow.VersionId) || string.IsNullOrWhiteSpace(flow.FlowType))
                Add("cash-forecast.flow-identity-required", "Stable flow, source-version and flow-type identities are required.", flow.VersionId);
            if (flow.EconomicDate is null or { DayNumber: 0 } || flow.SettlementDate is null or { DayNumber: 0 }
                || flow.CashReceiptDate is null or { DayNumber: 0 })
                Add("cash-forecast.flow-dates-required", "Economic, settlement and cash-receipt dates must be retained separately.", flow.VersionId);
            if (!Enum.IsDefined(flow.Status))
                Add("cash-forecast.flow-status-invalid", "Flow status must identify expected or actual cash.", flow.VersionId);
            ValidateProvenance(flow.Provenance, flow.VersionId,
                flow.Status == AccountCashFlowStatus.Actual ? AccountCashEvidenceRole.Observation : AccountCashEvidenceRole.Projection);
            if (!IsCurrency(flow.Currency))
                Add("cash-forecast.currency-required", "A three-letter uppercase flow currency is required.", flow.VersionId);
            if (flow.Status == AccountCashFlowStatus.Actual && flow.Amount != Round(flow.Amount, flow.Currency))
                Add("cash-forecast.currency-precision-invalid", "Source cash must be represented in declared currency minor units.", flow.VersionId);
            if (flow.Status == AccountCashFlowStatus.Actual && flow.Provenance.Evidence.EffectiveDate != flow.EconomicDate)
                Add("cash-forecast.flow-evidence-date-mismatch", "Flow evidence must retain the economic event date.", flow.VersionId);
            if (flow.Status == AccountCashFlowStatus.Actual && flow.ProjectionRunId is not null)
                Add("cash-forecast.actual-projection-invalid", "A projection run cannot serve as observed cash evidence.", flow.VersionId);
            if (flow.Status == AccountCashFlowStatus.Actual && flow.CashReceiptDate > DateOnly.FromDateTime(inputs.RecordedAtUtc.UtcDateTime))
                Add("cash-forecast.actual-date-in-future", "A future cash receipt cannot be treated as an observed movement.", flow.VersionId);
            if (flow.Status == AccountCashFlowStatus.Actual && flow.CashReceiptDate > DateOnly.FromDateTime(flow.Provenance.Evidence.RetainedAtUtc.UtcDateTime))
                Add("cash-forecast.actual-not-observed-at-retention", "Cash receipt must already have occurred when observed source evidence was retained.", flow.VersionId);
        }
        foreach (var group in inputs.Flows.GroupBy(static row => row.FlowId, StringComparer.Ordinal))
        {
            var expected = group.FirstOrDefault(static row => row.Status == AccountCashFlowStatus.Expected);
            var actual = group.FirstOrDefault(static row => row.Status == AccountCashFlowStatus.Actual);
            if (expected is not null && actual is not null && (expected.FlowType != actual.FlowType
                || (expected.SecurityId.HasValue && actual.SecurityId.HasValue && expected.SecurityId != actual.SecurityId)))
                Add("cash-forecast.flow-link-mismatch", "Linked expected and observed cash must identify the same flow type and retained security.", group.Key);
            if (expected is not null && actual is not null &&
                Sha256Digest.FixedEquals(expected.Provenance.SourceContentHash, actual.Provenance.SourceContentHash))
                Add("cash-forecast.actual-independent-source-required", "Expected economics and observed cash require independent retained source observations.", group.Key);
        }
        foreach (var duplicate in inputs.FxConversions.GroupBy(static row => (row.Currency, row.BaseCurrency, row.ConversionDate)).Where(static group => group.Count() > 1))
            Add("cash-forecast.fx-ambiguous", "More than one FX rate is declared for the same direction and date.", duplicate.Key.Currency);
        foreach (var fx in inputs.FxConversions)
        {
            if (!IsCurrency(fx.Currency) || fx.BaseCurrency != scope.BaseCurrency || fx.Currency == fx.BaseCurrency
                || fx.ConversionDate == default || fx.BaseUnitsPerCurrencyUnit <= 0m)
                Add("cash-forecast.fx-invalid", "FX must declare a positive base-currency-per-native-unit rate and exact conversion date.", fx.Currency);
            ValidateProvenance(fx.Provenance, fx.Currency, AccountCashEvidenceRole.DeclaredFx);
            if (fx.Provenance.Evidence.EffectiveDate != fx.ConversionDate)
                Add("cash-forecast.fx-evidence-date-mismatch", "FX evidence must identify the declared conversion date.", fx.Currency);
        }

        decimal? opening = null, actualNet = null, reconciled = null, closing = null, variance = null;
        var contributions = new List<PortfolioCashLadderContributionDto>();
        try
        {
            if (inputs.OpeningCash is { } openingObservation && openingObservation.AccountId == scope.AccountId && openingObservation.BookId == scope.BookId)
                opening = Convert(inputs.OpeningCash.Amount, inputs.OpeningCash.Currency, inputs.PeriodStart, "opening");
            if (inputs.ClosingCash is { } closingObservation && closingObservation.AccountId == scope.AccountId && closingObservation.BookId == scope.BookId)
                closing = Convert(inputs.ClosingCash.Amount, inputs.ClosingCash.Currency, inputs.PeriodEnd, "closing");
            var scoped = inputs.Flows.Where(row => row.AccountId == scope.AccountId && row.BookId == scope.BookId).ToArray();
            var effective = scoped.GroupBy(static row => row.FlowId, StringComparer.Ordinal)
                .Select(static group => group.FirstOrDefault(static row => row.Status == AccountCashFlowStatus.Actual) ?? group.First())
                .Where(row => row.CashReceiptDate >= inputs.PeriodStart && row.CashReceiptDate <= inputs.PeriodEnd).ToArray();
            var included = scoped.Where(row => row.CashReceiptDate >= inputs.PeriodStart && row.CashReceiptDate <= inputs.PeriodEnd).ToArray();
            var actuals = included.Where(static row => row.Status == AccountCashFlowStatus.Actual).ToArray();
            var cashRows = effective.Concat(actuals).DistinctBy(static row => row.VersionId, StringComparer.Ordinal);
            var converted = cashRows.ToDictionary(static row => row.VersionId,
                row => Convert(row.Amount, row.Currency, row.CashReceiptDate!.Value, row.VersionId), StringComparer.Ordinal);
            if (actuals.All(row => converted[row.VersionId].HasValue))
                actualNet = actuals.Sum(row => converted[row.VersionId]!.Value);
            if (opening.HasValue && actualNet.HasValue)
                reconciled = checked(opening.Value + actualNet.Value);
            if (reconciled.HasValue && closing.HasValue)
                variance = checked(reconciled.Value - closing.Value);
            foreach (var flow in effective)
            {
                if (converted[flow.VersionId] is { } amount)
                    contributions.Add(new PortfolioCashLadderContributionDto(
                        flow.SecurityId ?? Guid.Empty, flow.FlowId, "AccountCash", flow.ProjectionRunId,
                        flow.ProjectionEngineVersion, flow.TermsVersionId, flow.TermsHash,
                        flow.Provenance.SourceDomain, flow.Provenance.SourceEntityId,
                        flow.FlowType, ResolveLane(flow.FlowType), flow.CashReceiptDate!.Value,
                        amount, scope.BaseCurrency, amount, null));
            }
        }
        catch (OverflowException)
        {
            Add("cash-forecast.amount-overflow", "Retained amounts or declared FX exceed supported decimal arithmetic.");
            opening = actualNet = reconciled = closing = variance = null;
            contributions.Clear();
        }
        catch (ArgumentException) when (issues.Any(static issue => issue.Code is "cash-forecast.version-duplicate"))
        {
            opening = actualNet = reconciled = closing = variance = null;
            contributions.Clear();
        }

        var forecastBlockers = issues.Select(static issue => issue.Message).Distinct(StringComparer.Ordinal).ToArray();
        var start = validPeriod ? inputs.PeriodStart : new DateOnly(2000, 1, 1);
        var end = validPeriod ? inputs.PeriodEnd : start;
        PortfolioCashLadderDto ladder;
        try
        {
            ladder = PortfolioCashLadderEngine.BuildFromRetainedContributions(start, end, scope.BaseCurrency,
                opening ?? 0m, contributions, inputs.MinimumCashThreshold, inputs.BucketDays, inputs.RecordedAtUtc, forecastBlockers);
        }
        catch (OverflowException)
        {
            Add("cash-forecast.amount-overflow", "The cumulative cash ladder exceeds supported decimal arithmetic.");
            ladder = PortfolioCashLadderEngine.BuildFromRetainedContributions(start, end, scope.BaseCurrency,
                opening ?? 0m, [], inputs.MinimumCashThreshold, inputs.BucketDays, inputs.RecordedAtUtc,
                ["The cumulative cash ladder exceeds supported decimal arithmetic."]);
        }
        if (inputs.ClosingCash is null)
            Add("cash-forecast.closing-required", "Observed closing settled cash is required to certify the forecast.");
        else
        {
            foreach (var expected in inputs.Flows.Where(row => row.Status == AccountCashFlowStatus.Expected
                && row.AccountId == scope.AccountId && row.BookId == scope.BookId
                && row.CashReceiptDate >= inputs.PeriodStart && row.CashReceiptDate <= inputs.PeriodEnd
                && !inputs.Flows.Any(actual => actual.Status == AccountCashFlowStatus.Actual && actual.FlowId == row.FlowId
                    && actual.AccountId == scope.AccountId && actual.BookId == scope.BookId)))
                Add("cash-forecast.expected-unreceived", "An expected cash movement remains unobserved at the retained closing boundary.", expected.FlowId);
        }
        if (variance is not null and not 0m)
            Add("cash-forecast.closing-variance", "Opening cash plus validated observed cash movements does not reconcile to closing settled cash.");
        var requestHash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(inputs, AccountCashForecastJsonContext.Default.AccountCashForecastRequestDto));
        var evidenceHash = Sha256Digest.ComputeUtf8($"{EngineVersion}|{requestHash}");
        return new AccountCashForecastSnapshotDto(evidenceHash, evidenceHash, EngineVersion, inputs,
            opening, actualNet, reconciled, closing, variance, issues.Count == 0 && variance == 0m, ladder, issues.ToArray());

        void Add(string code, string message, string? inputId = null) => issues.Add(new(code, message, inputId));

        void ValidateObservation(AccountSettledCashObservationDto? observation, DateOnly date, string label)
        {
            if (observation is null)
            {
                Add("cash-forecast.opening-required", "Opening settled cash is required.", label);
                return;
            }
            if (observation.AccountId != scope.AccountId || observation.BookId != scope.BookId)
                Add("cash-forecast.balance-scope-mismatch", "Settled cash belongs to a different account or book.", label);
            if (observation.AsOfDate != date || !observation.IsSettled)
                Add("cash-forecast.balance-date-or-state-invalid", "Settled cash must identify the requested period boundary and settled state.", label);
            if (observation.AsOfDate > DateOnly.FromDateTime(inputs.RecordedAtUtc.UtcDateTime))
                Add("cash-forecast.balance-date-in-future", "A future balance cannot serve as observed settled cash.", label);
            if (observation.AsOfDate > DateOnly.FromDateTime(observation.Provenance.Evidence.RetainedAtUtc.UtcDateTime))
                Add("cash-forecast.balance-not-observed-at-retention", "A settled balance must already have been observed when its source evidence was retained.", label);
            if (!IsCurrency(observation.Currency) || observation.Amount != Round(observation.Amount, observation.Currency))
                Add("cash-forecast.balance-currency-invalid", "Settled cash requires currency evidence and valid currency minor units.", label);
            ValidateProvenance(observation.Provenance, label, AccountCashEvidenceRole.Observation);
            if (observation.Provenance.Evidence.EffectiveDate != observation.AsOfDate)
                Add("cash-forecast.balance-evidence-date-mismatch", "Settled cash evidence must identify its observation date.", label);
        }

        void ValidateProvenance(AccountCashProvenanceDto provenance, string id, AccountCashEvidenceRole role)
        {
            if (string.IsNullOrWhiteSpace(provenance.SourceDomain) || string.IsNullOrWhiteSpace(provenance.SourceEntityId)
                || !Sha256Digest.IsCanonical(provenance.SourceContentHash) || provenance.SourceContentHash.All(static ch => ch == '0')
                || !RetainedEvidenceIdentityValidator.IsComplete(provenance.Evidence)
                || provenance.Evidence.SourceSystem != provenance.SourceDomain || provenance.Evidence.SourceReference != provenance.SourceEntityId
                || !Sha256Digest.FixedEquals(provenance.SourceContentHash, provenance.Evidence.ContentHashSha256))
                Add("cash-forecast.provenance-incomplete", "Source identity, bytes, accepted review and retained evidence must agree.", id);
            if (provenance.Role != role)
                Add("cash-forecast.evidence-role-invalid", "Projection, observed cash and declared FX evidence roles cannot substitute for one another.", id);
            if (provenance.Evidence.RetainedAtUtc > inputs.RecordedAtUtc)
                Add("cash-forecast.evidence-not-yet-retained", "Inputs must already have been retained at the forecast recording time.", id);
        }

        decimal? Convert(decimal amount, string currency, DateOnly date, string id)
        {
            if (!IsCurrency(currency) || !IsCurrency(scope.BaseCurrency))
                return null;
            if (currency == scope.BaseCurrency)
                return Round(amount, scope.BaseCurrency);
            var rates = inputs.FxConversions.Where(row => row.Currency == currency && row.BaseCurrency == scope.BaseCurrency && row.ConversionDate == date).ToArray();
            if (rates.Length != 1 || rates[0].BaseUnitsPerCurrencyUnit <= 0m)
            {
                Add("cash-forecast.fx-required", $"A declared {currency}-to-{scope.BaseCurrency} conversion on {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is required.", id);
                return null;
            }
            return Round(checked(amount * rates[0].BaseUnitsPerCurrencyUnit), scope.BaseCurrency);
        }
    }

    public static bool IsCompleteScope(AccountCashForecastScopeDto scope) =>
        new[] { scope.TenantId, scope.CompanyId, scope.AccountId, scope.BookId }
            .All(static value => !string.IsNullOrWhiteSpace(value) && value == value.Trim()) && IsCurrency(scope.BaseCurrency);

    private static AccountCashForecastRequestDto Canonicalize(AccountCashForecastRequestDto request)
    {
        // Copy before sorting so caller-owned mutable collections cannot change retained evidence.
        var copy = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(request,
            AccountCashForecastJsonContext.Default.AccountCashForecastRequestDto), AccountCashForecastJsonContext.Default.AccountCashForecastRequestDto)!;
        if (copy.Scope is null || copy.Flows is null || copy.FxConversions is null
            || copy.Flows.Any(static row => row is null || row.Provenance is null || row.Provenance.Evidence is null)
            || copy.FxConversions.Any(static row => row is null || row.Provenance is null || row.Provenance.Evidence is null)
            || copy.OpeningCash is { Provenance: null } || copy.OpeningCash?.Provenance is { Evidence: null }
            || copy.ClosingCash is { Provenance: null } || copy.ClosingCash?.Provenance is { Evidence: null })
            throw new ArgumentException("Forecast inputs and source provenance collections must be complete.", nameof(request));
        return copy with
        {
            Flows = copy.Flows.OrderBy(static row => row.FlowId, StringComparer.Ordinal).ThenBy(static row => row.Status)
                .ThenBy(static row => row.VersionId, StringComparer.Ordinal).ToArray(),
            FxConversions = copy.FxConversions.OrderBy(static row => row.Currency, StringComparer.Ordinal)
                .ThenBy(static row => row.BaseCurrency, StringComparer.Ordinal).ThenBy(static row => row.ConversionDate).ToArray()
        };
    }

    private static bool IsCurrency(string? currency) => currency is { Length: 3 } && currency.All(static ch => ch is >= 'A' and <= 'Z');
    private static decimal Round(decimal amount, string currency) => decimal.Round(amount, CurrencyMinorUnits(currency), MidpointRounding.AwayFromZero);
    private static int CurrencyMinorUnits(string currency) => currency switch
    {
        "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
        "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or "RWF" or "UGX" or "UYI" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
        _ => 2
    };
    private static string ResolveLane(string flowType) => flowType switch
    {
        "Coupon" or "Interest" => "Coupons & interest",
        "Maturity" or "Principal" or "FactorPaydown" => "Maturities & principal",
        "UnsettledTrade" => "Trade settlements",
        _ => "Other"
    };
}
