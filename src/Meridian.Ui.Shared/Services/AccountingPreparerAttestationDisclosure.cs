using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Classifies only a retained native journal decision. Source evidence, ordinary approval and
/// financial coverage remain separate dimensions; no mutable case or receipt lookup is permitted.
/// </summary>
internal static class AccountingPreparerAttestationDisclosure
{
    internal static IReadOnlyList<AccountingPreparerAttestationDisclosureDto>? Read(
        LedgerJournalEntryRecord record, LedgerAmountScopeDto scope)
    {
        var metadata = record.Entry.Metadata;
        var tags = metadata.Tags;
        var binding = metadata.PreparerAttestation;
        if (binding is null)
            return HasAttestationClaim(tags)
                ? [Unknown(scope, [record.Entry.JournalEntryId], "A preparer decision claim has no retained typed native binding.")]
                : null;

        var issues = new List<string>();
        string? actor = null;
        try
        {
            actor = AccountingPostingCommandValidator.ReadRetainedPostingActor(metadata);
        }
        catch (LedgerValidationException)
        {
            issues.Add("The retained native posting actor attribution is invalid.");
        }
        var eventVersion = long.TryParse(Tag(tags, "sourceEventVersion"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var version) ? version : 0;
        decimal debit;
        decimal credit;
        try
        {
            debit = record.Entry.Lines.Sum(line => line.Debit);
            credit = record.Entry.Lines.Sum(line => line.Credit);
        }
        catch (OverflowException)
        {
            return [Unknown(scope, [record.Entry.JournalEntryId], "The retained native journal gross amount cannot be classified.")];
        }
        issues.AddRange(AssetAccountingPreparerAttestationDto.Validate(binding,
            scope.LedgerBookId, scope.PeriodId, record.SourceEventId ?? Guid.Empty, eventVersion,
            scope.TenantId, scope.CompanyId, actor, "USD", debit));
        var receipt = binding.Receipt;
        if (receipt is null || receipt.AttestationId == Guid.Empty)
            return [Unknown(scope, [record.Entry.JournalEntryId], issues.ToArray())];

        if (record.AggregateId != scope.LedgerBookId || record.PeriodId != scope.PeriodId
            || metadata.LedgerBook != scope.LedgerBookId.ToString("D")
            || record.CommandId.GetValueOrDefault() == Guid.Empty
            || record.SourceJournalEntryId is not null || record.PostingKind != LedgerPostingKindDto.Originating
            || record.AdjustmentApproval is not null || debit <= 0m || debit != credit
            || receipt.AttestedAtUtc > record.CreatedAt
            || metadata.EffectiveDate != receipt.ExDate
            || receipt.BudgetMonth != new DateOnly(receipt.ExDate.Year, receipt.ExDate.Month, 1)
            || record.Entry.Lines.Any(line => line.Currency is not { } currency
                || currency.TransactionCurrency != "USD" || currency.FunctionalCurrency != "USD"
                || currency.FxRateToFunctional != 1m || currency.TransactionDebit != line.Debit
                || currency.TransactionCredit != line.Credit
                || line.Dimensions?.FundId is { } fund && fund != scope.FundProfileId
                || line.Dimensions?.BookId is { } book && book != scope.LedgerBookId.ToString("D")))
            issues.Add("The retained journal does not match the exact native USD book, period, originating event and gross amount context.");

        if (Tag(tags, "approvalState") != AccountingPostingApprovalStateDto.PreparerAttested.ToString()
            || Tag(tags, "decisionKind") != CorporateActionCashDividendAttestationPolicy.DecisionKind
            || Tag(tags, "attestationReceiptRef") != PreparerAttestationApprovalBoundary.ReceiptReference(receipt.AttestationId)
            || Tag(tags, "attestationId") != receipt.AttestationId.ToString("D")
            || Tag(tags, "attestedBy") != receipt.AttestedBy
            || Tag(tags, "declarationId") != receipt.DeclarationId.ToString("D")
            || Tag(tags, "declarationRevision") != receipt.DeclarationRevision.ToString(CultureInfo.InvariantCulture)
            || Tag(tags, "policyVersion") != receipt.PolicyVersion || Tag(tags, "policyHash") != receipt.PolicyHash
            || Tag(tags, "postingCommandId") != record.CommandId?.ToString("D")
            || !IsPostingCommandFingerprint(Tag(tags, "postingCommandFingerprint"))
            || !string.IsNullOrWhiteSpace(Tag(tags, "approvalId"))
            || Tag(tags, "sourceEventId") != record.SourceEventId?.ToString("D")
            || Tag(tags, "sourceEventType") != AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction)
            || Tag(tags, "assetAccountingDraftFingerprint") != binding.DraftedCandidateFingerprint
            || !Sha256Digest.IsCanonical(Tag(tags, "assetAccountingDraftResultFingerprint"))
            || !string.IsNullOrWhiteSpace(Tag(tags, "assetAccountingLotMutationFingerprint")))
            issues.Add("Native journal decision, event and posting fingerprints are missing or conflict with the typed preparer receipt.");

        if (receipt.AccountRoleBinding is not { SchemaVersion: 1 } roles
            || record.Entry.Lines.Count != 2
            || record.Entry.Lines.Count(line => line.Account.Name == roles.DividendReceivableAccountName
                && line.Account.AccountType == LedgerAccountType.Asset && line.Debit == debit && line.Credit == 0m) != 1
            || record.Entry.Lines.Count(line => line.Account.Name == roles.DividendIncomeAccountName
                && line.Account.AccountType == LedgerAccountType.Revenue && line.Credit == debit && line.Debit == 0m) != 1
            || record.RuleId != roles.SelectedRuleId || record.RuleVersion != roles.SelectedRuleVersion
            || Tag(tags, "rulePackId") != roles.RulePackId || Tag(tags, "rulePackVersion") != roles.RulePackVersion
            || Tag(tags, "selectedRuleId") != roles.SelectedRuleId || Tag(tags, "selectedRuleVersion") != roles.SelectedRuleVersion)
            issues.Add("The retained journal accounts and promoted rule do not match the book's explicit dividend role binding.");

        if (issues.Count != 0)
            return [Unknown(scope, [record.Entry.JournalEntryId], issues.Distinct().ToArray())];

        return [new AccountingPreparerAttestationDisclosureDto(1,
            LedgerAmountApprovalClassification.PreparerAttestation,
            CorporateActionCashDividendAttestationPolicy.Disclosure, scope, [record.Entry.JournalEntryId], [])
        {
            AttestationId = receipt.AttestationId,
            CaseId = receipt.CaseId,
            ProjectionId = receipt.ProjectionId,
            EventId = binding.EventId,
            EventVersion = binding.EventVersion,
            Actor = receipt.AttestedBy,
            RecordedAt = receipt.AttestedAtUtc,
            PolicyVersion = receipt.PolicyVersion,
            PolicyHash = receipt.PolicyHash,
            DeclarationId = receipt.DeclarationId,
            DeclarationRevision = receipt.DeclarationRevision,
            DraftedCandidateFingerprint = binding.DraftedCandidateFingerprint,
            PostingIntentHash = binding.PostingIntentHash
        }];
    }

