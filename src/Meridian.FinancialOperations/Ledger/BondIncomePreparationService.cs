using System.Buffers.Binary;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Retains the reviewed calculation source, then uses the existing governed event/draft path.</summary>
public sealed class BondIncomePreparationService(IBondIncomeService income, ILedgerJournalStore ledger,
    IInstrumentPositionProjectionStore positions, IAccountingPolicyService policies,
    IAssetAccountingEventSpineService spine, IBondIncomePreparationEvidenceStore evidence)
{
    public async Task<BondIncomePreviewDto> PrepareAsync(BondIncomePreparationRequestDto request,
        string tenantId, string companyId, string actor, DateTimeOffset preparedAtUtc, CancellationToken ct = default)
    {
        var preview = await income.PrepareAsync(request, ct).ConfigureAwait(false);
        var instruction = preview.Instruction;
        if (instruction.ExpectedLot.Acquisition.AcquisitionCurrency != instruction.ExpectedLot.Acquisition.FunctionalCurrency)
            throw new InvalidOperationException("Governed bond-income preparation requires a same-currency lot; functional FX recognition needs a separately adopted policy.");
        var movement = OpenLotAmortization.Project(instruction).FunctionalMovement;
        if (movement == 0m)
            return preview;
        var book = await ledger.GetLedgerBookAsync(preview.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authoritative income preparation book is unavailable.");
        var period = (await ledger.ListPeriodsAsync(preview.LedgerBookId, ct: ct).ConfigureAwait(false))
            .Where(p => p.StartDate <= instruction.AsOfDate && p.EndDate >= instruction.AsOfDate
                && (request.PeriodId is null || p.PeriodId == request.PeriodId)).ToArray();
        if (period.Length != 1 || period[0].Status != "Open")
            throw new InvalidOperationException("Income preparation requires one explicitly selected open book period covering the recognition date.");
        var currentPeriod = period[0];
        var position = await positions.GetBookPositionAsync(instruction.ExpectedLot.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The governed income book position is unavailable.");
        var policy = BondIncomeCalculator.ReadPolicy(await policies.ResolvePolicyAsync(new(book.AccountingBasis,
            instruction.AsOfDate, book.AccountingPolicyId, book.FundProfileId, book.FundStructureNodeId,
            PolicyVersion: book.AccountingPolicyVersion), ct).ConfigureAwait(false));
        if (string.IsNullOrWhiteSpace(policy.AmortizationIncomeAccountPath)
            || policy.AmortizationIncomeAccountPath == preview.LotMutation.AssetAccountId)
            throw new InvalidOperationException("The adopted book policy requires a distinct amortization/indexation income account mapping.");
        var identityHash = Convert.FromHexString(Sha256Digest.ComputeUtf8($"bond-income:{tenantId}:{companyId}:{preview.LedgerBookId:D}:{preview.TaxLotRecordId:D}:{instruction.AsOfDate:yyyy-MM-dd}"));
        var eventId = new Guid(identityHash.AsSpan(0, 16));
        var version = Math.Max(1L, BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(preview.Fingerprint).AsSpan(0, 8)) & long.MaxValue);
        var artifact = new BondIncomePreparationArtifactDto(tenantId, companyId, request.Request.AccountId,
            preview.LedgerBookId, currentPeriod.PeriodId, eventId, version, preview.Fingerprint, instruction);
        var existing = await spine.GetLatestAsync(eventId, version, ct).ConfigureAwait(false);
        var source = await evidence.RetainAsync(artifact, actor, preparedAtUtc, existing is not null, ct).ConfigureAwait(false);
        var retained = instruction.ExpectedLot.Acquisition.Evidence.Append(instruction.SecurityEvidence).Append(source)
            .DistinctBy(e => e.EvidenceId, StringComparer.OrdinalIgnoreCase).ToArray();
        var links = retained.Select(e => e.EvidenceUri).ToArray();
        var dimensions = (position.BookContext.Dimensions ?? new LedgerDimensionSetDto()) with
        {
            BookId = book.LedgerBookId.ToString("D"),
            FundId = book.FundProfileId,
            InstrumentId = instruction.Security.SecurityId,
            PositionId = position.PositionId,
            AccountId = request.Request.AccountId.ToString("D"),
            TaxLotId = instruction.ExpectedLot.LotId
        };
        var scope = new AssetAccountingEventScopeDto(instruction.Security.SecurityId, instruction.Security.Version,
            position.PositionId, instruction.ExpectedBookPositionVersion, book.LedgerBookId, currentPeriod.PeriodId,
            book.AccountingBasis, book.FundProfileId, tenantId, companyId, dimensions);
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.DepreciationAmortization);
        var economic = new EconomicEventReferenceDto(eventId, eventType, version, instruction.AsOfDate,
            source.RetainedAtUtc, source.SourceSystem, source.SourceReference, SourceContentHash: source.ContentHashSha256)
        { SecurityId = instruction.Security.SecurityId, BookPositionId = position.PositionId, EvidenceLinks = links, RetainedEvidence = retained };
        var lineage = new ProjectionLineageDto(eventId, null, "bond-income-amortization", BondIncomeCalculator.ModelVersion,
            "1", policy.FloaterPolicy.ToString(), instruction.AsOfDate, source.RetainedAtUtc,
            source.SourceSystem, source.SourceReference, economic)
        { BookPositionId = position.PositionId, EvidenceLinks = links, RetainedEvidence = retained };
        var amount = Math.Abs(movement);
        var lines = new ProjectedAccountingEffectLineDto[]
        {
            new(preview.LotMutation.AssetAccountId!, Math.Max(movement, 0m), Math.Max(-movement, 0m), book.BaseCurrency,
                "Adopted bond carrying-value movement", dimensions),
            new(policy.AmortizationIncomeAccountPath, Math.Max(-movement, 0m), Math.Max(movement, 0m), book.BaseCurrency,
                "Bond premium/discount and observed indexation income", dimensions)
        };
        var effect = new ProjectedAccountingEffectDto(eventId, lineage.ModelKey, lineage.ModelVersion,
            instruction.AsOfDate, amount, amount, book.BaseCurrency, lines);
        var projected = await spine.ProjectAsync(new(AssetAccountingEventKindDto.DepreciationAmortization, scope,
            economic, lineage, effect, amount, book.BaseCurrency, currentPeriod.Version,
            source.ReviewedBy, source.RetainedAtUtc, retained), ct).ConfigureAwait(false);
        if (projected.Spine.Stages.Any(s => s.Stage == AssetAccountingLifecycleStageDto.Drafted))
            return preview with
            {
                PreparedArtifact = artifact,
                PreparedCandidate = new(projected.Spine, projected.Spine.DraftedCandidateResult!)
            };
        var drafted = await spine.BuildPostingCandidateAsync(new(AssetAccountingEventKindDto.DepreciationAmortization,
            scope, economic, lineage, amount, book.BaseCurrency, source.ReviewedBy, source.RetainedAtUtc,
            "Prepared adopted bond income", projected.Spine.SpineVersion, currentPeriod.Version,
            retained, preview.LotMutation), ct).ConfigureAwait(false);
        return preview with { PreparedArtifact = artifact, PreparedCandidate = drafted };
    }
}
