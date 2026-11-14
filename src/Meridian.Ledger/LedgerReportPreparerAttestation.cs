using System.Globalization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ledger;

/// <summary>Compact immutable financial-export disclosure; it grants no independent approval.</summary>
public sealed record LedgerReportPreparerAttestation(
    Guid JournalEntryId,
    string Classification,
    string Disclosure,
    string? ReceiptReference,
    Guid? AttestationId,
    Guid? CaseId,
    Guid? LedgerBookId,
    Guid? SourcePeriodId,
    Guid? DeclarationId,
    long? DeclarationRevision,
    string? PolicyVersion,
    string? PolicyHash,
    string? Actor,
    DateTimeOffset? RecordedAtUtc)
{
    public bool IndependentApprovalSatisfied => false;

    internal static LedgerReportPreparerAttestation? Read(JournalEntry entry, LedgerReportPackRequest request)
    {
        var tags = entry.Metadata.Tags;
        string? Tag(string key) => tags is not null && tags.TryGetValue(key, out var value) ? value : null;
        if (entry.Metadata.PreparerAttestation is not { } binding)
            return Tag("approvalState") == "PreparerAttested" || Tag("decisionKind") == "PreparerAttestation"
                || Tag("attestationId") is not null || Tag("attestationReceiptRef") is not null ? Unknown(entry) : null;
        var receipt = binding.Receipt;
        var actor = Tag("postingActor");
        var debit = entry.Lines.Sum(line => line.Debit);
        if (receipt is null || !long.TryParse(Tag("sourceEventVersion"), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || !Guid.TryParse(entry.Metadata.LedgerBook, out var bookId)
            || !Guid.TryParse(Tag("sourceEventId"), out var eventId)
            || !string.IsNullOrWhiteSpace(request.LineDimensions?.BookId) && request.LineDimensions.BookId != bookId.ToString("D")
            || AssetAccountingPreparerAttestationDto.Validate(binding, bookId, binding.PeriodId, eventId, version,
                binding.TenantId, binding.CompanyId, actor, request.BaseCurrency, debit).Count != 0
            || entry.Metadata.EffectiveDate != receipt.ExDate || receipt.AttestedAtUtc > request.GeneratedAtUtc
            || receipt.BudgetMonth != new DateOnly(receipt.ExDate.Year, receipt.ExDate.Month, 1)
            || entry.Lines.Sum(line => line.Credit) != debit || entry.Lines.Count != 2
            || receipt.AccountRoleBinding is not { } roles
            || entry.Lines.Count(line => line.Account.Name == roles.DividendReceivableAccountName
                && line.Account.AccountType == LedgerAccountType.Asset && line.Debit == debit && line.Credit == 0m) != 1
            || entry.Lines.Count(line => line.Account.Name == roles.DividendIncomeAccountName
                && line.Account.AccountType == LedgerAccountType.Revenue && line.Credit == debit && line.Debit == 0m) != 1
            || entry.Lines.Any(line => line.Currency is not { TransactionCurrency: "USD", FunctionalCurrency: "USD", FxRateToFunctional: 1m } currency
                || currency.TransactionDebit != line.Debit || currency.TransactionCredit != line.Credit
                || line.Dimensions?.FundId != request.FundId || line.Dimensions?.BookId != bookId.ToString("D"))
            || Tag("approvalState") != "PreparerAttested" || !string.IsNullOrWhiteSpace(Tag("approvalId"))
            || Tag("decisionKind") != receipt.DecisionKind || Tag("postingActorAttribution") != "command-v1"
            || !Guid.TryParse(Tag("postingCommandId"), out var commandId) || commandId == Guid.Empty
            || Tag("postingCommandFingerprint") is not { } commandFingerprint
            || !commandFingerprint.StartsWith("sha256:", StringComparison.Ordinal)
            || !Sha256Digest.IsCanonical(commandFingerprint["sha256:".Length..])
            || Tag("attestationReceiptRef") != PreparerAttestationApprovalBoundary.ReceiptReference(receipt.AttestationId)
            || Tag("attestationId") != receipt.AttestationId.ToString("D") || Tag("attestedBy") != receipt.AttestedBy
            || Tag("declarationId") != receipt.DeclarationId.ToString("D")
            || Tag("declarationRevision") != receipt.DeclarationRevision.ToString(CultureInfo.InvariantCulture)
            || Tag("policyVersion") != receipt.PolicyVersion || Tag("policyHash") != receipt.PolicyHash
            || Tag("sourceEventType") != AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction)
            || Tag("assetAccountingDraftFingerprint") != binding.DraftedCandidateFingerprint
            || !Sha256Digest.IsCanonical(Tag("assetAccountingDraftResultFingerprint"))
            || !string.IsNullOrWhiteSpace(Tag("assetAccountingLotMutationFingerprint"))
            || Tag("rulePackId") != roles.RulePackId || Tag("rulePackVersion") != roles.RulePackVersion
            || Tag("selectedRuleId") != roles.SelectedRuleId || Tag("selectedRuleVersion") != roles.SelectedRuleVersion)
            return Unknown(entry);
        return new(entry.JournalEntryId, "PreparerAttestation", CorporateActionCashDividendAttestationPolicy.Disclosure,
            PreparerAttestationApprovalBoundary.ReceiptReference(receipt.AttestationId), receipt.AttestationId,
            receipt.CaseId, binding.LedgerBookId, binding.PeriodId, receipt.DeclarationId, receipt.DeclarationRevision,
            receipt.PolicyVersion, receipt.PolicyHash, receipt.AttestedBy, receipt.AttestedAtUtc);
    }

    private static LedgerReportPreparerAttestation Unknown(JournalEntry entry)
        => new(entry.JournalEntryId, "Unknown", "Accounting preparer-attestation classification is unknown. Independent approval is not established.",
            null, null, null, null, null, null, null, null, null, null, null);
}
