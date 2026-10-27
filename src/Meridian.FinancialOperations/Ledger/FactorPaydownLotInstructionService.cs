using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// What a caller states about a factor observation: the scope, the two factors, the retained
/// factor evidence and the chart paths the paydown posts to. Everything else — the lots, the
/// principal, the per-lot allocation, the realized result — is derived server side.
/// </summary>
public sealed record FactorPaydownPreparationRequest(
    Guid LedgerBookId,
    Guid SecurityId,
    Guid BookPositionId,
    Guid PeriodId,
    long ExpectedPeriodVersion,
    AccountingBasisKindDto AccountingBasis,
    string FundProfileId,
    DateOnly EffectiveDate,
    decimal PriorFactor,
    decimal NewFactor,
    string SourceDomain,
    string SourceEntityId,
    string SourceContentHash,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<RetainedEvidenceIdentityDto> FactorEvidence,
    string CashAccountPath,
    string CarryingAccountPath,
    string RealizedGainAccountPath,
    string RealizedLossAccountPath,
    string? TenantId = null,
    string? CompanyId = null,
    LedgerDimensionSetDto? Dimensions = null,
    Guid? CorrelationId = null);

/// <summary>The derived instruction and the spine projection request that carries it.</summary>
public sealed record FactorPaydownPreparation(
    OpenLotFactorPaydownInstructionDto Instruction,
    AssetLotMutationInstructionDto LotMutation,
    ProjectAssetAccountingEventRequestDto ProjectRequest,
    IReadOnlyDictionary<string, decimal> Components);

