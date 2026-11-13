using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Retained collection evidence and watch information; never posting or payment authority.</summary>
public static class BondMonitoringEngine
{
    public const string LegacyEngineVersion = "bond-monitoring-v1";
    public const string PreviousEngineVersion = "bond-monitoring-v2";
    public const string EngineVersion = "bond-monitoring-v3";

    private static readonly string[] LegacyRenderingCultures = ["", "en-US", "fr-FR", "de-DE", "th-TH", "ar-SA", "fa-IR"];

    public static BondMonitoringSnapshotDto Build(BondMonitoringCaptureRequest request) => Build(request, EngineVersion);

    public static BondMonitoringSnapshotDto Build(BondMonitoringCaptureRequest request, string? engineVersion)
        => Build(request, engineVersion, CultureInfo.CurrentCulture);

    internal static BondMonitoringSnapshotDto Build(BondMonitoringCaptureRequest request, string? engineVersion,
        CultureInfo renderingCulture)
    {
        ArgumentNullException.ThrowIfNull(request);
        var version = engineVersion ?? EngineVersion;
        if (version is not (LegacyEngineVersion or PreviousEngineVersion or EngineVersion))
            throw new ArgumentException("The requested bond monitoring engine is not retained.", nameof(engineVersion));
        var culture = version == EngineVersion ? CultureInfo.InvariantCulture : renderingCulture;
        var legacy = version == LegacyEngineVersion;
        var inputs = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(request,
            BondMonitoringJsonContext.Default.BondMonitoringCaptureRequest), BondMonitoringJsonContext.Default.BondMonitoringCaptureRequest)!;
        if (inputs.Scope is null || inputs.Obligations is null || inputs.Payments is null || inputs.Allocations is null
            || inputs.ReceiptRevisions is null || inputs.Observations is null || inputs.Resets is null || inputs.ExerciseWindows is null
            || inputs.Obligations.Any(row => row is null || row.Inputs is null)
            || inputs.Payments.Any(row => row is null || row.Provenance?.Evidence is null)
            || inputs.Allocations.Any(row => row is null) || inputs.ReceiptRevisions.Any(row => row is null || row.Evidence?.Evidence is null)
            || inputs.Observations.Any(row => row is null || row.Provenance?.Evidence is null)
            || inputs.Resets.Any(row => row is null || row.Inputs is null))
            throw new ArgumentException("Monitoring collections and retained source identities are required.", nameof(request));
        inputs = inputs with
        {
            Obligations = inputs.Obligations.OrderBy(row => row.ObligationId, StringComparer.Ordinal).ToArray(),
            Payments = inputs.Payments.OrderBy(row => row.VersionId, StringComparer.Ordinal).ToArray(),
            Allocations = inputs.Allocations.OrderBy(row => row.AllocationId, StringComparer.Ordinal).ToArray(),
            ReceiptRevisions = inputs.ReceiptRevisions.OrderBy(row => row.ExpectedFlowId, StringComparer.Ordinal).ToArray(),
            Observations = inputs.Observations.OrderBy(row => row.ObservationId, StringComparer.Ordinal).ToArray(),
            Resets = inputs.Resets.OrderBy(row => row.SecurityId).ThenBy(row => row.AccrualDate).ThenBy(row => row.ObservationDate).ToArray(),
            MarketWatch = BondMonitoringMarketWatch.Canonicalize(inputs.MarketWatch)
        };
        if (!AccountCashForecastEngine.IsCompleteScope(inputs.Scope) || string.IsNullOrWhiteSpace(inputs.RunId)
            || inputs.RecordedAtUtc == default || inputs.RecordedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Exact scope, run identity and UTC recording time are required.", nameof(request));
        if (inputs.PreviousSnapshotId is not null && string.IsNullOrWhiteSpace(inputs.CorrectionRationale))
            throw new ArgumentException("A monitoring correction must retain its rationale.", nameof(request));
        var issues = new List<AccountCashForecastIssueDto>();
        var today = DateOnly.FromDateTime(inputs.RecordedAtUtc.UtcDateTime);
        Unique(inputs.Obligations.Select(row => row.ObligationId), "obligation");
        Unique(inputs.Payments.Select(row => row.VersionId), "payment");
        Unique(inputs.Allocations.Select(row => row.AllocationId), "allocation");
        Unique(inputs.Observations.Select(row => row.ObservationId), "observation");
        Unique(inputs.ReceiptRevisions.Select(row => row.ExpectedFlowId), "receipt-revision");
        ValidateChains(inputs.Payments.Select(row => (row.VersionId, row.SupersedesVersionId)), "payment");
        ValidateChains(inputs.Observations.Select(row => (row.ObservationId, row.SupersedesObservationId)), "observation");
        foreach (var payment in inputs.Payments.Where(row => row.SupersedesVersionId is not null))
            if (!inputs.Payments.Any(prior => prior.VersionId == payment.SupersedesVersionId && prior.FlowId == payment.FlowId
                && prior.AccountId == payment.AccountId && prior.BookId == payment.BookId && prior.SecurityId == payment.SecurityId
                && prior.Currency == payment.Currency && prior.FlowType == payment.FlowType))
                Add("bond-monitoring.payment-revision-invalid", "A corrected receipt must link an earlier retained version of the exact same cash movement.", payment.VersionId);
        var activePayments = SelectActivePayments(inputs.Payments);
        foreach (var group in activePayments.GroupBy(row => row.FlowId).Where(group => group.Count() > 1))
            Add("bond-monitoring.payment-revision-ambiguous", "Select one current revision of each observed cash movement.", group.Key);
        foreach (var obligation in inputs.Obligations)
        {
            if (obligation.SecurityId == Guid.Empty || obligation.DueDate == default || !Currency(obligation.Currency)
                || obligation.Amount != LedgerCurrencyRounding.RoundCurrency(obligation.Amount, obligation.Currency)
                || string.IsNullOrWhiteSpace(obligation.FlowType) || string.IsNullOrWhiteSpace(obligation.ModelVersion)
                || obligation.Inputs.Count == 0 || obligation.Inputs.Any(row => row is null || string.IsNullOrWhiteSpace(row.Key)
                    || (row.ContentSha256 is { } digest && !Sha256Digest.IsCanonical(digest))
                    || row.RecordedAt > inputs.RecordedAtUtc))
                Add("bond-monitoring.obligation-incomplete", "Obligations require signed retained model economics, currency, dates and input identities.", obligation.ObligationId);
        }
        foreach (var payment in inputs.Payments)
        {
            Evidence(payment.Provenance, payment.VersionId);
            if (payment.Status != AccountCashFlowStatus.Actual || payment.AccountId != inputs.Scope.AccountId
                || payment.BookId != inputs.Scope.BookId || payment.SecurityId is null || !Currency(payment.Currency)
                || payment.Amount != LedgerCurrencyRounding.RoundCurrency(payment.Amount, payment.Currency)
                || payment.ProjectionRunId is not null || payment.CashReceiptDate is null || payment.EconomicDate is null
                || payment.SettlementDate is null || payment.CashReceiptDate > today
                || payment.CashReceiptDate > DateOnly.FromDateTime(payment.Provenance.Evidence.RetainedAtUtc.UtcDateTime)
                || payment.Provenance.Evidence.EffectiveDate != payment.EconomicDate)
                Add("bond-monitoring.payment-invalid", "Payments must be already observed signed cash in the exact account, book and security scope.", payment.VersionId);
        }
        foreach (var allocation in inputs.Allocations)
        {
            var obligations = inputs.Obligations.Where(row => row.ObligationId == allocation.ExpectedFlowId).ToArray();
            var receipts = activePayments.Where(row => row.VersionId == allocation.ActualVersionId).ToArray();
            if (obligations.Length != 1 || receipts.Length != 1)
            { Add("bond-monitoring.allocation-source-missing", "Allocations require exactly one retained obligation and receipt.", allocation.AllocationId); continue; }
            var obligation = obligations[0];
            var payment = receipts[0];
            if (obligation.SecurityId != payment.SecurityId || obligation.Currency != payment.Currency || obligation.FlowType != payment.FlowType)
                Add("bond-monitoring.allocation-scope-mismatch", "Cash may only settle the same security, currency and obligation type.", allocation.AllocationId);
            if (allocation.NetAmount == 0m || allocation.GrossAmount == 0m
                || Math.Sign(allocation.NetAmount) != Math.Sign(payment.Amount)
                || Math.Sign(allocation.GrossAmount) != Math.Sign(obligation.Amount)
                || allocation.GrossAmount != allocation.NetAmount + allocation.WithholdingTax + allocation.Fees
                || allocation.WithholdingTax < 0m || allocation.Fees < 0m)
                Add("bond-monitoring.allocation-amount-invalid", "Gross equals signed net cash plus nonnegative evidenced tax and fee cash costs.", allocation.AllocationId);
            if (allocation.WithholdingTax != 0m || allocation.Fees != 0m)
                Evidence(allocation.DeductionEvidence, allocation.AllocationId);
        }
        foreach (var payment in activePayments)
        {
            var allocated = inputs.Allocations.Where(row => row.ActualVersionId == payment.VersionId).Sum(row => row.NetAmount);
            if (Math.Abs(allocated) > Math.Abs(payment.Amount))
                Add("bond-monitoring.payment-overallocated", "Observed cash cannot be reused or allocated above its retained amount.", payment.VersionId);
        }
        foreach (var revision in inputs.ReceiptRevisions)
        {
            Evidence(revision.Evidence, revision.ExpectedFlowId);
            var obligation = inputs.Obligations.FirstOrDefault(row => row.ObligationId == revision.ExpectedFlowId);
            if (obligation is null || (legacy ? revision.ReceiptDate < today : revision.ReceiptDate == default))
                Add("bond-monitoring.receipt-revision-invalid", legacy
                    ? "A revised receipt date requires an existing obligation and a current or future date."
                    : "A revised receipt date requires an existing obligation and an explicit receipt date.", revision.ExpectedFlowId);
        }
        foreach (var observation in inputs.Observations)
        {
            Evidence(observation.Provenance, observation.ObservationId);
            if (observation.SecurityId == Guid.Empty || observation.EffectiveDate == default || string.IsNullOrWhiteSpace(observation.Value)
                || observation.Kind is not ("Rating" or "RatingWithdrawal" or "IssuerCallNotice" or "NoticeCancellation" or "Factor" or "Fixing" or "FixingRevision"))
                Add("bond-monitoring.observation-invalid", "Watch observations require a supported kind and dated security evidence.", observation.ObservationId);
            if (observation.Kind is "Rating" or "RatingWithdrawal" && string.IsNullOrWhiteSpace(observation.Agency))
                Add("bond-monitoring.rating-agency-required", "Ratings remain distinct by agency.", observation.ObservationId);
            if (observation.SupersedesObservationId is { } previous && !inputs.Observations.Any(row => row.ObservationId == previous
                    && row.SecurityId == observation.SecurityId && row.Agency == observation.Agency && row.ObservationId != observation.ObservationId
                    && ObservationFamily(row.Kind) == ObservationFamily(observation.Kind)))
                Add("bond-monitoring.supersession-invalid", "Superseding evidence must retain the preceding security and agency observation.", observation.ObservationId);
        }
        var collection = inputs.Obligations.Select(obligation =>
        {
            var allocated = inputs.Allocations.Where(row => row.ExpectedFlowId == obligation.ObligationId).ToArray();
            var gross = allocated.Sum(row => row.GrossAmount);
            var residual = checked(obligation.Amount - gross);
            if (gross != 0m && (Math.Sign(gross) != Math.Sign(obligation.Amount) || Math.Abs(gross) > Math.Abs(obligation.Amount)))
                Add("bond-monitoring.obligation-overallocated", "Allocated gross cash exceeds the signed obligation.", obligation.ObligationId);
            var revision = inputs.ReceiptRevisions.FirstOrDefault(row => row.ExpectedFlowId == obligation.ObligationId);
            var receiptDate = revision?.ReceiptDate ?? obligation.DueDate;
            var overdue = residual != 0m && (legacy ? obligation.DueDate < today && revision is null : receiptDate < today);
            var unexplained = activePayments.Any(payment => payment.SecurityId == obligation.SecurityId
                && payment.Currency == obligation.Currency && payment.FlowType == obligation.FlowType
                && payment.Amount != inputs.Allocations.Where(row => row.ActualVersionId == payment.VersionId).Sum(row => row.NetAmount));
            return new BondCollectionStatusDto(obligation.ObligationId, obligation.SecurityId, obligation.FlowType,
                obligation.DueDate, obligation.Amount, allocated.Sum(row => row.NetAmount), gross,
                allocated.Sum(row => row.WithholdingTax), allocated.Sum(row => row.Fees), residual,
                obligation.Currency, residual == 0m ? "Collected" : unexplained ? "Unexplained" : overdue ? "Overdue" : gross != 0m ? "Partial" : "Expected",
                residual == 0m ? 0 : Math.Max(0, today.DayNumber - (legacy ? obligation.DueDate : receiptDate).DayNumber), revision?.ReceiptDate,
                residual != 0m && !overdue && !unexplained && obligation.IsCurrentHolding, allocated.Select(row => row.ActualVersionId).Distinct().ToArray());
        }).ToArray();
        var watch = inputs.Obligations.Where(row => row.IsCurrentHolding && row.DueDate >= today).Select(row => new BondWatchItemDto(
            $"due:{row.ObligationId}", row.SecurityId, row.FlowType, row.DueDate,
            Render($"{row.FlowType}: {row.Amount} {row.Currency}{(row.IsForecast ? " (forecast)" : "")}"), null,
            row.ModelVersion, row.IsForecast, [row.ObligationId])).ToList();
        foreach (var observation in inputs.Observations.Where(row => !inputs.Observations.Any(next => next.SupersedesObservationId == row.ObservationId)))
        {
            var affected = inputs.Obligations.Where(row => row.SecurityId == observation.SecurityId
                && (row.DueDate >= observation.EffectiveDate || observation.Kind is "Rating" or "RatingWithdrawal"))
                .Select(row => row.ObligationId).ToArray();
            watch.Add(new(observation.ObservationId, observation.SecurityId, observation.Kind, observation.EffectiveDate,
                observation.Value, observation.Agency, observation.Provenance.SourceDomain, true, affected));
        }
        foreach (var reset in inputs.Resets)
        {
            if (reset.SecurityId == Guid.Empty || reset.AccrualDate == default || reset.ObservationDate == default
                || string.IsNullOrWhiteSpace(reset.Source) || reset.Inputs.Count == 0
                || reset.Inputs.Any(row => row.RecordedAt > inputs.RecordedAtUtc || row.ContentSha256 is { } digest && !Sha256Digest.IsCanonical(digest))
                || reset.IsObserved && reset.ObservationDate > today
                || reset.CouponPaymentDate.HasValue != reset.CouponAmount.HasValue
                || reset.CouponPaymentDate == default(DateOnly) || reset.PreviousCouponAmount is not null && reset.CouponPaymentDate is null)
                Add("bond-monitoring.reset-evidence-invalid", "Reset monitoring requires dated retained observed or forecast rate inputs.", reset.SecurityId.ToString("D"));
            watch.Add(new(Render($"reset:{reset.SecurityId:D}:{reset.AccrualDate:yyyy-MM-dd}:{reset.ObservationDate:yyyy-MM-dd}"),
                reset.SecurityId, reset.IsObserved ? "ObservedReset" : "ForecastReset", reset.ObservationDate,
                Render($"{(reset.IsObserved ? "Observed" : "Forecast")} index {reset.Rate:P6}; accrual {reset.AccrualDate:yyyy-MM-dd}{(reset.PreviousRate is { } priorRate ? Render($"; prior index {priorRate:P6}") : "")}"), null,
                reset.Source, !reset.IsObserved, inputs.Obligations.Where(row => row.SecurityId == reset.SecurityId && row.DueDate >= reset.AccrualDate)
                    .Select(row => row.ObligationId).ToArray()));
        }
        foreach (var coupon in inputs.Resets.Where(row => row.CouponPaymentDate is not null)
            .GroupBy(row => (row.SecurityId, Payment: row.CouponPaymentDate!.Value)))
        {
            var amounts = coupon.Select(row => row.CouponAmount).Distinct().ToArray();
            var priorAmounts = coupon.Select(row => row.PreviousCouponAmount).Distinct().ToArray();
            if (amounts.Length != 1 || priorAmounts.Length != 1)
            {
                Add("bond-monitoring.coupon-revision-ambiguous", "Daily resets for one payment must retain the same whole-coupon current and prior estimates.", coupon.Key.SecurityId.ToString("D"));
                continue;
            }
            var amount = amounts[0]!.Value;
            var priorAmount = priorAmounts[0];
            var revised = priorAmount is { } prior && prior != amount;
            var affected = inputs.Obligations.Where(row => row.IsCurrentHolding && row.SecurityId == coupon.Key.SecurityId
                && row.FlowType == "Coupon" && row.DueDate == coupon.Key.Payment).ToArray();
            var currency = affected.Select(row => row.Currency).Distinct().SingleOrDefault() ?? "native currency";
            watch.Add(new(Render($"coupon-estimate:{coupon.Key.SecurityId:D}:{coupon.Key.Payment:yyyy-MM-dd}"), coupon.Key.SecurityId,
                revised ? "CouponRevision" : "NextCoupon", coupon.Key.Payment,
                Render($"Whole coupon {amount} {currency} on {coupon.Key.Payment:yyyy-MM-dd}; ")
                    + (priorAmount is { } baseline ? Render($"prior {baseline}; total change {amount - baseline}.") : "previous estimate unavailable.")
                    + " Change reflects all refreshed model and holding inputs; daily reset effects are not added repeatedly.",
                null, string.Join(", ", coupon.Select(row => row.Source).Distinct().Order(StringComparer.Ordinal)),
                revised || coupon.Any(row => !row.IsObserved), affected.Select(row => row.ObligationId).ToArray()));
        }
        watch.AddRange(BondMonitoringMarketWatch.Build(inputs, Add));
        foreach (var exercise in inputs.ExerciseWindows)
        {
            foreach (var blocker in exercise.Blockers)
                watch.Add(new($"exercise-blocker:{exercise.SecurityId:D}:{blocker.Code}", exercise.SecurityId, "ExerciseInputMissing",
                    exercise.AsOf, blocker.Reason, null, "SecurityMaster", true, []));
            foreach (var window in exercise.Windows)
                watch.Add(new(Render($"exercise:{exercise.SecurityId:D}:{window.Kind}:{window.ExerciseDate:yyyy-MM-dd}"), exercise.SecurityId,
                    "ExerciseEligibility", window.LatestNoticeDate ?? window.ExerciseDate,
                    Render($"{window.Kind} eligibility {window.ExerciseDate:yyyy-MM-dd} at {window.RedemptionPrice}; notice {window.Status}. Eligibility is not an observed exercise notice."),
                    null, "SecurityMaster", window.Status is BondExerciseNoticeStatus.Open or BondExerciseNoticeStatus.DeadlineElapsed,
                    inputs.Obligations.Where(row => row.SecurityId == exercise.SecurityId && row.DueDate >= window.ExerciseDate).Select(row => row.ObligationId).ToArray()));
        }
        foreach (var payment in activePayments)
        {
            var allocated = inputs.Allocations.Where(row => row.ActualVersionId == payment.VersionId).Sum(row => row.NetAmount);
            if (allocated != payment.Amount)
                watch.Add(new($"unallocated:{payment.VersionId}", payment.SecurityId ?? Guid.Empty, "UnallocatedCash",
                    payment.CashReceiptDate ?? today, Render($"Unallocated observed cash {payment.Amount - allocated} {payment.Currency}"),
                    null, payment.Provenance.SourceDomain, true, []));
        }
        var hash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(inputs, BondMonitoringJsonContext.Default.BondMonitoringCaptureRequest));
        return new(Sha256Digest.ComputeUtf8($"{version}|{hash}"), version, inputs, collection,
            watch.OrderBy(row => row.Date).ThenBy(row => row.WatchId, StringComparer.Ordinal).ToArray(), issues);

