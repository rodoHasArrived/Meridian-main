using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class LedgerInventoryAccountRoleClassifierTests
{
    [Fact]
    public void NativePreparerDividendRetainsExactNonLotRoleWithoutBecomingIndependentApproval()
    {
        var write = AccountingPostingCommandValidator.NormalizeAndValidate(
            CashDividendPreparerPostingContractTests.BuildWrite(), requirePostingCommand: true,
            requireExpectedVersion: true, allowPreparerAttestation: true);
        var original = write.Entry.Lines.Single(line => line.Debit > 0m);
        var metadata = write.Entry.Metadata;
        var binding = metadata.PreparerAttestation!;
        var renamedRoles = binding.Receipt.AccountRoleBinding! with { DividendReceivableAccountName = "Configured dividend accrual" };
        metadata = metadata with { PreparerAttestation = binding with { Receipt = binding.Receipt with { AccountRoleBinding = renamedRoles } } };
        var line = new LedgerEntry(original.EntryId, original.JournalEntryId, original.Timestamp,
            original.Account with { Name = renamedRoles.DividendReceivableAccountName!, Symbol = "AAPL" },
            original.Debit, original.Credit, original.Description, original.Dimensions, original.Currency);
        LedgerInventoryAccountRoleClassifier.Classify(line, metadata, Empty()).Should().Be(LedgerInventoryAccountRole.KnownNonLotAsset);
        metadata.PreparerAttestation.Receipt.DecisionKind.Should().Be("PreparerAttestation");
        LedgerInventoryAccountRoleClassifier.Classify(line, metadata with { EvidenceReferences = [] }, Empty())
            .Should().Be(LedgerInventoryAccountRole.UnknownInstrumentAsset);
        LedgerInventoryAccountRoleClassifier.Classify(line, metadata with { LedgerBook = Guid.NewGuid().ToString("D") }, Empty())
            .Should().Be(LedgerInventoryAccountRole.UnknownInstrumentAsset);
        var foreignCurrency = new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
            line.Debit, line.Credit, line.Description, line.Dimensions, new("EUR", "USD", line.Debit, 0m, 1m));
        LedgerInventoryAccountRoleClassifier.Classify(foreignCurrency, metadata, Empty())
            .Should().Be(LedgerInventoryAccountRole.UnknownInstrumentAsset);
    }

    [Fact]
    public void DividendReceivableRequiresTypedActivityAndRetainedSourceRole()
    {
        var line = Line(LedgerAccounts.DividendReceivable("AAPL", "equity-account"));
        var known = new JournalEntryMetadata(ActivityType: "DividendDeclared", LedgerView: LedgerViewKind.SecurityMaster);
        LedgerInventoryAccountRoleClassifier.Classify(line, known, Empty()).Should().Be(LedgerInventoryAccountRole.KnownNonLotAsset);
        LedgerInventoryAccountRoleClassifier.Classify(line, known with { LedgerView = null }, Empty())
            .Should().Be(LedgerInventoryAccountRole.UnknownInstrumentAsset);
        LedgerInventoryAccountRoleClassifier.Classify(line, known with { ActivityType = "Purchase" }, Empty())
            .Should().Be(LedgerInventoryAccountRole.UnknownInstrumentAsset);
    }

    [Fact]
    public void DirectLoanPrincipalRequiresMatchingSubledgerAndRetainedEventEvidence()
    {
        var account = Guid.NewGuid().ToString("D");
        var line = Line(new("Configured loan carrying caption", LedgerAccountType.Asset, "LOAN", account));
        var metadata = new JournalEntryMetadata(ActivityType: "loan.drawdown-booked", Institution: "DirectLending",
            FinancialAccountId: account, Tags: new Dictionary<string, string>
            { ["loanId"] = account, ["sourceEventType"] = "loan.drawdown-booked" }, EvidenceReferences:
            [new("event", "direct-lending://events/" + Guid.NewGuid(), "Source", "DirectLending", line.Timestamp,
                "source", SubjectId: account)]);
        LedgerInventoryAccountRoleClassifier.Classify(line, metadata, Empty()).Should().Be(LedgerInventoryAccountRole.KnownNonLotAsset);
        LedgerInventoryAccountRoleClassifier.Classify(line, metadata with { EvidenceReferences = [] }, Empty())
            .Should().Be(LedgerInventoryAccountRole.UnknownInstrumentAsset);
        LedgerInventoryAccountRoleClassifier.Classify(line, metadata with { FinancialAccountId = "foreign" }, Empty())
            .Should().Be(LedgerInventoryAccountRole.UnknownInstrumentAsset);
    }

    [Fact]
    public void CanonicalAcquisitionScopeEstablishesCarryingRoleWhileUnknownRenamesStayUnresolved()
    {
        var account = new LedgerAccount("Configured investment caption", LedgerAccountType.Asset, "AAPL", "account");
        var line = Line(account);
        LedgerInventoryAccountRoleClassifier.Classify(line, new(), new HashSet<LedgerAccount> { account })
            .Should().Be(LedgerInventoryAccountRole.LotCarrying);
        LedgerInventoryAccountRoleClassifier.Classify(line, new(), Empty()).Should().Be(LedgerInventoryAccountRole.UnknownInstrumentAsset);
        LedgerInventoryAccountRoleClassifier.Classify(Line(LedgerAccounts.Securities("AAPL", "account")), new(), Empty())
            .Should().Be(LedgerInventoryAccountRole.LotCarrying);
    }

    private static HashSet<LedgerAccount> Empty() => [];
    private static LedgerEntry Line(LedgerAccount account)
        => new(Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            account, 100m, 0m, "Role evidence");
}
