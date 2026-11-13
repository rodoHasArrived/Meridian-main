using System.Globalization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Account planning from accepted custodian face and the shared retained bond model.</summary>
public sealed class AccountBondIncomePlanningService(IAccountBondHoldingsSource holdings,
    IBondWorkflowService bonds, IBondMonitoringService monitoring, TimeProvider? clock = null,
    ISecurityMasterQueryService? securities = null, IBusinessDayCalendarSource? calendars = null,
    IBondMarketDataService? market = null) : IAccountBondIncomePlanningService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly string[] LegacyIdentityCultures = ["", "en-US", "fr-FR", "de-DE", "th-TH", "ar-SA", "fa-IR"];

    public async Task<AccountBondIncomePlanDto> PlanAsync(AccountBondIncomePlanningRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AccountCashForecastEngine.IsCompleteScope(request.Scope) || !Guid.TryParse(request.Scope.AccountId, out var accountId)
            || request.PeriodStart == default || request.PeriodEnd < request.PeriodStart
            || request.PeriodEnd == DateOnly.MaxValue || request.PeriodEnd.DayNumber - request.PeriodStart.DayNumber > 3660
            || request.BucketDays is < 1 or > 366 || request.OpeningBalances is null || request.FxConversions is null)
            throw new ArgumentException("An exact account/book scope, bounded inclusive dates and cash input collections are required.", nameof(request));
        var generatedAt = _clock.GetUtcNow();
        var knownAt = request.Context.KnownAt ?? generatedAt;
        if (request.Context.AsOf == default || request.Context.AsOf.Offset != TimeSpan.Zero || knownAt.Offset != TimeSpan.Zero
            || knownAt > generatedAt || request.Context.AsOf > generatedAt)
            throw new ArgumentException("Economic and retained-information cutoffs cannot be in the future.", nameof(request));
        var asOf = DateOnly.FromDateTime(request.Context.AsOf.UtcDateTime);
        var snapshot = await holdings.GetHoldingsAsync(accountId, asOf, knownAt, ct).ConfigureAwait(false);
        var history = await monitoring.GetHistoryAsync(request.Scope, ct).ConfigureAwait(false);
        var previous = history.LastOrDefault(row => row.Inputs.RecordedAtUtc <= knownAt);
        var blockers = new List<string>();
        var calculations = new List<BondWorkflowResult>();
        var obligations = new List<BondMonitoringObligationDto>();
        var directions = new Dictionary<Guid, int>();
        var heldSecurities = new HashSet<Guid>();
        var exerciseWindows = new List<BondExerciseNoticeWatchResult>();
        if (snapshot is null)
            blockers.Add("No accepted account holdings snapshot is available at the selected economic and knowledge cutoffs.");
        else
        {
            if (snapshot.AccountId != accountId || snapshot.AsOfDate > asOf || !Sha256Digest.IsCanonical(snapshot.SourceContentHash))
                throw new InvalidDataException("Accepted holdings did not retain the requested account, date and source content identity.");
            foreach (var incomplete in snapshot.Positions.Where(row => row.Bond is null || row.SecurityId is null || row.SecurityId == Guid.Empty))
                blockers.Add($"Holding {incomplete.Identifier} lacks canonical bond identity or explicit face/factor economics; it is excluded from bond calculations.");
            foreach (var group in snapshot.Positions.Where(row => row.Bond is not null && row.SecurityId is { } id && id != Guid.Empty).GroupBy(row => row.SecurityId!.Value))
            {
                var rows = group.ToArray();
                if (rows.Select(row => row.Currency).Distinct().Count() != 1 || rows.Select(row => row.Bond!.PrincipalFactor).Distinct().Count() != 1)
                { blockers.Add($"Security {group.Key} has conflicting holding currencies or factors."); continue; }
                var face = rows.Sum(row => (row.IsShort ? -Math.Abs(row.Bond!.OriginalFace) : row.Bond!.OriginalFace));
                if (rows.Any(row => row.Bond!.OriginalFace != 0m))
                    heldSecurities.Add(group.Key);
                if (face == 0m)
                {
                    if (heldSecurities.Contains(group.Key))
                        blockers.Add(OffsettingHolderEntitlementBlocker(group.Key));
                    continue;
                }
                var result = await bonds.CalculateAsync(new(group.Key, request.Context with { KnownAt = knownAt }, Math.Abs(face)), ct).ConfigureAwait(false);
                if (result is null)
                { blockers.Add($"Security {group.Key} has no retained bond terms."); continue; }
                calculations.Add(result);
                if (securities is not null)
                {
                    var retained = await securities.GetRecordedByIdAsOfAsync(group.Key, knownAt, ct).ConfigureAwait(false);
                    if (retained is not null && retained.Version == result.Context.SecurityVersion)
                        exerciseWindows.Add(BondExerciseNoticeWatch.Evaluate(retained, calendars, asOf,
                            Math.Clamp(request.PeriodEnd.DayNumber - asOf.DayNumber, 1, BondExerciseNoticeWatch.MaxHorizonDays)));
                }
                if (result.Currency != rows[0].Currency)
                { blockers.Add($"Security {group.Key} holding currency disagrees with canonical terms."); continue; }
                if (result.IsBlocked)
                { blockers.AddRange(result.Issues.Where(row => row.BlocksCalculation).Select(row => $"{group.Key}: {row.Reason}")); continue; }
                if (HolderEntitlementBlocker(result) is { } entitlementBlocker)
                    blockers.Add(entitlementBlocker);
                var heldCurrentFace = rows.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.CurrentFace) : row.Bond!.CurrentFace);
                if (result.CurrentFace != Math.Abs(heldCurrentFace))
                { blockers.Add($"Security {group.Key} retained terms factor and accepted holding current face disagree; review principal evidence."); continue; }
                var direction = Math.Sign(face);
                directions[group.Key] = direction;
                var inputs = result.Inputs.Append(new BondCalculationInput("HoldingsSnapshot", snapshot.BatchId.ToString("D"),
                    ContentSha256: snapshot.SourceContentHash, AsOf: new DateTimeOffset(snapshot.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))).ToArray();
                obligations.AddRange(ProjectObligations(request.Scope, group.Key, result, direction, inputs, previous));
            }
        }
        blockers.AddRange(MissingHeldCouponBlockers(previous, obligations, previous?.Inputs.Allocations ?? []));
        // Retain earlier obligations for payments already observed; revised current projections are explicit new evidence.
        foreach (var allocated in previous?.Inputs.Obligations.Where(row => previous.Inputs.Allocations.Any(allocation => allocation.ExpectedFlowId == row.ObligationId)) ?? [])
            if (obligations.Any(row => row.ObligationId == allocated.ObligationId && row.Amount != allocated.Amount))
                blockers.Add($"Holding or forecast changes affect already allocated obligation {allocated.ObligationId}; review collection entitlement before relying on a revised forecast.");
        var merged = obligations.Concat(previous?.Inputs.Obligations.Where(row => !obligations.Any(current => current.ObligationId == row.ObligationId))
                .Select(row => row with { IsCurrentHolding = false }) ?? [])
            .OrderBy(row => row.ObligationId, StringComparer.Ordinal).ToArray();
        var monitorInputs = new BondMonitoringCaptureRequest(Guid.NewGuid().ToString("D"), request.Scope, generatedAt, merged,
            previous?.Inputs.Payments ?? [], previous?.Inputs.Allocations ?? [], previous?.Inputs.ReceiptRevisions ?? [], previous?.Inputs.Observations ?? [],
            previous?.SnapshotId, previous is null ? null : "Refresh expected bond obligations from accepted account holdings and retained model inputs.")
        {
            CalculationContext = request.Context with { KnownAt = knownAt },
            ExerciseWindows = exerciseWindows,
            MarketWatch = market is not null && snapshot is not null
                ? await BondMonitoringMarketWatch.LoadAsync(market, request.Scope.TenantId, snapshot, asOf, knownAt, ct).ConfigureAwait(false)
                : null,
            Resets = ResetInputs(calculations, directions, previous)
        };
        var monitor = BondMonitoringEngine.Build(monitorInputs);
        blockers.AddRange(monitor.Issues.Select(row => row.Message));
        blockers.AddRange(monitor.Watch.Where(row => row.Kind is "FactorConflict" or "FactorSourceConflict" or "MarketDependencyStale")
            .Select(row => $"Security {row.SecurityId}: {row.Kind} requires retained source review and a refreshed calculation before relying on expected cash. {row.Summary}"));
        if (monitor.Collections.Any(row => row.Status == "Unexplained"))
            blockers.Add("Related observed payments remain unallocated; resolve collection attribution before treating contractual residuals as additional spendable cash.");
        var native = new List<PortfolioCashLadderDto>();
        var baseContributions = new List<PortfolioCashLadderContributionDto>();
        var baseBlockers = new List<string>(blockers);
        decimal baseOpening = 0m;
        var currencies = request.OpeningBalances.Select(row => row.Currency).Concat(merged.Select(row => row.Currency))
            .Concat(monitorInputs.Payments.Select(row => row.Currency)).Distinct().OrderBy(row => row, StringComparer.Ordinal);
        foreach (var currency in currencies)
        {
            var currencyBlockers = new List<string>(blockers);
            var openings = request.OpeningBalances.Where(row => row.Currency == currency).ToArray();
            if (openings.Length != 1)
                currencyBlockers.Add($"Exactly one retained {currency} opening settled balance is required.");
            var opening = openings.FirstOrDefault();
            var openingValidation = AccountCashForecastEngine.Build(new("opening-validation", request.Scope with { BaseCurrency = currency },
                request.PeriodStart, request.PeriodEnd, opening, null, [], [], knownAt));
            currencyBlockers.AddRange(openingValidation.Blockers.Where(row => row.Code != "cash-forecast.closing-required").Select(row => row.Message));
            var contributions = new List<PortfolioCashLadderContributionDto>();
            foreach (var receipt in BondMonitoringEngine.SelectActivePayments(monitorInputs.Payments).Where(row => monitor.Issues.Count == 0
                && row.SecurityId is not null && row.Currency == currency && row.CashReceiptDate >= request.PeriodStart && row.CashReceiptDate <= request.PeriodEnd))
                contributions.Add(Contribution(receipt.VersionId, receipt.SecurityId!.Value, receipt.FlowType, receipt.CashReceiptDate!.Value,
                    receipt.Amount, currency, receipt.Provenance.SourceDomain, receipt.Provenance.SourceEntityId));
            foreach (var collection in monitor.Collections.Where(row => row.Currency == currency && row.IncludedInSpendableForecast))
            {
                var date = collection.RevisedReceiptDate ?? collection.DueDate;
                if (date < request.PeriodStart || date > request.PeriodEnd)
                    continue;
                contributions.Add(Contribution(collection.ObligationId, collection.SecurityId, collection.FlowType, date,
                    collection.RemainingAmount, currency, "BondWorkflow", collection.ObligationId));
            }
            native.Add(PortfolioCashLadderEngine.BuildFromRetainedContributions(request.PeriodStart, request.PeriodEnd, currency,
                opening?.Amount ?? 0m, contributions, currency == request.Scope.BaseCurrency ? request.MinimumCashThreshold : 0m,
                request.BucketDays, generatedAt, currencyBlockers));
            baseBlockers.AddRange(currencyBlockers);
            if (opening is not null)
                baseOpening += Convert(opening.Amount, currency, request.PeriodStart);
            foreach (var contribution in contributions)
            {
                var amount = Convert(contribution.Amount, currency, contribution.DueDate);
                baseContributions.Add(contribution with { Amount = amount, BaseAmount = amount, Currency = request.Scope.BaseCurrency });
            }
        }
        if (native.Count == 0)
            baseBlockers.Add("No native currency opening balances or held bond obligations are available.");
        var baseLadder = PortfolioCashLadderEngine.BuildFromRetainedContributions(request.PeriodStart, request.PeriodEnd,
            request.Scope.BaseCurrency, baseOpening, baseContributions, request.MinimumCashThreshold, request.BucketDays,
            generatedAt, baseBlockers.Distinct().ToArray());
        return new(request.Scope, request.PeriodStart, request.PeriodEnd, snapshot?.BatchId, snapshot?.SourceContentHash,
            native, baseLadder, calculations, monitorInputs, baseBlockers.Distinct().ToArray(), generatedAt);

        decimal Convert(decimal amount, string currency, DateOnly date)
        {
            if (currency == request.Scope.BaseCurrency)
                return amount;
            var rates = request.FxConversions.Where(row => row.Currency == currency && row.BaseCurrency == request.Scope.BaseCurrency && row.ConversionDate == date).ToArray();
            if (rates.Length != 1 || rates[0].BaseUnitsPerCurrencyUnit <= 0m || rates[0].Provenance.Role != AccountCashEvidenceRole.DeclaredFx
                || !RetainedEvidenceIdentityValidator.IsComplete(rates[0].Provenance.Evidence)
                || rates[0].Provenance.Evidence.EffectiveDate != date || rates[0].Provenance.Evidence.RetainedAtUtc > knownAt
                || !Sha256Digest.FixedEquals(rates[0].Provenance.SourceContentHash, rates[0].Provenance.Evidence.ContentHashSha256))
            { baseBlockers.Add($"Retained {currency}/{request.Scope.BaseCurrency} FX on {date:yyyy-MM-dd} is missing; native balances remain separate."); return 0m; }
            return LedgerCurrencyRounding.RoundCurrency(checked(amount * rates[0].BaseUnitsPerCurrencyUnit), request.Scope.BaseCurrency);
        }
    }

    public static string ObligationIdentity(AccountCashForecastScopeDto scope, Guid security, string kind, DateOnly payment, DateOnly accrual) =>
        Sha256Digest.ComputeUtf8(FormattableString.Invariant($"{scope.AccountId}|{scope.BookId}|{security:D}|{kind}|{payment:yyyy-MM-dd}|{accrual:yyyy-MM-dd}"));

    internal static string OffsettingHolderEntitlementBlocker(Guid security) =>
        $"Security {security}: offsetting nonzero long and short holdings do not establish retained holder coupon entitlement or a right to set off coupon cash. Reconcile gross account coupon rights before relying on this cash forecast.";

    internal static string? HolderEntitlementBlocker(BondWorkflowResult result)
    {
        if (result.HasDetachedCoupon == true)
            return $"Security {result.SecurityId}: purchaser ex-coupon cash flows do not establish retained holder coupon entitlement. Reconcile account coupon rights before relying on this cash forecast; accepted holding face alone does not prove continuous ownership.";
        var settlement = result.Context.SettlementDate ?? DateOnly.FromDateTime(result.Context.AsOf.UtcDateTime);
        return result.ModelVersion == BondWorkflowModelVersions.Legacy && result.HasDetachedCoupon is null
            && result.CashFlows.Any(flow => flow.PaymentDate > settlement && flow.AccrualStart < flow.AccrualEnd && flow.Interest == 0m)
            ? $"Security {result.SecurityId}: a legacy zero-interest future coupon interval has no retained holder coupon entitlement status. Explicitly refresh with {BondWorkflowModelVersions.Current} or reconcile account coupon rights before relying on this cash forecast."
            : null;
    }

    internal static IEnumerable<string> MissingHeldCouponBlockers(BondMonitoringSnapshotDto? previous,
        IReadOnlyCollection<BondMonitoringObligationDto> current, IReadOnlyList<AccountCashPaymentAllocationDto> allocations)
    {
        if (previous is null)
            yield break;
        foreach (var prior in previous.Inputs.Obligations.Where(row => row.FlowType == "Coupon"
            && !current.Any(item => item.ObligationId == row.ObligationId)))
            if (prior.Amount != allocations.Where(row => row.ExpectedFlowId == prior.ObligationId).Sum(row => row.GrossAmount))
                yield return $"Security {prior.SecurityId}: previously retained uncollected coupon {prior.ObligationId} is absent from the purchaser projection. Reconcile retained holder coupon entitlement before relying on this cash forecast; its earlier obligation and allocations remain visible.";
    }

    private static string RetainedObligationIdentity(AccountCashForecastScopeDto scope, Guid security, string kind,
        DateOnly payment, DateOnly accrual, BondMonitoringSnapshotDto? previous)
    {
        var canonical = ObligationIdentity(scope, security, kind, payment, accrual);
        if (previous is null)
            return canonical;
        if (previous.Inputs.Scope != scope)
            throw new InvalidOperationException("Retained obligation identity requires the exact account/book scope.");
        FormattableString legacy = $"{scope.AccountId}|{scope.BookId}|{security:D}|{kind}|{payment:yyyy-MM-dd}|{accrual:yyyy-MM-dd}";
        var hashes = new HashSet<string>(StringComparer.Ordinal) { canonical };
        try { hashes.Add(Sha256Digest.ComputeUtf8(legacy.ToString(CultureInfo.CurrentCulture))); }
        catch (ArgumentOutOfRangeException) { /* This current calendar cannot represent the contractual date. */ }
        foreach (var name in LegacyIdentityCultures)
        {
            try { hashes.Add(Sha256Digest.ComputeUtf8(legacy.ToString(CultureInfo.GetCultureInfo(name)))); }
            catch (ArgumentOutOfRangeException) { /* This calendar cannot represent this contractual date. */ }
            catch (CultureNotFoundException) { /* An unavailable renderer proves no retained identity. */ }
        }
        var matches = previous.Inputs.Obligations.Where(row => row.SecurityId == security && row.FlowType == kind
            && row.DueDate == payment && hashes.Contains(row.ObligationId)).Select(row => row.ObligationId).Distinct(StringComparer.Ordinal).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException("Competing retained identities for one contractual obligation require explicit source reconciliation.");
        return matches.SingleOrDefault() ?? canonical;
    }

    internal static IReadOnlyList<BondMonitoringObligationDto> ProjectObligations(AccountCashForecastScopeDto scope,
        Guid securityId, BondWorkflowResult result, int direction, IReadOnlyList<BondCalculationInput> inputs, BondMonitoringSnapshotDto? previous = null)
    {
        var coupons = result.CashFlows.Where(flow => flow.Interest != 0m).Select(flow =>
            (Kind: "Coupon", flow.PaymentDate, flow.AccrualStart, Amount: flow.Interest, flow.IsForecast));
        // A final installment and residual balloon can be separate model rows for one obligation.
        // Sum raw principal before its single native cash boundary; coupon rights remain separate.
        var principal = result.CashFlows.Where(flow => flow.Principal != 0m)
            .GroupBy(flow => (flow.PaymentDate, flow.AccrualStart))
            .Select(group => (Kind: "Principal", group.Key.PaymentDate, group.Key.AccrualStart,
                Amount: group.Sum(flow => flow.Principal), IsForecast: group.Any(flow => flow.IsForecast)));
        return coupons.Concat(principal).Select(row => new BondMonitoringObligationDto(
                RetainedObligationIdentity(scope, securityId, row.Kind, row.PaymentDate, row.AccrualStart, previous),
                securityId, row.Kind, row.PaymentDate, LedgerCurrencyRounding.RoundCurrency(direction * row.Amount, result.Currency),
                result.Currency, result.ModelVersion, inputs, row.IsForecast))
            .Where(row => row.Amount != 0m).ToArray();
    }

    private static IReadOnlyList<BondMonitoringResetDto> ResetInputs(IReadOnlyList<BondWorkflowResult> calculations,
        IReadOnlyDictionary<Guid, int> directions, BondMonitoringSnapshotDto? previous)
    {
        var rows = new List<BondMonitoringResetDto>();
        foreach (var result in calculations.Where(result => directions.ContainsKey(result.SecurityId)))
            foreach (var flow in result.CashFlows.Where(flow => flow.Observations.Count > 0))
            {
                var amount = LedgerCurrencyRounding.RoundCurrency(directions[result.SecurityId]
                    * result.CashFlows.Where(row => row.PaymentDate == flow.PaymentDate).Sum(row => row.Interest), result.Currency);
                var priorAmounts = previous?.Inputs.Resets.Where(row => row.SecurityId == result.SecurityId
                        && row.CouponPaymentDate == flow.PaymentDate && row.CouponAmount is not null)
                    .Select(row => row.CouponAmount!.Value).Distinct().ToArray() ?? [];
                var priorObligations = previous?.Inputs.Obligations.Where(row => row.IsCurrentHolding && row.SecurityId == result.SecurityId
                    && row.FlowType == "Coupon" && row.DueDate == flow.PaymentDate).ToArray() ?? [];
                decimal? previousAmount = priorAmounts.Length == 1 ? priorAmounts[0]
                    : priorObligations.Length > 0 ? priorObligations.Sum(row => row.Amount) : null;
                foreach (var observation in flow.Observations.Distinct())
                {
                    var priorRates = previous?.Inputs.Resets.Where(row => row.SecurityId == result.SecurityId
                            && row.AccrualDate == observation.AccrualDate && row.ObservationDate == observation.ObservationDate)
                        .Select(row => row.Rate).Distinct().ToArray() ?? [];
                    rows.Add(new(result.SecurityId, observation.AccrualDate, observation.ObservationDate,
                        observation.Rate, observation.IsObserved, observation.Source, result.Inputs)
                    {
                        CouponPaymentDate = flow.PaymentDate,
                        CouponAmount = amount,
                        PreviousCouponAmount = previousAmount,
                        PreviousRate = priorRates.Length == 1 ? priorRates[0] : null
                    });
                }
            }
        return rows.Distinct().ToArray();
    }

    private static PortfolioCashLadderContributionDto Contribution(string id, Guid security, string kind, DateOnly date,
        decimal amount, string currency, string source, string? reference) => new(security, id, "Bond", null,
            BondMonitoringEngine.EngineVersion, null, null, source, reference, kind,
            kind == "Coupon" ? "Coupons & interest" : "Maturities & principal", date, amount, currency, amount, null);
}
