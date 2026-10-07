using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Connects an already mapped corporate-action projection to the shared draft/approval rail. Never approves or posts.</summary>
public sealed class CanonicalLotCorporateActionService(
    ILedgerJournalStore? lots,
    ISecurityMasterStore? securities,
    IInstrumentPositionProjectionStore? positions,
    IAssetAccountingEventSpineService? spine = null)
{
    public async Task<AssetLotMutationInstructionDto> PreviewAsync(
        CorporateActionAssetAccountingEventProjectionDto mapped,
        OpenLotSuccessorInstructionDto reviewed,
        string assetAccountId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(reviewed);
        var lotStore = lots ?? throw new InvalidOperationException("Corporate-action preparation requires authoritative ledger lots.");
        var securityStore = securities ?? throw new InvalidOperationException("Corporate-action preparation requires authoritative Security Master records.");
        var positionStore = positions ?? throw new InvalidOperationException("Corporate-action preparation requires authoritative book positions.");
        var source = mapped.Event;
        var projection = reviewed.Projection;
        var scope = projection.AccountingScope;
        var instruction = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.CorporateAction,
            AssetAccountId: assetAccountId, CorporateAction: reviewed);
        if (source.EventKind != AssetAccountingEventKindDto.CorporateAction || !mapped.PostingSet.RequiresJournalCandidate
            || source.Correction is not null || string.IsNullOrWhiteSpace(assetAccountId)
            || source.CorporateAction is null || !Equivalent(source.CorporateAction, reviewed)
            || mapped.LotMutation is not { Intent: AssetLotMutationIntentDto.CorporateAction, CorporateAction: { } mappedInstruction }
            || !Equivalent(mappedInstruction, reviewed)
            || mapped.LotMutation.AssetAccountId is { } mappedAccount && mappedAccount != assetAccountId
            || !Equivalent(mapped.LotMutation with { AssetAccountId = assetAccountId }, instruction)
            || !Equivalent(projection.EconomicEvent, source.EconomicEvent)
            || !Equivalent(projection.ProjectionLineage, source.ProjectionLineage)
            || !Equivalent(projection.Treatment, mapped.Treatment)
            || !Equivalent(projection.LotMutations, mapped.LotMutations)
            || !Equivalent(projection.PostingSet, mapped.PostingSet)
            || reviewed.ExpectedLot.LedgerBookId != source.Scope.LedgerBookId
            || reviewed.ExpectedLot.SecurityId != source.Scope.SecurityId
            || reviewed.ExpectedLot.BookPositionId != source.Scope.BookPositionId
            || projection.LotMutations?.ExpectedPositionVersion != source.Scope.ExpectedBookPositionVersion
            || reviewed.ExpectedSecurityVersion != source.Scope.ExpectedSecurityVersion
            || projection.EventAmount != source.EventAmount || projection.PostingSet?.Currency != source.Currency
            || scope is null || scope.LedgerBookId != source.Scope.LedgerBookId
            || scope.PeriodId != source.Scope.PeriodId || scope.ExpectedPeriodVersion != source.ExpectedPeriodVersion
            || scope.FundProfileId != source.Scope.FundProfileId || scope.TenantId != source.Scope.TenantId
            || scope.CompanyId != source.Scope.CompanyId || projection.Treatment.AccountingBasis != source.Scope.AccountingBasis)
            throw new InvalidOperationException("Corporate-action lot preparation must bind the exact mapped event, treatment, authoritative mutation plan and scope; no-journal or correction actions require another workflow.");

        OpenLotSuccessors.Validate(reviewed);
        var predecessorCredits = source.ProjectedEffect.Lines.Where(line => line.Debit == 0m
            && line.Credit == reviewed.ExpectedLot.OpenFunctionalCostBasis
            && line.Dimensions?.InstrumentId == reviewed.ExpectedLot.SecurityId
            && line.Dimensions?.PositionId == reviewed.ExpectedLot.BookPositionId
            && (line.Dimensions.TaxLotId is null || line.Dimensions.TaxLotId == reviewed.ExpectedLot.LotId)).ToArray();
        if (predecessorCredits.Length != 1 || predecessorCredits[0].AccountId != assetAccountId)
            throw new InvalidOperationException("The source asset account path must match the exact mapped predecessor credit.");
        var retained = (await lotStore.GetTaxLotsByIdsAsync(source.Scope.LedgerBookId,
            [reviewed.ExpectedLot.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new InvalidOperationException("The reviewed predecessor lot no longer exists.");
        if (!Equivalent(retained.ToOpenLot(), reviewed.ExpectedLot))
            throw new InvalidOperationException("The predecessor lot quantity, basis or version is stale.");

        var expectedSecurities = new[] { (reviewed.ExpectedLot.SecurityId, reviewed.ExpectedSecurityVersion, reviewed.ExpectedSecurityHash) }
            .Concat(reviewed.Successors.Select(target => (target.Lot.SecurityId, target.ExpectedSecurityVersion, target.ExpectedSecurityHash)));
        foreach (var (securityId, version, hash) in expectedSecurities)
        {
            var actual = await securityStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
            if (actual is null || actual.Version != version || OpenLotAmortization.SecurityHash(actual) != hash)
                throw new InvalidOperationException("A source or successor Security Master projection is missing or stale.");
        }

        var expectedPositions = new[] { (reviewed.ExpectedLot.BookPositionId, reviewed.ExpectedLot.SecurityId, source.Scope.ExpectedBookPositionVersion) }
            .Concat(reviewed.Successors.Select(target => (target.Lot.BookPositionId, target.Lot.SecurityId, target.ExpectedBookPositionVersion)))
            .Distinct();
        foreach (var (positionId, securityId, version) in expectedPositions)
        {
            var actual = await positionStore.GetBookPositionAsync(positionId, ct).ConfigureAwait(false);
            if (actual is null || actual.SecurityId != securityId || actual.Version != version
                || actual.BookContext.LedgerBookId != source.Scope.LedgerBookId
                || actual.BookContext.FundProfileId != source.Scope.FundProfileId
                || actual.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset)
                || actual.Status != "Active" || actual.EffectiveFrom > source.EconomicEvent.EffectiveDate
                || actual.EffectiveTo is { } end && end < source.EconomicEvent.EffectiveDate)
                throw new InvalidOperationException("A source or successor book position is missing, stale or outside the mapped book scope.");
        }

        var issues = AssetLotMutationInstructionValidator.Validate(source.EventKind, instruction, source.EventAmount,
            source.EconomicEvent.EffectiveDate, source.RetainedEvidence);
        if (issues.Count != 0)
            throw new InvalidOperationException(string.Join(" ", issues));
        return instruction;
    }

    /// <summary>Persists only Projected/Drafted stages. Independent human approval and the existing posting command remain required.</summary>
    public async Task<AssetAccountingPostingCandidateDto> DraftAsync(
        CorporateActionAssetAccountingEventProjectionDto mapped,
        OpenLotSuccessorInstructionDto reviewed,
        string assetAccountId,
        string actor,
        DateTimeOffset accountingTimestamp,
        string description,
        CancellationToken ct = default)
    {
        var eventSpine = spine ?? throw new InvalidOperationException("Corporate-action drafting requires the governed asset accounting event spine.");
        var instruction = await PreviewAsync(mapped, reviewed, assetAccountId, ct).ConfigureAwait(false);
        var projection = await eventSpine.ProjectAsync(mapped.Event, ct).ConfigureAwait(false);
        var source = mapped.Event;
        return await eventSpine.BuildPostingCandidateAsync(new AssetAccountingPostingCandidateRequestDto(
            source.EventKind, source.Scope, source.EconomicEvent, source.ProjectionLineage, source.EventAmount,
            source.Currency, actor, accountingTimestamp, description, projection.Spine.SpineVersion,
            source.ExpectedPeriodVersion, source.RetainedEvidence, instruction), ct).ConfigureAwait(false);
    }

    private static bool Equivalent<T>(T actual, T expected)
        => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(actual), JsonSerializer.SerializeToElement(expected));
}