        string Render(FormattableString text) => text.ToString(culture);

        void Add(string code, string message, string? id = null) => issues.Add(new(code, message, id));
        void Unique(IEnumerable<string> keys, string category)
        {
            foreach (var group in keys.GroupBy(row => row, StringComparer.Ordinal).Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
                Add($"bond-monitoring.{category}-identity-invalid", "Retained identities must be nonempty and unique.", group.Key);
        }
        void ValidateChains(IEnumerable<(string Id, string? Parent)> rows, string category)
        {
            var array = rows.ToArray();
            foreach (var fork in array.Where(row => row.Parent is not null).GroupBy(row => row.Parent).Where(group => group.Count() > 1))
                Add($"bond-monitoring.{category}-revision-fork", "A retained source version cannot have competing selected successors.", fork.Key);
            foreach (var start in array)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var current = start;
                while (true)
                {
                    if (!seen.Add(current.Id))
                    { Add($"bond-monitoring.{category}-revision-cycle", "Source revision chains cannot contain cycles.", start.Id); break; }
                    if (current.Parent is null)
                        break;
                    var parents = array.Where(row => row.Id == current.Parent).ToArray();
                    if (parents.Length != 1)
                    { Add($"bond-monitoring.{category}-revision-parent-missing", "Every revision must retain exactly one preceding source version.", current.Id); break; }
                    current = parents[0];
                }
            }
        }
        void Evidence(AccountCashProvenanceDto? source, string id)
        {
            if (source?.Evidence is null || source.Role != AccountCashEvidenceRole.Observation
                || !RetainedEvidenceIdentityValidator.IsComplete(source.Evidence) || !Sha256Digest.IsCanonical(source.SourceContentHash)
                || source.SourceDomain != source.Evidence.SourceSystem || source.SourceEntityId != source.Evidence.SourceReference
                || !Sha256Digest.FixedEquals(source.SourceContentHash, source.Evidence.ContentHashSha256)
                || source.Evidence.RetainedAtUtc > inputs.RecordedAtUtc)
                Add("bond-monitoring.source-evidence-required", "Observations, deductions and revised dates require accepted retained source evidence.", id);
        }
    }

    internal static bool Reproduces(BondMonitoringSnapshotDto retained)
    {
        var serialized = JsonSerializer.Serialize(retained, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDto);
        if (retained.EngineVersion == EngineVersion)
            return Matches(CultureInfo.InvariantCulture);
        if (retained.EngineVersion is not (LegacyEngineVersion or PreviousEngineVersion))
            return false;
        if (Matches(CultureInfo.CurrentCulture))
            return true;
        // Old receipts did not retain their rendering culture. A bounded renderer is admissible
        // only when it reproduces the ENTIRE retained receipt, including all financial fields,
        // watch identities and summaries. No culture is inferred and no stored bytes are changed.
        foreach (var name in LegacyRenderingCultures)
        {
            CultureInfo candidate;
            try
            { candidate = CultureInfo.GetCultureInfo(name); }
            catch (CultureNotFoundException) { continue; }
            if (Matches(candidate))
                return true;
        }
        return false;

        bool Matches(CultureInfo candidate)
        {
            try
            {
                var replay = Build(retained.Inputs, retained.EngineVersion, candidate);
                return serialized == JsonSerializer.Serialize(replay, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDto);
            }
            // Some calendars cannot represent otherwise valid retained Gregorian dates. Such a
            // renderer proves nothing; another bounded candidate must still match every field.
            catch (ArgumentOutOfRangeException) { return false; }
        }
    }

    private static bool Currency(string value) => value is { Length: 3 } && value.All(ch => ch is >= 'A' and <= 'Z');

    public static IReadOnlyList<AccountCashFlowDto> SelectActivePayments(IReadOnlyList<AccountCashFlowDto> payments) =>
        payments.Where(row => !payments.Any(next => next.SupersedesVersionId == row.VersionId)).ToArray();

    private static string ObservationFamily(string kind) => kind switch
    {
        "Rating" or "RatingWithdrawal" => "Rating",
        "IssuerCallNotice" or "NoticeCancellation" => "CallNotice",
        "Fixing" or "FixingRevision" => "Fixing",
        _ => kind
    };
}

