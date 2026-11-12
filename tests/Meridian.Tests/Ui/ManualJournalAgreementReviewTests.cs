using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ManualJournalAgreementReviewTests
{
    [Fact]
    public async Task MissingAgreement_RemainsAWarningWithoutRequestingAuthority()
    {
        var (service, draft, validation) = Harness(referenced: false);
        var result = await service.ValidateDraftAsync(new(draft, "preparer"));
        result.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.agreement-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Warning);
        result.ValidationIssues.Should().NotContain(issue => issue.Code == "manual-je.agreement-authority-invalid");
        await validation.DidNotReceive().ValidateIntercompanyAgreementReferencesAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangedAgreementAuthority_BlocksReviewWithRepairIssue()
    {
        var (service, draft, validation) = Harness(referenced: true);
        validation.ValidateIntercompanyAgreementReferencesAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new LedgerValidationException("Approved revision changed after submission.")));
        var result = await service.ValidateDraftAsync(new(draft, "reviewer"));
        result.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.agreement-authority-invalid" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical && issue.Message.Contains("revision changed"));
    }

    [Fact]
    public async Task ValidReference_IsCheckedWithTheExactAccountingDateAndDimensions()
    {
        var (service, draft, validation) = Harness(referenced: true);
        var result = await service.ValidateDraftAsync(new(draft, "reviewer"));
        result.ValidationIssues.Should().NotContain(issue => issue.Code.StartsWith("manual-je.agreement-"));
        await validation.Received(1).ValidateIntercompanyAgreementReferencesAsync(Arg.Is<LedgerJournalEntryWrite>(write =>
            write.Entry.Metadata.EffectiveDate == draft.AccountingDate &&
            write.Entry.Lines[0].Dimensions!.IntercompanyAgreementId == draft.Lines[0].Dimensions!.IntercompanyAgreementId &&
            write.Entry.Lines[0].Dimensions!.IntercompanyAgreementVersion == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PaddedJournalCurrency_DoesNotCreateAFalseAgreementAuthorityFailure()
    {
        var (service, draft, _) = Harness(referenced: true);
        var result = await service.ValidateDraftAsync(new(draft with { Currency = " USD " }, "reviewer"));
        result.ValidationIssues.Should().NotContain(issue => issue.Code == "manual-je.agreement-authority-invalid");
    }

    private static (ManualJournalEntryWorkbenchService Service, ManualJournalEntryDraftDto Draft,
        IIntercompanyAgreementJournalValidation Validation) Harness(bool referenced)
    {
        var book = IntercompanyAgreementPostingGuardTests.Book();
        var periodId = Guid.NewGuid();
        var domain = IntercompanyAgreementPostingGuardTests.Dimensions();
        var dimensions = new LedgerDimensionSetDto(EntityId: domain.EntityId, CounterpartyId: domain.CounterpartyId)
        { IntercompanyAgreementId = referenced ? domain.IntercompanyAgreementId : null,
            IntercompanyAgreementVersion = referenced ? 1 : null };
        var now = DateTimeOffset.UtcNow;
        var draft = new ManualJournalEntryDraftDto(Guid.NewGuid(), ManualJournalEntryStatusDto.Draft, book.FundProfileId,
            book.LedgerBookId, AccountingBasisKindDto.Primary, new(2026, 5, 15), periodId.ToString("D"),
            domain.EntityId, "fund-agreement", "USD", "Intercompany services", "preparer", now, now, 1,
            [new("receivable", AccountingTemplateLineSideDto.Debit, 120m, "USD", "Assets:Intercompany Receivable", Dimensions: dimensions),
             new("revenue", AccountingTemplateLineSideDto.Credit, 120m, "USD", "Revenue:Services")],
            ["evidence:services"], [], TenantId: "tenant-agreement", CompanyId: "company-agreement");
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.GetWorkspaceAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new AccountingConfigurationWorkspaceDto(book.FundProfileId, book.LedgerBookId,
                AccountingConfigurationStatusDto.Active, "v1", now, [],
                [new("receivable", "Assets:Intercompany Receivable", "Assets:Intercompany Receivable", "Asset"),
                 new("revenue", "Revenue:Services", "Revenue:Services", "Revenue")], [], [], [], []));
        var journalStore = Substitute.For<ILedgerJournalStore, IIntercompanyAgreementJournalValidation>();
        journalStore.GetLedgerBookAsync(book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(book);
        journalStore.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(periodId,
            book.LedgerBookId, 2026, 5, "May", new(2026, 5, 1), new(2026, 5, 31), "Open", now, null, 1));
        var validation = (IIntercompanyAgreementJournalValidation)journalStore;
        validation.ValidateIntercompanyAgreementReferencesAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return (new(Substitute.For<IManualJournalEntryDraftStore>(), configuration,
            Substitute.For<IAccountingActionAuditStore>(), journalStore: journalStore), draft, validation);
    }
}
