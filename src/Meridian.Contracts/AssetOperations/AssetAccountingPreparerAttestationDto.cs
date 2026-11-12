using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// Retained exact native cash-dividend decision binding. This is a preparer attestation, not an
/// ordinary approval; the native posting lease remains necessary at the ledger commit boundary.
/// </summary>
public sealed record AssetAccountingPreparerAttestationDto(
    CorporateActionCashDividendAttestationDto Receipt,
    Guid LedgerBookId,
    Guid PeriodId,
    string TenantId,
    string CompanyId,
    Guid EventId,
    long EventVersion,
    string DraftedCandidateFingerprint,
    string PostingIntentHash)
{
    public static IReadOnlyList<string> Validate(AssetAccountingPreparerAttestationDto? value,
        Guid ledgerBookId, Guid periodId, Guid eventId, long eventVersion,
        string? tenantId, string? companyId, string? actor, string currency, decimal amount)
    {
        var issues = new List<string>();
        if (value is null)
            return ["Preparer-attested posting requires an exact native cash-dividend decision binding."];
        var receipt = value.Receipt;
        if (receipt is null || !Meridian.Contracts.Operations.PreparerAttestationApprovalBoundary.IsReservedReceiptId(receipt.AttestationId) || receipt.CaseId == Guid.Empty
            || receipt.ProjectionId == Guid.Empty || receipt.BudgetReservationId == Guid.Empty || receipt.BoundCaseVersion <= 0
            || receipt.DeclarationId == Guid.Empty || receipt.DeclarationRevision <= 0 || !receipt.IsActive
            || receipt.PolicyVersion != CorporateActionCashDividendAttestationPolicy.Version
            || receipt.PolicyHash != CorporateActionCashDividendAttestationPolicy.PolicyHash
            || receipt.Disclosure != CorporateActionCashDividendAttestationPolicy.Disclosure
            || receipt.Confirmation != CorporateActionCashDividendAttestationPolicy.Confirmation
            || receipt.AttestedAtUtc == default || receipt.AttestedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(receipt.Reason) || string.IsNullOrWhiteSpace(receipt.RelatedEventKey)
            || string.IsNullOrWhiteSpace(receipt.ProviderId) || string.IsNullOrWhiteSpace(receipt.SourceEventId)
            || string.IsNullOrWhiteSpace(receipt.SourceEventVersion)
            || !Sha256Digest.IsCanonical(receipt.SourceEvidenceHash)
            || !Sha256Digest.IsCanonical(receipt.EntitlementSnapshotHash)
            || receipt.EvidenceReference != "cash-dividend-eligibility:" + receipt.EntitlementSnapshotHash
            || !Sha256Digest.FixedEquals(receipt.EvidenceHash, receipt.EntitlementSnapshotHash)
            || string.IsNullOrWhiteSpace(receipt.EntitlementSnapshotJson)
            || !Sha256Digest.FixedEquals(Sha256Digest.ComputeUtf8(receipt.EntitlementSnapshotJson), receipt.EntitlementSnapshotHash)
            || receipt.GrossImpactUsd <= 0m || receipt.GrossImpactUsd != amount
            || receipt.WholeEventGrossUsd < receipt.GrossImpactUsd
            || receipt.WholeEventGrossUsd > CorporateActionCashDividendAttestationPolicy.RelatedEventGrossLimitUsd
            || receipt.BudgetMonth == default || receipt.BudgetMonth.Day != 1
            || receipt.AccountRoleBinding is not { SchemaVersion: 1 } roles
            || roles.LedgerBookId != ledgerBookId
            || string.IsNullOrWhiteSpace(roles.DividendReceivableAccount) || string.IsNullOrWhiteSpace(roles.DividendIncomeAccount)
            || roles.DividendReceivableAccount == roles.DividendIncomeAccount
            || string.IsNullOrWhiteSpace(roles.DividendReceivableAccountName) || string.IsNullOrWhiteSpace(roles.DividendIncomeAccountName)
            || roles.DividendReceivableAccountName == roles.DividendIncomeAccountName
            || string.IsNullOrWhiteSpace(roles.RulePackId) || string.IsNullOrWhiteSpace(roles.RulePackVersion)
            || string.IsNullOrWhiteSpace(roles.SelectedRuleId) || string.IsNullOrWhiteSpace(roles.SelectedRuleVersion)
            || !string.Equals(receipt.AttestedBy?.Trim(), actor?.Trim(), StringComparison.OrdinalIgnoreCase))
            issues.Add("The native cash-dividend preparer receipt is missing, stale, outside policy, or bound to another actor or amount.");
        if (currency != CorporateActionCashDividendAttestationPolicy.Currency
            || value.LedgerBookId != ledgerBookId || ledgerBookId == Guid.Empty
            || value.PeriodId != periodId || periodId == Guid.Empty
            || value.EventId != eventId || eventId == Guid.Empty || value.EventVersion != eventVersion || eventVersion <= 0
            || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId)
            || value.TenantId != tenantId || value.CompanyId != companyId
            || !Sha256Digest.IsCanonical(value.DraftedCandidateFingerprint)
            || !Sha256Digest.IsCanonical(value.PostingIntentHash))
            issues.Add("The preparer decision must bind the exact USD book, period, event, owner scope and retained posting fingerprints.");
        return issues;
    }
}

public sealed record PostAttestedCashDividendPostingCandidateRequestDto(
    Meridian.Contracts.Ledger.PostingRuleJournalCandidateRequestDto Candidate,
    string Actor,
    Guid AttestationId,
    string TenantId,
    string CompanyId,
    string Reason,
    string? CorrelationId = null,
    Meridian.Contracts.Workstation.OperationsActionOriginDto ActionOrigin = Meridian.Contracts.Workstation.OperationsActionOriginDto.HumanOperator);
