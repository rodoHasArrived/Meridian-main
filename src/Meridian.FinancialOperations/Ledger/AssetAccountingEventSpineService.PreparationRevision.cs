using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineService
{
    public async Task<AssetAccountingEventSpineAppendResultDto> ReprepareUnpostedFactorPaydownAsync(
        ProjectAssetAccountingEventRequestDto request, AssetLotMutationInstructionDto lotMutation, long expectedSpineVersion,
        string expectedCanonicalFingerprint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lotMutation);
        ValidateProjectionRequest(request);
        var mutationIssues = AssetLotMutationInstructionValidator.Validate(request.EventKind, lotMutation,
            request.EventAmount, request.EconomicEvent.EffectiveDate, request.RetainedEvidence);
        RequireAssertion(mutationIssues.Count == 0 && lotMutation.Intent == AssetLotMutationIntentDto.FactorPaydown &&
            PayloadEquals(lotMutation.FactorPaydown, request.FactorPaydown) &&
            lotMutation.FactorPaydown!.ExpectedPeriodVersion == request.ExpectedPeriodVersion,
            $"A preparation revision requires its exact reviewed factor instruction: {string.Join(" ", mutationIssues)}");
        RequireAssertion(request.ProjectedAtUtc <= DateTimeOffset.UtcNow,
            "A factor preparation revision cannot attest a future preparation time.");
        var previous = await _eventStore.GetAsync(request.EconomicEvent.EventId,
            request.EconomicEvent.EventVersion, expectedSpineVersion, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The factor preparation predecessor is unavailable.");
        ValidateStoredRecord(previous);
        RequireAssertion(Sha256Digest.FixedEquals(previous.CanonicalFingerprint, expectedCanonicalFingerprint),
            "The factor preparation predecessor changed. Prepare and review it again.");
        var revision = new AssetAccountingPreparationRevisionDto(expectedSpineVersion, previous.CanonicalFingerprint,
            request.Actor.Trim(), request.ProjectedAtUtc, "Fresh reviewed factor preparation replaces an undrafted attempt.");
        var stages = new[]
        {
            new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Expected, request.ProjectedAtUtc,
                request.Actor.Trim(), request.RetainedEvidence,
                $"economic-event://{request.EconomicEvent.EventId:D}/{request.EconomicEvent.EventVersion}", request.Notes?.Trim()),
            new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Projected, request.ProjectedAtUtc,
                request.Actor.Trim(), request.RetainedEvidence,
                $"projection-run://{request.ProjectionLineage.ProjectionRunId:D}", request.Notes?.Trim())
        };
        var projected = new AssetAccountingEventSpineDto(request.EconomicEvent.EventId, request.EventKind,
            request.EconomicEvent.EventVersion, expectedSpineVersion + 1, request.EconomicEvent.EffectiveDate,
            request.EventAmount, request.Currency.Trim(), request.Scope, request.EconomicEvent, request.ProjectionLineage,
            request.RetainedEvidence, stages, request.ProjectedEffect, Correction: request.Correction,
            CorporateAction: request.CorporateAction, CapitalReturn: request.CapitalReturn,
            FactorPaydown: request.FactorPaydown, PreparationRevision: revision);
        var issues = AssetAccountingEventSpineValidator.Validate(projected);
        RequireAssertion(issues.Count == 0, $"Reprepared factor projection is invalid: {string.Join(" ", issues)}");
        AssetAccountingPreparationRevisionValidator.ValidateTransition(previous.Projection, projected);
        var latest = await _eventStore.GetLatestAsync(projected.EventId, projected.EventVersion, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The current factor preparation is unavailable.");
        ValidateStoredRecord(latest);
        if (latest.Projection.SpineVersion == projected.SpineVersion && PayloadEquals(latest.Projection, projected))
            return new AssetAccountingEventSpineAppendResultDto(latest.Projection, WasReplay: true);
        RequireAssertion(latest.Projection.SpineVersion == expectedSpineVersion &&
            Sha256Digest.FixedEquals(latest.CanonicalFingerprint, expectedCanonicalFingerprint),
            "The factor preparation predecessor changed. Prepare and review it again.");

        await ResolveAuthoritativeSecurityAsync(projected, request.ProjectedAtUtc, ct).ConfigureAwait(false);
        var securityStore = _securityMasterStore
            ?? throw new InvalidOperationException("Factor preparation recovery requires the authoritative Security Master projection store.");
        var security = await securityStore.GetProjectionAsync(projected.Scope.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The current factor Security Master projection is unavailable.");
        RequireAssertion(security.SecurityId == projected.Scope.SecurityId && security.Version == projected.Scope.ExpectedSecurityVersion &&
            Sha256Digest.FixedEquals(OpenLotAmortization.SecurityHash(security), projected.FactorPaydown!.ExpectedSecurityHash),
            "The factor preparation revision must bind the current exact Security Master projection.");
        var position = await _positionStore.GetBookPositionAsync(projected.Scope.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authoritative factor book position is unavailable.");
        ValidatePosition(projected, position);
        var book = await _ledgerBookService.GetBookAsync(projected.Scope.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authoritative factor ledger book is unavailable.");
        ValidateBook(projected, position, book);
        var periods = await _ledgerBookService.ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: book.LedgerBookId), ct).ConfigureAwait(false);
        var period = periods.SingleOrDefault(item => item.PeriodId == projected.Scope.PeriodId)
            ?? throw new InvalidOperationException("The authoritative factor accounting period is unavailable.");
        ValidateProjectionPeriod(request, projected, book, period);

        var lots = await _journalStore.ListOpenTaxLotsByAssetScopeAsync(book.LedgerBookId, projected.Scope.SecurityId,
            position.PositionId, projected.EffectiveDate, ct).ConfigureAwait(false);
        RequireAssertion(lots.Count == projected.FactorPaydown!.Lots.Count && lots.Count > 0 &&
            lots.Select(static lot => lot.TaxLotRecordId).ToHashSet().SetEquals(
                projected.FactorPaydown.Lots.Select(static lot => lot.ExpectedLot.TaxLotRecordId)),
            "A factor preparation revision requires the complete current set of lots of record.");
        await ResolveAuthoritativeLotMutationAsync(lotMutation, projected, position, book, ct).ConfigureAwait(false);
        var append = await _eventStore.AppendAsync(projected, expectedSpineVersion, position.Version, ct).ConfigureAwait(false);
        ValidateStoredRecord(new AssetAccountingEventProjectionRecord(append.Projection, append.CanonicalFingerprint));
        return new AssetAccountingEventSpineAppendResultDto(append.Projection, append.WasReplay);
    }
}