/// <summary>
/// Prepares a reviewed factor paydown from the lots of record: loads the lots open in the scope as
/// of the effective date, derives held face and the position principal through the registered
/// projector, allocates principal and pro-rata basis relief per lot, and assembles the canonical
/// corporate-action event, lineage, two- or three-line projected effect and lot instruction the
/// spine retains. It never approves or posts; the candidate service and the atomic store re-derive
/// and re-lock everything here before anything is written. Hosts compose it like the amortization
/// preview: a host without persistence still starts, and preparation refuses the missing authority
/// before reading any source record.
/// </summary>
public sealed class FactorPaydownLotInstructionService(
    ILedgerJournalStore? lots,
    ISecurityMasterStore? securities,
    IInstrumentPositionProjectionStore? positions,
    IFactorPaydownProjectionService? projector)
{
    public async Task<FactorPaydownPreparation> PrepareAsync(
        FactorPaydownPreparationRequest request,
        string actor,
        DateTimeOffset projectedAtUtc,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lotStore = lots ?? throw new InvalidOperationException(
            "Factor paydown cannot be prepared because no authoritative ledger journal store is configured.");
        var securityStore = securities ?? throw new InvalidOperationException(
            "Factor paydown cannot be prepared because no authoritative Security Master store is configured.");
        var positionStore = positions ?? throw new InvalidOperationException(
            "Factor paydown cannot be prepared because no authoritative book-position store is configured.");
        var paydownProjector = projector ?? throw new InvalidOperationException(
            "Factor paydown cannot be prepared because no factor-paydown projector is configured.");
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FactorEvidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var hash = Sha256Digest.Normalize(request.SourceContentHash)
            ?? throw new InvalidOperationException("Factor paydown requires a canonical SHA-256 source-content hash.");

        var security = await securityStore.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Factor paydown requires the authoritative Security Master projection.");
        var position = await positionStore.GetBookPositionAsync(request.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Factor paydown requires the authoritative book position.");
        if (position.SecurityId != request.SecurityId || position.BookContext.LedgerBookId != request.LedgerBookId
            || position.Status != "Active" || position.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset))
            throw new InvalidOperationException("Factor-paydown book position must be an active long position of the security in the ledger book.");
        var currency = position.BookContext.BaseCurrency;
        if (!string.Equals(security.Currency, currency, StringComparison.Ordinal))
            throw new InvalidOperationException("Factor paydown supports same-currency positions only; a translated paydown requires explicit FX authority.");

        var records = await lotStore.ListOpenTaxLotsByAssetScopeAsync(request.LedgerBookId, request.SecurityId,
            request.BookPositionId, request.EffectiveDate, ct).ConfigureAwait(false);
        if (records.Count == 0)
            throw new InvalidOperationException("The position holds no lot of record open on the paydown date.");
        var heldFace = 0m;
        foreach (var record in records)
        {
            var face = record.ToFaceValueLot()
                ?? throw new InvalidOperationException($"Open lot '{record.LotId}' did not record its par conventions; held face cannot be derived.");
            if (face.CurrentFactor != request.PriorFactor)
                throw new InvalidOperationException($"Open lot '{record.LotId}' holds factor {face.CurrentFactor}, not the position's prior factor {request.PriorFactor}.");
            heldFace += face.ParFace * (record.OpenQuantity / record.OriginalQuantity);
        }

        var projection = paydownProjector.Project(new FactorPaydownProjectionRequest(request.SecurityId, request.BookPositionId,
            position.Version, position.Version, heldFace, request.PriorFactor, request.NewFactor, currency,
            request.EffectiveDate, request.OccurredAtUtc, request.SourceDomain, request.SourceEntityId, hash,
            request.FactorEvidence.Select(static evidence => evidence.EvidenceUri).ToArray(), request.CorrelationId));
        if (!projection.ProducesPostingCandidate)
            throw new InvalidOperationException("The factor observation does not project a principal paydown: "
                + string.Join(" ", projection.Issues.Select(static issue => issue.Message)));

        var openLots = records.Select(static record => record.ToOpenLot()).ToArray();
        var allocated = OpenLotFactorPaydown.Allocate(openLots, request.PriorFactor, request.NewFactor,
            projection.PrincipalPaydown!.Value, currency);
        var evidence = request.FactorEvidence
            .Concat(openLots.SelectMany(static lot => lot.Acquisition.Evidence))
            .DistinctBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidenceLinks = evidence.Select(static item => item.EvidenceUri).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // The projector's event, retyped as the canonical corporate-action event; the model's own
        // event type survives only in the lineage model key.
        var economicEvent = projection.EconomicEvent! with
        {
            EventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
            EvidenceLinks = evidenceLinks,
            RetainedEvidence = evidence
        };
        var lineage = projection.Lineage! with
        {
            TriggerEvent = economicEvent,
            EvidenceLinks = evidenceLinks,
            RetainedEvidence = evidence
        };
        var dimensions = (request.Dimensions ?? position.BookContext.Dimensions ?? new LedgerDimensionSetDto()) with
        {
            FundId = request.FundProfileId,
            BookId = request.LedgerBookId.ToString("D"),
            InstrumentId = request.SecurityId,
            PositionId = request.BookPositionId
        };
        var scope = new AssetAccountingEventScopeDto(request.SecurityId, security.Version, request.BookPositionId,
            position.Version, request.LedgerBookId, request.PeriodId, request.AccountingBasis, request.FundProfileId,
            request.TenantId, request.CompanyId, dimensions);
        var instruction = new OpenLotFactorPaydownInstructionDto(economicEvent, lineage, scope, request.ExpectedPeriodVersion,
            OpenLotAmortization.SecurityHash(security), request.PriorFactor, request.NewFactor,
            projection.PrincipalPaydown.Value, currency, allocated);
        OpenLotFactorPaydown.Project(instruction);

        var components = OpenLotFactorPaydown.Components(instruction);
        var lines = new List<ProjectedAccountingEffectLineDto>
        {
            new(request.CashAccountPath, components[OpenLotFactorPaydown.PrincipalComponent], 0m, currency, "Principal received", dimensions)
        };
        if (components[OpenLotFactorPaydown.BasisRelievedComponent] > 0m)
            lines.Add(new(request.CarryingAccountPath, 0m, components[OpenLotFactorPaydown.BasisRelievedComponent], currency, "Carrying basis relieved", dimensions));
        if (components[OpenLotFactorPaydown.RealizedGainComponent] > 0m)
            lines.Add(new(request.RealizedGainAccountPath, 0m, components[OpenLotFactorPaydown.RealizedGainComponent], currency, "Realized gain on principal repaid", dimensions));
        if (components[OpenLotFactorPaydown.RealizedLossComponent] > 0m)
            lines.Add(new(request.RealizedLossAccountPath, components[OpenLotFactorPaydown.RealizedLossComponent], 0m, currency, "Realized loss on principal repaid", dimensions));
        var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion,
            request.EffectiveDate, lines.Sum(static line => line.Debit), lines.Sum(static line => line.Credit), currency, lines);
        var projectRequest = new ProjectAssetAccountingEventRequestDto(AssetAccountingEventKindDto.CorporateAction, scope,
            economicEvent, lineage, effect, instruction.PrincipalPaydown, currency, request.ExpectedPeriodVersion,
            actor.Trim(), projectedAtUtc, evidence, FactorPaydown: instruction);
        var lotMutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.FactorPaydown,
            AssetAccountId: request.CarryingAccountPath, FactorPaydown: instruction);
        return new FactorPaydownPreparation(instruction, lotMutation, projectRequest, components);
    }
}
