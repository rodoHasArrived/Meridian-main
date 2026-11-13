using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class ManualJournalAgreementReviewTests
{
    [Theory]
    [InlineData(false, "normal")]
    [InlineData(false, "reversal")]
    [InlineData(false, "rebook")]
    [InlineData(true, "normal")]
    [InlineData(true, "reversal")]
    [InlineData(true, "rebook")]
    public async Task CorrectionReview_DoesNotRequireFutureApproval_WhileFinalPostingRetainsActualApproval(bool profiled, string kind)
    {
        var fixture = CorrectionHarness(profiled, kind);
        var draft = await fixture.Service.SaveDraftAsync(new(fixture.Draft, "preparer",
            TenantId: fixture.Draft.TenantId, CompanyId: fixture.Draft.CompanyId));
        Assert.Null(draft.ApprovalId);
        Assert.Null(draft.ApprovedBy);
        Assert.Null(draft.ApprovedAtUtc);
        var submitted = await fixture.Service.SubmitApprovalAsync(new(draft.JournalEntryId, draft.FundProfileId,
            "preparer", draft.Version, Notes: "Reviewed source correction and accounting arrangement.",
            LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
        Assert.Equal(ManualJournalEntryStatusDto.Submitted, submitted.Status);
        Assert.Null(submitted.ApprovedBy);
        Assert.Null(submitted.ApprovedAtUtc);
        Assert.Empty(fixture.Posted);
        if (profiled)
        {
            Assert.NotEmpty(fixture.Reviews);
            Assert.All(fixture.Reviews, write =>
            {
                Assert.Equal(AccountingPostingApprovalStateDto.NotRequired, write.PostingCommand!.ApprovalState);
                Assert.Null(write.PostingCommand.ApprovalId);
                Assert.Null(write.AdjustmentApproval);
                AssertExactPins(fixture.Draft, write);
            });
        }
        var approved = (await fixture.Service.ApplyLifecycleActionAsync(Action(submitted, JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        Assert.Equal(ManualJournalEntryStatusDto.Approved, approved.Status);
        Assert.Equal("reviewer", approved.ApprovedBy);
        Assert.NotNull(approved.ApprovedAtUtc);
        Assert.Empty(fixture.Posted);
        var posted = (await fixture.Service.ApplyLifecycleActionAsync(Action(approved, JournalEntryLifecycleActionDto.Post))).JournalEntry;
        Assert.Equal(ManualJournalEntryStatusDto.Posted, posted.Status);
        var final = Assert.Single(fixture.Posted);
        Assert.Equal(AccountingPostingApprovalStateDto.Approved, final.PostingCommand!.ApprovalState);
        Assert.Equal(approved.ApprovalId, final.PostingCommand.ApprovalId);
        AssertExactPins(fixture.Draft, final);
        if (kind == "normal")
            Assert.Null(final.AdjustmentApproval);
        else
        {
            Assert.Equal(approved.ApprovalId, final.AdjustmentApproval!.ApprovalId);
            Assert.Equal(approved.ApprovedBy, final.AdjustmentApproval.ApprovedBy);
            Assert.Equal(approved.ApprovedAtUtc, final.AdjustmentApproval.ApprovedAt);
            Assert.Equal(fixture.Draft.ReversalOfJournalEntryId ?? fixture.Draft.RebookedFromJournalEntryId, final.SourceJournalEntryId);
        }
    }

    private static void AssertExactPins(ManualJournalEntryDraftDto draft, LedgerJournalEntryWrite write)
    {
        var expected = draft.Lines[0].Dimensions!;
        var actual = write.Entry.Lines[0].Dimensions!;
        Assert.Equal(expected.IntercompanyAgreementId, actual.IntercompanyAgreementId);
        Assert.Equal(expected.IntercompanyAgreementVersion, actual.IntercompanyAgreementVersion);
        Assert.Equal(expected.AccountingArrangementId, actual.AccountingArrangementId);
        Assert.Equal(expected.AccountingArrangementRevisionId, actual.AccountingArrangementRevisionId);
        Assert.Equal(expected.ChartAccountNodeId, actual.ChartAccountNodeId);
        Assert.Equal(expected.AccountBindingRevisionId, actual.AccountBindingRevisionId);
        Assert.Equal(expected.IntercompanyTransactionId, actual.IntercompanyTransactionId);
    }

    private static JournalEntryLifecycleActionRequestDto Action(ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionDto action)
        => new(draft.JournalEntryId, draft.FundProfileId, action, "reviewer", draft.Version,
            Notes: "Reviewed exact source economics and approved legal/accounting pins.",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/{(action == JournalEntryLifecycleActionDto.Post ? "posting" : "approval")}/tenant/{draft.TenantId}/company/{draft.CompanyId}/ledger-book/{draft.LedgerBookId:D}/{draft.PeriodId}"],
            LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId);

    private static CorrectionFixture CorrectionHarness(bool profiled, string kind)
    {
        var book = IntercompanyAgreementPostingGuardTests.Book();
        var periodId = Guid.NewGuid();
        var domain = IntercompanyAgreementPostingGuardTests.Dimensions();
        var dimensions = new LedgerDimensionSetDto(EntityId: domain.EntityId, CounterpartyId: domain.CounterpartyId,
            FundId: book.FundProfileId, BookId: book.LedgerBookId.ToString("D"))
        {
            IntercompanyAgreementId = domain.IntercompanyAgreementId,
            IntercompanyAgreementVersion = 1,
            AccountingArrangementId = profiled ? Guid.NewGuid().ToString("D") : null,
            AccountingArrangementRevisionId = profiled ? Guid.NewGuid().ToString("D") : null,
            ChartAccountNodeId = profiled ? "receivable" : null,
            AccountBindingRevisionId = profiled ? Guid.NewGuid().ToString("D") : null,
            IntercompanyTransactionId = profiled ? "original-correction-event" : null
        };
        var now = DateTimeOffset.UtcNow;
        var sourceId = Guid.NewGuid();
        var draft = new ManualJournalEntryDraftDto(Guid.NewGuid(), ManualJournalEntryStatusDto.Draft, book.FundProfileId,
            book.LedgerBookId, AccountingBasisKindDto.Primary, new(2026, 5, 15), periodId.ToString("D"),
            domain.EntityId, "fund-agreement", "USD", "Reviewed intercompany correction", "preparer", now, now, 1,
            [new("receivable", AccountingTemplateLineSideDto.Debit, 120m, "USD", "Assets:Intercompany Receivable", Dimensions: dimensions),
             new("revenue", AccountingTemplateLineSideDto.Credit, 120m, "USD", "Revenue:Services")],
            ["evidence:source-economics"], [], EntryType: kind == "reversal" ? ManualJournalEntryTypeDto.Reversal : ManualJournalEntryTypeDto.General,
            ReversalOfJournalEntryId: kind == "reversal" ? sourceId : null,
            RebookedFromJournalEntryId: kind == "rebook" ? sourceId : null,
            TenantId: "tenant-agreement", CompanyId: "company-agreement");
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.GetWorkspaceAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new AccountingConfigurationWorkspaceDto(book.FundProfileId, book.LedgerBookId,
                AccountingConfigurationStatusDto.Active, "v1", now, [],
                [new("receivable", "Assets:Intercompany Receivable", "Assets:Intercompany Receivable", "Asset",
                    IntercompanyRole: profiled ? IntercompanyAccountRoleDto.Receivable : null),
                 new("revenue", "Revenue:Services", "Revenue:Services", "Revenue")], [], [], [], [],
                TenantId: draft.TenantId, CompanyId: draft.CompanyId));
        var journals = (ILedgerJournalStore)Substitute.For([typeof(ILedgerJournalStore),
            typeof(IIntercompanyAgreementJournalValidation), typeof(ILedgerEntityControlJournalValidation),
            typeof(ILedgerPostingIdentityCollisionLookup)], []);
        journals.GetLedgerBookAsync(book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(book);
        journals.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(periodId,
            book.LedgerBookId, 2026, 5, "May", new(2026, 5, 1), new(2026, 5, 31), "Open", now, null, 1));
        var reviews = new List<LedgerJournalEntryWrite>();
        ((IIntercompanyAgreementJournalValidation)journals).ValidateIntercompanyAgreementReferencesAsync(Arg.Any<LedgerJournalEntryWrite>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        ((ILedgerEntityControlJournalValidation)journals).ValidateEntityPostingControlsAsync(Arg.Do<LedgerJournalEntryWrite>(reviews.Add),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var posted = new List<LedgerJournalEntryWrite>();
        var records = new List<LedgerJournalEntryRecord>();
        journals.AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var write = AccountingPostingCommandValidator.NormalizeAndValidate(call.Arg<LedgerJournalEntryWrite>());
            posted.Add(write);
            records.Add(new(write.Entry, write.AggregateId, write.PeriodId, write.CommandId, write.CorrelationId, 1, now,
                write.AccountingBasis, write.AccountingPolicyId, write.AccountingPolicyVersion, write.RuleId, write.RuleVersion,
                write.SourceEventId, write.SourceJournalEntryId, write.PostingKind, write.AdjustmentApproval));
            return Task.CompletedTask;
        });
        ((ILedgerPostingIdentityCollisionLookup)journals).FindPostingIdentityCollisionsAsync(Arg.Any<LedgerPostingIdentity>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(records.ToArray()));
        journals.GetByPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(records.ToArray()));
        var arrangements = Substitute.For<IIntercompanyAccountingArrangementStore>();
        var service = new IntercompanyAccountingArrangementService(arrangements,
            Substitute.For<IIntercompanyAccountingArrangementScopeAuthority>(), Substitute.For<IIntercompanyAccountingArrangementLegalAuthority>());
        var operational = new IntercompanyLedgerPostingAuthority(arrangements, service, Substitute.For<IIntercompanyAccountBindingStore>(),
            Substitute.For<IAccountingConfigurationStore>(), journals, Substitute.For<IFundProfileTenancyRegistry>());
        return new(new(new InMemoryManualJournalEntryDraftStore(), configuration, new InMemoryAccountingActionAuditStore(),
            journalStore: journals, intercompanyPostingAuthority: operational), draft, reviews, posted);
    }

    private sealed record CorrectionFixture(ManualJournalEntryWorkbenchService Service, ManualJournalEntryDraftDto Draft,
        List<LedgerJournalEntryWrite> Reviews, List<LedgerJournalEntryWrite> Posted);
}