    internal static IReadOnlyList<AccountingPreparerAttestationDisclosureDto>? Union(
        IEnumerable<AccountingPreparerAttestationDisclosureDto> disclosures)
    {
        var result = new Dictionary<string, AccountingPreparerAttestationDisclosureDto>(StringComparer.Ordinal);
        foreach (var item in disclosures)
        {
            var key = item.AttestationId is { } id ? "receipt:" + id.ToString("D")
                : "unknown:" + JsonSerializer.Serialize(new { item.SourceScope, item.JournalEntryIds });
            if (!result.TryGetValue(key, out var previous))
                result.Add(key, item);
            else if (JsonSerializer.Serialize(previous) != JsonSerializer.Serialize(item))
                result[key] = Unknown(previous.SourceScope,
                    previous.JournalEntryIds.Concat(item.JournalEntryIds).Distinct().Order().ToArray(),
                    "A native attestation identifier has conflicting retained journal bindings; its classification is unknown.");
        }
        return result.Count == 0 ? null : result.Values.ToArray();
    }

    private static AccountingPreparerAttestationDisclosureDto Unknown(LedgerAmountScopeDto scope,
        IReadOnlyList<Guid> journalIds, params string[] issues)
        => new(1, LedgerAmountApprovalClassification.Unknown,
            "Accounting preparer-attestation classification is unknown. Independent approval is not established.",
            scope, journalIds, issues);

    private static string? Tag(IReadOnlyDictionary<string, string>? tags, string key)
        => tags is not null && tags.TryGetValue(key, out var value) ? value : null;

    private static bool IsPostingCommandFingerprint(string? value)
        => value is not null && value.StartsWith("sha256:", StringComparison.Ordinal)
            && Sha256Digest.IsCanonical(value["sha256:".Length..]);

    private static bool HasAttestationClaim(IReadOnlyDictionary<string, string>? tags)
        => Tag(tags, "approvalState") == AccountingPostingApprovalStateDto.PreparerAttested.ToString()
            || Tag(tags, "decisionKind") == CorporateActionCashDividendAttestationPolicy.DecisionKind
            || Tag(tags, "attestationId") is not null || Tag(tags, "attestationReceiptRef") is not null;
}
