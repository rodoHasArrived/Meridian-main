using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineService
{
    private async Task ValidateCouponRecognitionSourceAsync(AssetAccountingEventSpineDto source, CancellationToken ct)
    {
        if (source.BondCouponRecognition is not { } instruction)
            return;
        var authority = _couponRecognitionAuthority?.Invoke()
            ?? throw new InvalidOperationException("Coupon income requires canonical retained posted-state authority.");
        var calculationSource = source.RetainedEvidence.SingleOrDefault(e => e.SubjectType == AssetAccountingEvidenceSubjects.Event
            && e.SubjectId == instruction.EventId.ToString("D"))
            ?? throw new InvalidOperationException("Coupon income requires its exact retained operator-reviewed calculation source.");
        await authority.VerifyPreparedSourceAsync(instruction, calculationSource, ct).ConfigureAwait(false);
        var lot = instruction.IncomeInstruction.ExpectedLot;
        var evidence = lot.Acquisition.Evidence.Append(instruction.IncomeInstruction.SecurityEvidence)
            .Concat(instruction.Cash.Select(c => c.ObservedPayment.Provenance.Evidence))
            .Concat(instruction.Cash.Where(c => c.Allocation.DeductionEvidence is not null).Select(c => c.Allocation.DeductionEvidence!.Evidence))
            .Append(calculationSource).Distinct().ToArray();
        if (source.EventKind != AssetAccountingEventKindDto.Income || source.EventId != instruction.EventId
            || source.EventVersion != 1 || source.EffectiveDate != instruction.Request.ThroughDate
            || source.Scope.LedgerBookId != lot.LedgerBookId || source.Scope.BookPositionId != lot.BookPositionId
            || source.Scope.SecurityId != lot.SecurityId || source.Scope.ExpectedSecurityVersion != instruction.IncomeInstruction.Security.Version
            || source.Scope.ExpectedBookPositionVersion != instruction.IncomeInstruction.ExpectedBookPositionVersion
            || source.Scope.TenantId != instruction.Scope.TenantId || source.Scope.CompanyId != instruction.Scope.CompanyId
            || source.Scope.PeriodId != instruction.Request.PeriodId || source.Currency != instruction.Scope.BaseCurrency
            || source.Direction != OpenLotDirection.Long || source.CorporateAction is not null || source.CapitalReturn is not null
            || (instruction.ReversalTarget is null ? source.Correction is not null : source.Correction?.PostedJournalEntryId != instruction.ReversalTarget.JournalEntryId)
            || source.FactorPaydown is not null || source.EconomicEvent.SourceDomain != calculationSource.SourceSystem
            || source.EconomicEvent.SourceContentHash != calculationSource.ContentHashSha256
            || source.EconomicEvent.SourceEntityId != calculationSource.SourceReference || source.ProjectionLineage.ModelVersion != instruction.ModelVersion
            || source.ProjectedEffect is not { } effect || source.EventAmount != effect.TotalDebits
            || effect.TotalDebits != instruction.Lines.Sum(l => l.Debit) || effect.TotalCredits != instruction.Lines.Sum(l => l.Credit)
            || JsonSerializer.Serialize(effect.Lines) != JsonSerializer.Serialize(instruction.Lines)
            || !source.RetainedEvidence.SequenceEqual(evidence))
            throw new InvalidOperationException("Coupon Income source must exactly match its server-prepared retained economics, account scope and journal effects.");
    }
}
