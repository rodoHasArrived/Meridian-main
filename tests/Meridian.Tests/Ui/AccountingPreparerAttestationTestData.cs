using System.Globalization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Ui;

internal static class AccountingPreparerAttestationTestData
{
    public const string PrivateEntitlement = "{\"wholeBookEntitlement\":\"private-retained-operands\"}";

    public static LedgerJournalEntryRecord Attach(LedgerJournalEntryRecord record, LedgerAmountScopeDto scope)
    {
        var amount = record.Entry.Lines.Sum(line => line.Debit);
        var exDate = DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
        var eventId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var debitName = record.Entry.Lines.Single(line => line.Debit > 0m).Account.Name;
        var creditName = record.Entry.Lines.Single(line => line.Credit > 0m).Account.Name;
        if (creditName == debitName)
            creditName = "Dividend income";
        var roles = new CashDividendAccountRoleBindingDto(scope.LedgerBookId, debitName, creditName,
            "synthetic-dividend", "1", "dividend-income", "1",
            DividendReceivableAccountName: debitName, DividendIncomeAccountName: creditName);
        var entitlementHash = Sha256Digest.ComputeUtf8(PrivateEntitlement);
        var receipt = new CorporateActionCashDividendAttestationDto(
            PreparerAttestationApprovalBoundary.NewReceiptId(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 1,
            CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
            Guid.NewGuid(), "synthetic-related-dividend", exDate, "synthetic", "source-event", "1", new string('a', 64),
            entitlementHash, PrivateEntitlement, amount, amount, new DateOnly(exDate.Year, exDate.Month, 1),
            "preparer", record.CreatedAt.AddTicks(-1), "Inspected synthetic receipt.",
            "cash-dividend-eligibility:" + entitlementHash, entitlementHash,
            CorporateActionCashDividendAttestationPolicy.Confirmation, CorporateActionCashDividendAttestationPolicy.Disclosure,
            AccountRoleBinding: roles);
        var binding = new AssetAccountingPreparerAttestationDto(receipt, scope.LedgerBookId, scope.PeriodId,
            scope.TenantId, scope.CompanyId, eventId, 2, new string('b', 64), new string('c', 64));
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["approvalState"] = "PreparerAttested", ["decisionKind"] = receipt.DecisionKind,
            ["attestationReceiptRef"] = PreparerAttestationApprovalBoundary.ReceiptReference(receipt.AttestationId),
            ["attestationId"] = receipt.AttestationId.ToString("D"), ["attestedBy"] = receipt.AttestedBy,
            ["declarationId"] = receipt.DeclarationId.ToString("D"),
            ["declarationRevision"] = receipt.DeclarationRevision.ToString(CultureInfo.InvariantCulture),
            ["policyVersion"] = receipt.PolicyVersion, ["policyHash"] = receipt.PolicyHash,
            ["postingCommandId"] = commandId.ToString("D"), ["postingCommandFingerprint"] = "sha256:" + new string('d', 64),
            ["postingActor"] = receipt.AttestedBy, ["postingActorAttribution"] = "command-v1",
            ["sourceEventId"] = eventId.ToString("D"), ["sourceEventType"] = "AssetAccounting.CorporateAction",
            ["sourceEventVersion"] = "2", ["assetAccountingDraftFingerprint"] = binding.DraftedCandidateFingerprint,
            ["assetAccountingDraftResultFingerprint"] = new string('e', 64),
            ["rulePackId"] = roles.RulePackId, ["rulePackVersion"] = roles.RulePackVersion,
            ["selectedRuleId"] = roles.SelectedRuleId, ["selectedRuleVersion"] = roles.SelectedRuleVersion
        };
        var lines = record.Entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId,
            line.Timestamp, new LedgerAccount(line.Debit > 0m ? debitName : creditName,
                line.Debit > 0m ? LedgerAccountType.Asset : LedgerAccountType.Revenue),
            line.Debit, line.Credit, line.Description, line.Dimensions,
            new LedgerEntryCurrency("USD", "USD", line.Debit, line.Credit, 1m))).ToArray();
        var entry = new JournalEntry(record.Entry.JournalEntryId, record.Entry.Timestamp, record.Entry.Description,
            lines, record.Entry.Metadata with
            {
                LedgerBook = scope.LedgerBookId.ToString("D"), EffectiveDate = exDate,
                Tags = tags, PreparerAttestation = binding
            });
        return record with { Entry = entry, AggregateId = scope.LedgerBookId, CommandId = commandId,
            SourceEventId = eventId, RuleId = roles.SelectedRuleId, RuleVersion = roles.SelectedRuleVersion };
    }

    public static LedgerJournalEntryRecord WithMetadata(LedgerJournalEntryRecord record, JournalEntryMetadata metadata)
        => record with { Entry = new JournalEntry(record.Entry.JournalEntryId, record.Entry.Timestamp,
            record.Entry.Description, record.Entry.Lines, metadata) };
}
