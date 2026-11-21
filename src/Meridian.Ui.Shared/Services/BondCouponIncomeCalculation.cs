using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.FixedIncome;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Recurring coupon income for one governed bond tax lot (W10-JRNL-001, W10-BOND-001). Each
/// occurrence runs the existing <see cref="IBondCouponRecognitionService"/> (and, when the template
/// asks for it, <see cref="IBondIncomePreparationService"/> for premium/discount income) through
/// the governed accounting-event spine. The resulting candidate waits in accounting review; this
/// calculation never approves or posts, and it returns no manual-journal draft because the spine
/// candidate is the posting vehicle. Inputs: <c>accountId</c>, <c>taxLotRecordId</c>, optional
/// <c>includeAmortization</c> (<c>true</c>/<c>false</c>).
/// </summary>
/// <remarks>
/// Accrual only: the run carries no cash allocations, so coupon cash clearing stays on the
/// operator path that names retained payment allocations. Do not schedule this kind and
/// <see cref="FixedIncomeAccrualCalculation"/> for the same position: both recognize coupon income.
/// </remarks>
public sealed class BondCouponIncomeCalculation(
    IBondCouponRecognitionService? coupons = null,
    IAssetAccountingEventSpineService? spine = null,
    BondWorkspaceSnapshotStore? snapshots = null,
    IBondCouponRecognitionStore? receipts = null,
    IBondIncomeService? income = null,
    IBondIncomePreparationService? incomePreparation = null,
    ILogger<BondCouponIncomeCalculation>? logger = null) : IRecurringJournalCalculation
{
    public const string CalculationKind = "bond-coupon-income";
    internal const string PreparationKind = "coupon-preparation";

    private readonly ILogger _logger = logger ?? NullLogger<BondCouponIncomeCalculation>.Instance;

    public string Kind => CalculationKind;

    // Recognition is cumulative against the latest posted receipt, so an occurrence waits for the
    // previous one, and the lot is read at the close of the effective date.
    public bool DependsOnPostedHistory => true;

    public bool ReadsClosingState => true;

    public IReadOnlyList<string> RequiredInputs => ["accountId", "taxLotRecordId"];

    public async Task<RecurringJournalCalculationResult> CalculateAsync(RecurringJournalCalculationContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (coupons is null || spine is null || snapshots is null)
            throw new InvalidOperationException(
                "Governed bond coupon recognition, the accounting event spine and retained workflow evidence are required; recurring coupon income is blocked.");
        if (!Guid.TryParse(context.RequireInput("accountId"), out var accountId) || accountId == Guid.Empty)
            throw new InvalidOperationException("The calculated template input 'accountId' must be an account identifier.");
        if (!Guid.TryParse(context.RequireInput("taxLotRecordId"), out var taxLotRecordId) || taxLotRecordId == Guid.Empty)
            throw new InvalidOperationException("The calculated template input 'taxLotRecordId' must be a tax lot record identifier.");
        if (!Guid.TryParse(context.PeriodId, out var periodId) || periodId == Guid.Empty)
            throw new InvalidOperationException("Bond coupon income requires a ledger period identifier for the covering period.");
        var includeAmortization = context.OptionalInput("includeAmortization") is { } flag
            && (bool.TryParse(flag, out var parsed) ? parsed
                : throw new InvalidOperationException("The calculated template input 'includeAmortization' must be true or false."));

        var scope = new AccountCashForecastScopeDto(context.Scope.TenantId, context.Scope.CompanyId,
            accountId.ToString("D"), context.Scope.LedgerBookId.ToString("D"), context.Scope.Currency);
        var actor = $"recurring-journal:{context.ScheduleId}";
        await RequirePreviousRunPostedAsync(context, scope, taxLotRecordId, ct).ConfigureAwait(false);

        var prepared = new List<string>();
        if (includeAmortization)
        {
            if (income is null || incomePreparation is null)
                throw new InvalidOperationException(
                    "Bond income preparation is unavailable on this host; recurring premium/discount income is blocked.");
            var previewRequest = new BondIncomePreviewRequestDto(context.Scope.LedgerBookId, taxLotRecordId,
                context.EffectiveDate, context.OccurredAtUtc, accountId);
            var preview = await income.PreviewAsync(previewRequest, ct).ConfigureAwait(false);
            var result = await incomePreparation.PrepareAsync(new BondIncomePreparationRequestDto(previewRequest,
                preview.Fingerprint, periodId), scope.TenantId, scope.CompanyId, actor, context.OccurredAtUtc, ct).ConfigureAwait(false);
            if (result.PreparedArtifact is { } artifact)
                prepared.Add(FormattableString.Invariant($"premium/discount event {artifact.EventId:D}"));
        }

        var runId = RunId(context.ScheduleId, context.EffectiveDate);
        var preparation = await snapshots.ReadAsync<BondCouponRecognitionPreparationDto>(scope, PreparationKind, runId, ct)
            .ConfigureAwait(false);
        if (preparation is null)
        {
            var request = new BondCouponRecognitionRequest(runId, taxLotRecordId, periodId, context.EffectiveDate,
                context.OccurredAtUtc, []);
            var preview = await coupons.PreviewAsync(scope, request, ct).ConfigureAwait(false);
            if (preview.HasChanges)
            {
                // The preview was built moments ago from the same authorities; preparing against its
                // own fingerprint still fails closed if anything moved in between.
                preparation = await coupons.PrepareAsync(scope, request, preview.Fingerprint, actor,
                    context.OccurredAtUtc, ct).ConfigureAwait(false);
                await snapshots.RetainAsync(scope, PreparationKind, runId, preparation, ct).ConfigureAwait(false);
            }
        }
        if (preparation is not null)
        {
            var projected = await spine.ProjectAsync(preparation.Projection, ct).ConfigureAwait(false);
            prepared.Add(FormattableString.Invariant(
                $"coupon event {preparation.Preview.Instruction.EventId:D} ({RecurringJournalCalculationMath.Money(preparation.Preview.EarnedDelta)} earned)"));
            _logger.LogInformation(
                "Recurring bond coupon income prepared for review: schedule {ScheduleId}, lot {TaxLotRecordId}, through {ThroughDate}, event {EventId}, replay {WasReplay}",
                context.ScheduleId, taxLotRecordId, context.EffectiveDate, preparation.Preview.Instruction.EventId, projected.WasReplay);
        }

        return RecurringJournalCalculationResult.NothingDue(prepared.Count == 0
            ? FormattableString.Invariant($"Bond lot {taxLotRecordId:D} has no coupon movement to recognize through {context.EffectiveDate:yyyy-MM-dd}.")
            : FormattableString.Invariant(
                $"Bond income for lot {taxLotRecordId:D} through {context.EffectiveDate:yyyy-MM-dd} prepared for accounting review: {string.Join("; ", prepared)}. Approval and posting remain in accounting review."));
    }

    /// <summary>Deterministic per occurrence and within the recognition run-id bound.</summary>
    internal static string RunId(string scheduleId, DateOnly effectiveDate)
        => FormattableString.Invariant($"recurring:{Sha256Digest.ComputeUtf8(scheduleId)[..32]}:{effectiveDate:yyyyMMdd}");

    // A prepared but unposted earlier occurrence would make this preview count the same earned
    // coupon again, so the occurrence waits until the earlier receipt is posted.
    private async Task RequirePreviousRunPostedAsync(RecurringJournalCalculationContext context,
        AccountCashForecastScopeDto scope, Guid taxLotRecordId, CancellationToken ct)
    {
        if (context.WindowStart is not { } windowStart)
            return;
        var previousRunId = RunId(context.ScheduleId, windowStart.AddDays(-1));
        var previous = await snapshots!.ReadAsync<BondCouponRecognitionPreparationDto>(scope, PreparationKind, previousRunId, ct)
            .ConfigureAwait(false);
        if (previous is null)
            return;
        if (receipts is null)
            throw new InvalidOperationException("Durable coupon recognition receipts are unavailable; the previous occurrence's posting cannot be confirmed.");
        if (await receipts.GetCouponRecognitionRunAsync(scope, taxLotRecordId, previousRunId, ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException(
                $"The coupon income prepared for {windowStart.AddDays(-1):yyyy-MM-dd} is still awaiting approval and posting; this occurrence waits for it.");
    }
}
