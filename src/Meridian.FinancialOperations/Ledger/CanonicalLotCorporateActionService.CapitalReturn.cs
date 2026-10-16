using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class CanonicalLotCorporateActionService
{
    public async Task<AssetLotMutationInstructionDto> PreviewAsync(
        CorporateActionAssetAccountingEventProjectionDto mapped,
        OpenLotCapitalReturnInstructionDto reviewed,
        string assetAccountId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(mapped);
        OpenLotCapitalReturn.Project(reviewed);
        var lotStore = lots ?? throw new InvalidOperationException("Capital-return preparation requires authoritative ledger lots.");
        var securityStore = securities ?? throw new InvalidOperationException("Capital-return preparation requires authoritative Security Master records.");
        var positionStore = positions ?? throw new InvalidOperationException("Capital-return preparation requires authoritative book positions.");
        var source = mapped.Event;
        var projection = reviewed.Projection;
        var scope = projection.AccountingScope!;
        var instruction = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.CapitalReturn,
            AssetAccountId: assetAccountId, CapitalReturn: reviewed);
        if (source.EventKind != AssetAccountingEventKindDto.CorporateAction || source.Correction is not null
            || source.CorporateAction is not null || string.IsNullOrWhiteSpace(assetAccountId)
            || !Equivalent(source.CapitalReturn, reviewed)
            || mapped.LotMutation is not { Intent: AssetLotMutationIntentDto.CapitalReturn } mappedInstruction
            || mappedInstruction.AssetAccountId is { } account && account != assetAccountId
            || !Equivalent(mappedInstruction with { AssetAccountId = assetAccountId }, instruction)
            || !Equivalent(projection.EconomicEvent, source.EconomicEvent)
            || !Equivalent(projection.ProjectionLineage, source.ProjectionLineage)
            || !Equivalent(projection.Treatment, mapped.Treatment) || !Equivalent(projection.LotMutations, mapped.LotMutations)
            || !Equivalent(projection.PostingSet, mapped.PostingSet)
            || reviewed.ExpectedLot.LedgerBookId != source.Scope.LedgerBookId
            || reviewed.ExpectedLot.SecurityId != source.Scope.SecurityId || reviewed.ExpectedLot.BookPositionId != source.Scope.BookPositionId
            || projection.LotMutations!.ExpectedPositionVersion != source.Scope.ExpectedBookPositionVersion
            || reviewed.ExpectedSecurityVersion != source.Scope.ExpectedSecurityVersion
            || projection.EventAmount != source.EventAmount || projection.PostingSet!.Currency != source.Currency
            || scope.PeriodId != source.Scope.PeriodId || scope.ExpectedPeriodVersion != source.ExpectedPeriodVersion
            || scope.FundProfileId != source.Scope.FundProfileId || scope.TenantId != source.Scope.TenantId
            || scope.CompanyId != source.Scope.CompanyId || projection.Treatment.AccountingBasis != source.Scope.AccountingBasis)
            throw new InvalidOperationException("Capital-return preparation must bind the exact mapped event, treatment, mutation plan and accounting scope.");
        var assetLines = source.ProjectedEffect.Lines.Where(line => line.AccountId == assetAccountId).ToArray();
        if (assetLines.Length != 1 || assetLines[0].Debit != 0m || assetLines[0].Credit != source.EventAmount)
            throw new InvalidOperationException("Capital return requires the exact mapped carrying-account credit.");
        var retained = (await lotStore.GetTaxLotsByIdsAsync(source.Scope.LedgerBookId,
            [reviewed.ExpectedLot.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new InvalidOperationException("The reviewed capital-return lot no longer exists.");
        if (!Equivalent(retained.ToOpenLot(), reviewed.ExpectedLot))
            throw new InvalidOperationException("The reviewed capital-return lot is stale.");
        var security = await securityStore.GetProjectionAsync(reviewed.ExpectedLot.SecurityId, ct).ConfigureAwait(false);
        if (security is null || security.Version != reviewed.ExpectedSecurityVersion
            || OpenLotAmortization.SecurityHash(security) != reviewed.ExpectedSecurityHash)
            throw new InvalidOperationException("Capital-return Security Master evidence is missing or stale.");
        var position = await positionStore.GetBookPositionAsync(reviewed.ExpectedLot.BookPositionId, ct).ConfigureAwait(false);
        if (position is null || position.SecurityId != reviewed.ExpectedLot.SecurityId
            || position.Version != source.Scope.ExpectedBookPositionVersion || position.BookContext.LedgerBookId != source.Scope.LedgerBookId
            || position.BookContext.FundProfileId != source.Scope.FundProfileId
            || position.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset) || position.Status != "Active"
            || position.EffectiveFrom > source.EconomicEvent.EffectiveDate
            || position.EffectiveTo is { } end && end < source.EconomicEvent.EffectiveDate)
            throw new InvalidOperationException("Capital-return book position is missing, stale or outside the mapped scope.");
        var issues = AssetLotMutationInstructionValidator.Validate(source.EventKind, instruction,
            source.EventAmount, source.EconomicEvent.EffectiveDate, source.RetainedEvidence);
        if (issues.Count > 0)
            throw new InvalidOperationException(string.Join(" ", issues));
        return instruction;
    }

    /// <summary>Retains Projected/Drafted stages only; independent approval and atomic posting remain required.</summary>
    public async Task<AssetAccountingPostingCandidateDto> DraftAsync(
        CorporateActionAssetAccountingEventProjectionDto mapped, OpenLotCapitalReturnInstructionDto reviewed,
        string assetAccountId, string actor, DateTimeOffset accountingTimestamp, string description, CancellationToken ct = default)
    {
        var eventSpine = spine ?? throw new InvalidOperationException("Capital-return drafting requires the governed asset accounting event spine.");
        var instruction = await PreviewAsync(mapped, reviewed, assetAccountId, ct).ConfigureAwait(false);
        var projection = await eventSpine.ProjectAsync(mapped.Event, ct).ConfigureAwait(false);
        var source = mapped.Event;
        return await eventSpine.BuildPostingCandidateAsync(new AssetAccountingPostingCandidateRequestDto(
            source.EventKind, source.Scope, source.EconomicEvent, source.ProjectionLineage, source.EventAmount,
            source.Currency, actor, accountingTimestamp, description, projection.Spine.SpineVersion,
            source.ExpectedPeriodVersion, source.RetainedEvidence, instruction), ct).ConfigureAwait(false);
    }
}
