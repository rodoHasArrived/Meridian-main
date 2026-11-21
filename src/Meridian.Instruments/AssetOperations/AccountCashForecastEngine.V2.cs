using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Instruments.AssetOperations;

public static partial class AccountCashForecastEngine
{
    public const string LegacyNativeEngineVersion = "account-cash-forecast-v2";
    public const string NativeEngineVersion = "account-cash-forecast-v3";

    private static AccountCashForecastSnapshotDto BuildV2Checked(AccountCashForecastRequestDto request, string engineVersion)
    {
        try
        { return BuildV2(request, engineVersion); }
        catch (OverflowException)
        {
            var inputs = Canonicalize(request);
            var hash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(inputs, AccountCashForecastJsonContext.Default.AccountCashForecastRequestDto));
            var id = Sha256Digest.ComputeUtf8($"{engineVersion}|{hash}");
            var issue = new AccountCashForecastIssueDto("cash-forecast.amount-overflow", "Retained cash amounts exceed supported decimal arithmetic.");
            var ladder = PortfolioCashLadderEngine.BuildFromRetainedContributions(inputs.PeriodStart, inputs.PeriodEnd,
                inputs.Scope.BaseCurrency, 0m, [], inputs.MinimumCashThreshold, inputs.BucketDays, inputs.RecordedAtUtc, [issue.Message]);
            return new(id, id, engineVersion, inputs, null, null, null, null, null, false, ladder, [issue])
            { CurrencyLadders = [], Residuals = [] };
        }
    }

    private static AccountCashForecastSnapshotDto BuildV2(AccountCashForecastRequestDto request, string engineVersion)
    {
        var legacy = engineVersion == LegacyNativeEngineVersion;
        var inputs = Canonicalize(request);
        if (inputs.PeriodStart == default || inputs.PeriodEnd < inputs.PeriodStart || inputs.PeriodEnd == DateOnly.MaxValue
            || inputs.PeriodEnd.DayNumber - inputs.PeriodStart.DayNumber >= 3660 || inputs.BucketDays is < 1 or > 3660)
            throw new ArgumentException("A valid bounded inclusive cash period and bucket size are required.", nameof(request));
        var issues = new List<AccountCashForecastIssueDto>();
        var opening = inputs.NativeOpeningBalances ?? (inputs.OpeningCash is null ? [] : [inputs.OpeningCash]);
        var closing = inputs.NativeClosingBalances ?? (inputs.ClosingCash is null ? [] : [inputs.ClosingCash]);
        var allocations = inputs.PaymentAllocations ?? [];
        var revisions = inputs.ReceiptRevisions ?? [];
        var expected = inputs.Flows.Where(row => row.Status == AccountCashFlowStatus.Expected).ToArray();
        var actual = inputs.Flows.Where(row => row.Status == AccountCashFlowStatus.Actual).ToArray();
        var remaining = new List<AccountCashFlowDto>();
        var residuals = new List<AccountCashResidualDto>();
        var today = DateOnly.FromDateTime(inputs.RecordedAtUtc.UtcDateTime);

        if (!IsCompleteScope(inputs.Scope))
            Add("cash-forecast.scope-required", "Exact tenant, company, account, book and base currency are required.");
        if (opening.Count == 0)
            Add("cash-forecast.opening-required", "Retained opening cash is required for each contributing currency.");
        if (inputs.NativeOpeningBalances is not null && inputs.OpeningCash is not null
            || inputs.NativeClosingBalances is not null && inputs.ClosingCash is not null)
            Add("cash-forecast.balance-source-ambiguous", "Select native balance collections or legacy single balances, without conflicting duplicate sources.");
        if (inputs.Flows.GroupBy(row => row.VersionId).Any(group => group.Count() > 1))
            Add("cash-forecast.version-duplicate", "A retained source flow version can only be selected once.");
        if (expected.GroupBy(row => row.FlowId).Any(group => group.Count() > 1))
            Add("cash-forecast.flow-ambiguous", "Select one retained expectation per stable obligation.");
        if (actual.GroupBy(row => row.FlowId).Any(group => group.Count() > 1))
            Add("cash-forecast.flow-ambiguous", "Separate receipts need distinct cash-flow identities; select one revision of each receipt.");
        if (allocations.GroupBy(row => row.AllocationId).Any(group => group.Count() > 1))
            Add("cash-forecast.allocation-duplicate", "Each receipt allocation must have a unique retained identity.");
        if (revisions.GroupBy(row => row.ExpectedFlowId).Any(group => group.Count() > 1))
            Add("cash-forecast.revision-ambiguous", "Select one evidenced revised receipt date for each obligation.");
        foreach (var allocation in allocations)
        {
            var obligation = expected.SingleOrDefaultIfUnique(row => row.FlowId == allocation.ExpectedFlowId);
            var receipt = actual.SingleOrDefaultIfUnique(row => row.VersionId == allocation.ActualVersionId);
            if (string.IsNullOrWhiteSpace(allocation.AllocationId) || obligation is null || receipt is null)
            { Add("cash-forecast.allocation-source-missing", "Allocations require an exact retained expectation and observed receipt.", allocation.AllocationId); continue; }
            if (obligation.Currency != receipt.Currency || obligation.SecurityId != receipt.SecurityId
                || obligation.AccountId != receipt.AccountId || obligation.BookId != receipt.BookId || obligation.FlowType != receipt.FlowType)
                Add("cash-forecast.allocation-scope-mismatch", "Allocated receipts must match the obligation's account, book, security, currency and cash type.", allocation.AllocationId);
            if (allocation.NetAmount == 0m || Math.Sign(allocation.NetAmount) != Math.Sign(receipt.Amount)
                || allocation.GrossAmount == 0m || Math.Sign(allocation.GrossAmount) != Math.Sign(obligation.Amount)
                || allocation.GrossAmount != allocation.NetAmount + allocation.WithholdingTax + allocation.Fees
                || allocation.WithholdingTax < 0m || allocation.Fees < 0m)
                Add("cash-forecast.allocation-amount-invalid", "Signed gross must equal net cash plus nonnegative evidenced tax and fee cash costs.", allocation.AllocationId);
            if (Sha256Digest.FixedEquals(obligation.Provenance.SourceContentHash, receipt.Provenance.SourceContentHash))
                Add("cash-forecast.actual-independent-source-required", "Projected obligations and observed receipts require independent retained sources.", allocation.AllocationId);
            if (allocation.WithholdingTax != 0m || allocation.Fees != 0m)
                ValidateAdditionalEvidence(allocation.DeductionEvidence, allocation.AllocationId);
        }
        foreach (var receipt in actual)
        {
            var allocated = allocations.Where(row => row.ActualVersionId == receipt.VersionId).Sum(row => row.NetAmount);
            if (Math.Abs(allocated) > Math.Abs(receipt.Amount))
                Add("cash-forecast.receipt-overallocated", "Receipt cash cannot be allocated more than once or beyond its observed amount.", receipt.VersionId);
        }
        foreach (var revision in revisions)
        {
            var obligation = expected.FirstOrDefault(row => row.FlowId == revision.ExpectedFlowId);
            if (obligation is null || (legacy ? revision.ReceiptDate < today : revision.ReceiptDate == default))
                Add("cash-forecast.receipt-revision-invalid", legacy
                    ? "Revised receipt dates require an existing obligation and cannot be in the past."
                    : "Revised receipt dates require an existing obligation and an explicit receipt date.", revision.ExpectedFlowId);
            ValidateAdditionalEvidence(revision.Evidence, revision.ExpectedFlowId);
        }
        foreach (var obligation in expected)
        {
            var paid = allocations.Where(row => row.ExpectedFlowId == obligation.FlowId).Sum(row => row.GrossAmount);
            var residual = checked(obligation.Amount - paid);
            if (paid != 0m && (Math.Sign(paid) != Math.Sign(obligation.Amount) || Math.Abs(paid) > Math.Abs(obligation.Amount)))
                Add("cash-forecast.obligation-overallocated", "Allocated gross cash exceeds the signed contractual obligation.", obligation.FlowId);
            var revision = revisions.FirstOrDefault(row => row.ExpectedFlowId == obligation.FlowId);
            var receiptDate = revision?.ReceiptDate ?? obligation.CashReceiptDate;
            var overdue = residual != 0m && (legacy ? obligation.CashReceiptDate < today && revision is null : receiptDate < today);
            var unallocated = actual.Any(receipt => receipt.SecurityId == obligation.SecurityId && receipt.Currency == obligation.Currency
                && receipt.FlowType == obligation.FlowType && receipt.Amount != allocations.Where(row => row.ActualVersionId == receipt.VersionId).Sum(row => row.NetAmount));
            if (unallocated && residual != 0m)
                Add("cash-forecast.allocation-unresolved", "Related observed cash must be allocated before treating remaining contractual cash as additional spendable cash.", obligation.FlowId);
            var include = residual != 0m && !overdue && !unallocated && receiptDate >= inputs.PeriodStart && receiptDate <= inputs.PeriodEnd;
            residuals.Add(new(obligation.FlowId, obligation.Amount, paid, residual, obligation.Currency,
                obligation.CashReceiptDate, revision?.ReceiptDate, include,
                residual == 0m ? "Collected" : unallocated ? "Unexplained" : overdue ? "Overdue" : paid != 0m ? "Partial" : "Expected"));
            if (include)
                remaining.Add(obligation with { FlowId = $"residual:{obligation.FlowId}", Amount = residual, CashReceiptDate = receiptDate });
        }
        // Actual cash is included once, independently of how many obligations a receipt settles.
        var effective = actual.Select(row => row with { FlowId = $"receipt:{row.VersionId}" }).Concat(remaining).ToArray();
        var currencies = opening.Select(row => row.Currency).Concat(closing.Select(row => row.Currency)).Concat(inputs.Flows.Select(row => row.Currency))
            .Distinct(StringComparer.Ordinal).OrderBy(row => row, StringComparer.Ordinal).ToArray();
        var native = new List<AccountCashForecastSnapshotDto>();
        foreach (var currency in currencies)
        {
            var starts = opening.Where(row => row.Currency == currency).ToArray();
            var ends = closing.Where(row => row.Currency == currency).ToArray();
            if (starts.Length != 1 || ends.Length > 1)
                Add("cash-forecast.balance-ambiguous", "Each currency requires exactly one opening and at most one closing balance.", currency);
            var nativeInput = new AccountCashForecastRequestDto(inputs.RunId, inputs.Scope with { BaseCurrency = currency },
                inputs.PeriodStart, inputs.PeriodEnd, starts.FirstOrDefault(), ends.FirstOrDefault(),
                effective.Where(row => row.Currency == currency).ToArray(), [], inputs.RecordedAtUtc,
                MinimumCashThreshold: currency == inputs.Scope.BaseCurrency ? inputs.MinimumCashThreshold : 0m, BucketDays: inputs.BucketDays);
            var result = BuildV1(nativeInput);
            native.Add(result);
            issues.AddRange(result.Blockers.Where(row => row.Code != "cash-forecast.closing-required"));
        }
        // Validate every retained expectation, including overdue rows omitted from spendable cash.
        foreach (var currency in inputs.Flows.Select(row => row.Currency).Distinct())
        {
            var starts = opening.FirstOrDefault(row => row.Currency == currency);
            var validation = BuildV1(new(inputs.RunId, inputs.Scope with { BaseCurrency = currency }, inputs.PeriodStart,
                inputs.PeriodEnd, starts, null, inputs.Flows.Where(row => row.Currency == currency)
                    .Select(row => row with { FlowId = $"validate:{row.VersionId}" }).ToArray(), [], inputs.RecordedAtUtc));
            issues.AddRange(validation.Blockers.Where(row => row.Code != "cash-forecast.closing-required"));
        }
        foreach (var fx in inputs.FxConversions)
        {
            ValidateAdditionalEvidence(fx.Provenance, fx.Currency, AccountCashEvidenceRole.DeclaredFx);
            if (fx.Provenance.Evidence.EffectiveDate != fx.ConversionDate)
                Add("cash-forecast.fx-evidence-date-mismatch", "FX evidence must retain the exact declared conversion date.", fx.Currency);
        }
        var baseRows = new List<PortfolioCashLadderContributionDto>();
        decimal? openingBase = 0m, closingBase = closing.Count > 0 ? 0m : null, actualBase = 0m;
        foreach (var balance in opening)
            openingBase = AddNullable(openingBase, ConvertBase(balance.Amount, balance.Currency, inputs.PeriodStart, "opening"));
        foreach (var balance in closing)
            closingBase = AddNullable(closingBase, ConvertBase(balance.Amount, balance.Currency, inputs.PeriodEnd, "closing"));
        foreach (var flow in actual.Where(row => row.CashReceiptDate >= inputs.PeriodStart && row.CashReceiptDate <= inputs.PeriodEnd))
            actualBase = AddNullable(actualBase, ConvertBase(flow.Amount, flow.Currency, flow.CashReceiptDate!.Value, flow.VersionId));
        foreach (var ladder in native.Select(row => row.ForecastLadder))
            foreach (var row in ladder.Contributions)
                if (ConvertBase(row.Amount, row.Currency, row.DueDate, row.SourceEntityId ?? row.DisplayName) is { } amount)
                    baseRows.Add(row with { BaseAmount = amount, Amount = amount, Currency = inputs.Scope.BaseCurrency });
        var blockers = issues.Distinct().ToArray();
        var baseLadder = PortfolioCashLadderEngine.BuildFromRetainedContributions(inputs.PeriodStart, inputs.PeriodEnd,
            inputs.Scope.BaseCurrency, openingBase ?? 0m, baseRows, inputs.MinimumCashThreshold, inputs.BucketDays,
            inputs.RecordedAtUtc, blockers.Select(row => row.Message).Distinct().ToArray());
        var reconciled = openingBase is { } ob && actualBase is { } ab ? ob + ab : (decimal?)null;
        var variance = reconciled is { } rb && closingBase is { } cb ? rb - cb : (decimal?)null;
        var unpaidAtClosing = closing.Count > 0 && residuals.Any(row => row.RemainingAmount != 0m
            && row.OriginalReceiptDate >= inputs.PeriodStart && row.OriginalReceiptDate <= inputs.PeriodEnd);
        if (unpaidAtClosing)
            Add("cash-forecast.expected-unreceived", "Contractual cash remains unreceived at the retained closing boundary.");
        if (closing.Count == 0)
            Add("cash-forecast.closing-required", "Observed closing cash is required for cash reconciliation certification.");
        var certified = blockers.Length == 0 && !unpaidAtClosing && native.Count > 0 && native.All(row => row.IsCertified) && variance == 0m;
        var hash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(inputs, AccountCashForecastJsonContext.Default.AccountCashForecastRequestDto));
        var identity = Sha256Digest.ComputeUtf8($"{engineVersion}|{hash}");
        return new(identity, identity, engineVersion, inputs, openingBase, actualBase, reconciled, closingBase, variance,
            certified, baseLadder, issues.Distinct().ToArray())
        { CurrencyLadders = native.Select(row => row.ForecastLadder).ToArray(), Residuals = residuals };

        void Add(string code, string message, string? id = null) => issues.Add(new(code, message, id));
        void ValidateAdditionalEvidence(AccountCashProvenanceDto? source, string id, AccountCashEvidenceRole role = AccountCashEvidenceRole.Observation)
        {
            if (source is null || source.Role != role || !Sha256Digest.IsCanonical(source.SourceContentHash)
                || !RetainedEvidenceIdentityValidator.IsComplete(source.Evidence) || source.Evidence.RetainedAtUtc > inputs.RecordedAtUtc
                || source.SourceDomain != source.Evidence.SourceSystem || source.SourceEntityId != source.Evidence.SourceReference
                || !Sha256Digest.FixedEquals(source.SourceContentHash, source.Evidence.ContentHashSha256))
                Add("cash-forecast.provenance-incomplete", "Deductions, revised dates and FX require exact retained accepted evidence.", id);
        }
        decimal? ConvertBase(decimal amount, string currency, DateOnly date, string id)
        {
            if (currency == inputs.Scope.BaseCurrency)
                return Round(amount, currency);
            var rates = inputs.FxConversions.Where(row => row.Currency == currency && row.BaseCurrency == inputs.Scope.BaseCurrency && row.ConversionDate == date).ToArray();
            if (rates.Length != 1 || rates[0].BaseUnitsPerCurrencyUnit <= 0m)
            { Add("cash-forecast.fx-required", $"Exact retained {currency}/{inputs.Scope.BaseCurrency} FX on {date:yyyy-MM-dd} is required for base totals; native ladders remain separate.", id); return null; }
            return Round(checked(amount * rates[0].BaseUnitsPerCurrencyUnit), inputs.Scope.BaseCurrency);
        }
    }

    private static decimal? AddNullable(decimal? left, decimal? right) => left.HasValue && right.HasValue ? checked(left.Value + right.Value) : null;
}

internal static class CashForecastSelection
{
    internal static T? SingleOrDefaultIfUnique<T>(this IEnumerable<T> items, Func<T, bool> predicate) where T : class
    { var matching = items.Where(predicate).Take(2).ToArray(); return matching.Length == 1 ? matching[0] : null; }
}