public sealed partial class BondMonitoringService(IBondMonitoringStore store,
    IAccountBondHoldingsSource? holdings = null, IBondWorkflowService? bonds = null,
    IBondMarketDataService? market = null) : IBondMonitoringService
{
    public async Task<BondMonitoringSnapshotDto> CaptureAsync(BondMonitoringCaptureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var retained = (await GetHistoryAsync(request.Scope, ct).ConfigureAwait(false))
            .SingleOrDefault(row => row.Inputs.RunId == request.RunId);
        if (retained is not null)
        {
            var replay = BondMonitoringEngine.Build(request, retained.EngineVersion, CultureInfo.InvariantCulture);
            if (JsonSerializer.Serialize(replay.Inputs, BondMonitoringJsonContext.Default.BondMonitoringCaptureRequest)
                != JsonSerializer.Serialize(retained.Inputs, BondMonitoringJsonContext.Default.BondMonitoringCaptureRequest))
                throw new InvalidOperationException("A monitoring run identity cannot be reused with different inputs.");
            return retained;
        }
        var snapshot = BondMonitoringEngine.Build(request);
        if (snapshot.Issues.Count > 0)
            throw new ArgumentException(string.Join(" ", snapshot.Issues.Select(row => row.Message)), nameof(request));
        await VerifyObligationsAsync(snapshot.Inputs, ct).ConfigureAwait(false);
        return await store.AppendAsync(snapshot, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BondMonitoringSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default)
    {
        var history = await store.GetHistoryAsync(scope, ct).ConfigureAwait(false);
        foreach (var snapshot in history)
        {
            if (snapshot.Inputs.Scope != scope || !BondMonitoringEngine.Reproduces(snapshot))
                throw new InvalidDataException("Retained bond monitoring failed exact scope or deterministic replay validation. Legacy localized output must exactly match a supported rendering culture; unsupported custom output requires the original rendering settings before history can be reopened or a new capture created.");
        }
        return history;
    }
}
