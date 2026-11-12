using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private static bool HasValidNativePreparerStage(AssetAccountingEventSpineDto spine)
        => spine.PreparerAttestation is not null && AssetAccountingEventSpineValidator.IsValid(spine)
            && spine.Stages.Any(stage => stage.Stage == AssetAccountingLifecycleStageDto.PreparerAttested)
            && !spine.Stages.Any(stage => stage.Stage == AssetAccountingLifecycleStageDto.Approved);

    private static bool MatchesNativePreparerDecision(AssetAccountingEventSpineDto spine, LedgerJournalEntryRecord journal)
    {
        if (spine.PreparerAttestation is null)
            return journal.Entry.Metadata.PreparerAttestation is null;
        if (!HasValidNativePreparerStage(spine)
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(spine.PreparerAttestation),
                JsonSerializer.SerializeToElement(journal.Entry.Metadata.PreparerAttestation)))
            return false;
        var scope = spine.Scope;
        var disclosure = AccountingPreparerAttestationDisclosure.Read(journal,
            new(scope.TenantId ?? string.Empty, scope.CompanyId ?? string.Empty, scope.FundProfileId, scope.LedgerBookId, scope.PeriodId));
        return disclosure is { Count: 1 } && disclosure[0].Classification == LedgerAmountApprovalClassification.PreparerAttestation;
    }

    private static void AppendNativePreparerDecisionField(List<FinancialRecordExplorerSummaryItemDto> fields,
        InstrumentJournalProof proof)
    {
        if (!TryGetAuthoritativeStage(proof, AssetAccountingLifecycleStageDto.PreparerAttested, out var stage))
            return;
        fields.Add(new("Attested by preparer (single-principal)", stage.ReferenceId!,
            NativePreparerDecisionDetail(stage, proof.Spine!.PreparerAttestation!), FinancialRecordExplorerTone.Info));
    }

    private static void AppendNativePreparerDecisionImpact(List<FinancialRecordExplorerRelationshipDto> impacts,
        InstrumentJournalProof proof, string href)
    {
        if (!TryGetAuthoritativeStage(proof, AssetAccountingLifecycleStageDto.PreparerAttested, out var stage))
            return;
        impacts.Add(new("preparer-attestation", "Attested by preparer (single-principal)",
            NativePreparerDecisionDetail(stage, proof.Spine!.PreparerAttestation!), href, FinancialRecordExplorerTone.Info));
    }

    private static string NativePreparerDecisionDetail(AssetAccountingStageEvidenceDto stage,
        AssetAccountingPreparerAttestationDto decision)
        => $"{CorporateActionCashDividendAttestationPolicy.Disclosure} Actor {stage.AttestedBy}; recorded {stage.AttestedAtUtc.ToString("u", CultureInfo.InvariantCulture)}; "
            + $"receipt {stage.ReferenceId}; declaration {decision.Receipt.DeclarationId:D} revision {decision.Receipt.DeclarationRevision}; policy {decision.Receipt.PolicyVersion}.";
}
