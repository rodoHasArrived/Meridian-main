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
        OpenLotCorporateActionInstructionDto reviewed,
        string assetAccountId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(reviewed);
        var lotStore = lots ?? throw new InvalidOperationException("Corporate-action preparation requires authoritative ledger lots.");
        var securityStore = securities ?? throw new InvalidOperationException("Corporate-action preparation requires authoritative Security Master records.");
        var positionStore = positions ?? throw new InvalidOperationException("Corporate-action preparation requires authoritative book positions.");
        var source = mapped.Event;
        if (source.EventKind != AssetAccountingEventKindDto.CorporateAction || !mapped.PostingSet.RequiresJournalCandidate
            || source.Correction is not null || reviewed.CorporateActionId != source.EconomicEvent.EventId
            || reviewed.ActionType != mapped.Treatment.ActionType || reviewed.EffectiveDate != source.EconomicEvent.EffectiveDate
            || reviewed.ExpectedLot.LedgerBookId != source.Scope.LedgerBookId
            || reviewed.ExpectedLot.SecurityId != source.Scope.SecurityId
            || reviewed.ExpectedLot.BookPositionId != source.Scope.BookPositionId
            || reviewed.ExpectedBookPositionVersion != source.Scope.ExpectedBookPositionVersion
            || reviewed.Security.Version != source.Scope.ExpectedSecurityVersion
            || !Equivalent(reviewed.Mutations, mapped.LotMutations.Mutations))
            throw new InvalidOperationException("Corporate-action lot preparation must bind the exact mapped event, treatment, authoritative mutation plan and scope; no-journal or correction actions require another workflow.");
        _ = OpenLotCorporateAction.Project(reviewed);
        var groups = OpenLotCorporateAction.Groups(reviewed);
        var predecessorIds = groups.Select(group => group.ExpectedLot.TaxLotRecordId).ToHashSet();
        var inventory = await lotStore.ListOpenTaxLotsByAssetScopeAsync(source.Scope.LedgerBookId,
            source.Scope.SecurityId, source.Scope.BookPositionId, reviewed.EffectiveDate, ct).ConfigureAwait(false);
        if (inventory.Count != predecessorIds.Count || !predecessorIds.SetEquals(inventory.Select(lot => lot.TaxLotRecordId)))
            throw new InvalidOperationException("The reviewed corporate action must include every affected open predecessor lot in the authoritative position.");
        var retained = await lotStore.GetTaxLotsByIdsAsync(source.Scope.LedgerBookId,
            groups.Select(group => group.ExpectedLot.TaxLotRecordId).ToArray(), ct).ConfigureAwait(false);
        if (retained.Count != predecessorIds.Count || retained.Select(lot => lot.TaxLotRecordId).Distinct().Count() != retained.Count)
            throw new InvalidOperationException("A reviewed predecessor lot no longer exists.");
        foreach (var group in groups)
        {
            var actual = retained.SingleOrDefault(lot => lot.TaxLotRecordId == group.ExpectedLot.TaxLotRecordId);
            if (actual is null || actual.Account.ToString() != group.SourceAssetAccountId || !Equivalent(actual.ToOpenLot(), group.ExpectedLot))
                throw new InvalidOperationException("A predecessor lot account, quantity, basis or version is stale.");
        }
        var successors = groups.SelectMany(group => group.Successors).ToArray();
        foreach (var expected in new[] { reviewed.Security }.Concat(successors.Select(target => target.Security)))
        {
            var actual = await securityStore.GetProjectionAsync(expected.SecurityId, ct).ConfigureAwait(false);
            if (actual is null || OpenLotAmortization.SecurityHash(actual) != OpenLotAmortization.SecurityHash(expected))
                throw new InvalidOperationException("A source or successor Security Master projection is missing or stale.");
        }
        var expectedPositions = new[] { (reviewed.ExpectedLot.BookPositionId, reviewed.ExpectedLot.SecurityId, reviewed.ExpectedBookPositionVersion) }
            .Concat(successors.Select(target => (target.BookPositionId, target.Security.SecurityId, target.ExpectedBookPositionVersion))).Distinct();
        foreach (var (positionId, securityId, version) in expectedPositions)
        {
            var actual = await positionStore.GetBookPositionAsync(positionId, ct).ConfigureAwait(false);
            if (actual is null || actual.SecurityId != securityId || actual.Version != version
                || actual.BookContext.LedgerBookId != source.Scope.LedgerBookId
                || actual.BookContext.FundProfileId != source.Scope.FundProfileId
                || actual.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset)
                || actual.Status != "Active" || actual.EffectiveFrom > reviewed.EffectiveDate
                || actual.EffectiveTo is { } end && end < reviewed.EffectiveDate)
                throw new InvalidOperationException("A source or successor book position is missing, stale or outside the mapped book scope.");
        }
        var instruction = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.CorporateAction,
            AssetAccountId: assetAccountId, CorporateAction: reviewed);
        var issues = AssetLotMutationInstructionValidator.Validate(source.EventKind, instruction, source.EventAmount,
            source.EconomicEvent.EffectiveDate, source.RetainedEvidence);
        if (issues.Count != 0)
            throw new InvalidOperationException(string.Join(" ", issues));
        return instruction;
    }

    /// <summary>Persists only Projected/Drafted stages. Independent human approval and the existing posting command remain required.</summary>
    public async Task<AssetAccountingPostingCandidateDto> DraftAsync(
        CorporateActionAssetAccountingEventProjectionDto mapped,
        OpenLotCorporateActionInstructionDto reviewed,
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
