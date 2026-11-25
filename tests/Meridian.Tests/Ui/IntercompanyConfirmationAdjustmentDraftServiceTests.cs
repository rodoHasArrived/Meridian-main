using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Moq;
using static Meridian.Tests.TestSupport.IntercompanyBalanceConfirmationFixture;

namespace Meridian.Tests.Ui;

/// <summary>
/// After the lender disputes June's pinned loan balance and a reviewer resolves it, the lender's controller pre-fills
/// a correction in the ordinary journal workbench (DEC-IC-CONFIRM-003). The draft is never submitted or posted here.
/// Synthetic fixture only.
/// </summary>
public sealed class IntercompanyConfirmationAdjustmentDraftServiceTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AResolvedDispute_PrefillsABalancedDraftOnTheSidesMappedAccount_CarryingTheDisputeLink()
    {
        using var fixture = await CreateAsync();
        var (service, drafts) = Adjustments(fixture);
        var resolved = await ResolvedAsync(fixture);

        var draft = await service.CreateDraftAsync(Tenant, Company, "lender-controller", resolved.ConfirmationId, Request(resolved, -250m));

        // The workbench treats the leg as intercompany and demands agreement authority before review; this harness has no
        // ledger writer, so that check fails closed. Production composition supplies it.
        Assert.Equal(["manual-je.agreement-authority-invalid"], draft.ValidationIssues.Select(issue => issue.Code).Distinct());
        Assert.Equal((LenderBook, MonthEnd), (draft.LedgerBookId, draft.AccountingDate));
        Assert.Equal([(AccountingTemplateLineSideDto.Credit, "a-recv", 250m), (AccountingTemplateLineSideDto.Debit, "a-suspense", 250m)],
            draft.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!, line.Amount)));
        var intercompany = draft.Lines[0].Dimensions!;
        Assert.Equal((Profile.ToString("D"), Borrower.ToString("D")), (intercompany.AccountingArrangementId, intercompany.CounterpartyId));
        Assert.StartsWith(IntercompanyConfirmationAdjustmentDraftService.SourcePrefix, intercompany.IntercompanyTransactionId);
        var resolution = resolved.Events.Last(item => item.Action == IntercompanyBalanceConfirmationActionDto.Resolve);
        Assert.Contains(draft.EvidenceLinks, link => link.EndsWith($"/resolution:{resolution.EventId:D}", StringComparison.Ordinal));
        Assert.Contains("Lender booked June interest twice.", draft.Memo);
        Assert.Null(draft.SubmittedAtUtc);
        Assert.NotNull(await drafts.GetAsync("lender-fund", draft.JournalEntryId, default, Tenant, Company));
    }

    [Fact]
    public async Task ARetryReturnsTheRetainedDraft_EvenAfterReviewerEdits()
    {
        using var fixture = await CreateAsync();
        var (service, drafts) = Adjustments(fixture);
        var resolved = await ResolvedAsync(fixture);
        var first = await service.CreateDraftAsync(Tenant, Company, "lender-controller", resolved.ConfirmationId, Request(resolved, -250m));

        var retry = await service.CreateDraftAsync(Tenant, Company, "lender-controller", resolved.ConfirmationId, Request(resolved, -999m));

        Assert.Equal((first.JournalEntryId, first.Version), (retry.JournalEntryId, retry.Version));
        Assert.Equal(250m, retry.Lines[0].Amount);
    }

    [Fact]
    public async Task OnlyAResolvedConfirmation_AtTheVersionShown_CanBeAdjusted()
    {
        using var fixture = await CreateAsync();
        var (service, _) = Adjustments(fixture);
        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", new(Guid.NewGuid(), Profile, MonthEnd));
        Assert.Contains("only after the dispute is resolved", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDraftAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId, Request(proposed, -250m)))).Message);

        var resolved = await ResolvedAsync(fixture, proposed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDraftAsync(Tenant, Company, "lender-controller",
            resolved.ConfirmationId, Request(resolved, -250m) with { ExpectedVersion = resolved.Version - 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDraftAsync(Tenant, Company, "lender-controller",
            resolved.ConfirmationId, Request(resolved, 0m)));
        // An intercompany account cannot be the offset: the counterpart corrects its own book.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDraftAsync(Tenant, Company, "lender-controller",
            resolved.ConfirmationId, Request(resolved, -250m) with { OffsetAccountNodeId = "a-recv" }));
    }

    [Fact]
    public async Task AnotherCompanysSide_IsNotAdjustedFromHere()
    {
        using var fixture = await CreateAsync();
        var (service, _) = Adjustments(fixture);
        var resolved = await ResolvedAsync(fixture);
        fixture.Owners["borrower-fund"] = new FundProfileOwnership("borrower-fund", Tenant, "company-b");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateDraftAsync(Tenant, Company, "lender-controller",
            resolved.ConfirmationId, Request(resolved, 250m) with { Party = IntercompanyBalanceConfirmationPartyDto.PartyTwo, Role = IntercompanyAccountRoleDto.Payable }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateDraftAsync(Tenant, "company-c", "outsider",
            resolved.ConfirmationId, Request(resolved, -250m)));
    }

    private static async Task<IntercompanyBalanceConfirmationDto> ResolvedAsync(IntercompanyBalanceConfirmationFixture fixture,
        IntercompanyBalanceConfirmationDto? proposed = null)
    {
        proposed ??= await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", new(Guid.NewGuid(), Profile, MonthEnd));
        var disputed = await fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId,
            new(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Dispute, IntercompanyBalanceConfirmationPartyDto.PartyOne,
                proposed.Version, proposed.BalanceDigest, "Receivable overstated by 250."));
        return await fixture.Service.ActAsync(Tenant, Company, "group-controller", proposed.ConfirmationId,
            new(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Resolve, null, disputed.Version, disputed.BalanceDigest,
                "Lender booked June interest twice."));
    }

    private static CreateIntercompanyConfirmationAdjustmentDraftRequest Request(IntercompanyBalanceConfirmationDto current, decimal amount)
        => new(Guid.NewGuid(), IntercompanyBalanceConfirmationPartyDto.PartyOne, current.Version, current.BalanceDigest,
            IntercompanyAccountRoleDto.Receivable, amount, "a-suspense", MonthEnd);

    private static (IntercompanyConfirmationAdjustmentDraftService Service, InMemoryManualJournalEntryDraftStore Drafts) Adjustments(
        IntercompanyBalanceConfirmationFixture fixture)
    {
        fixture.Journals.Setup(item => item.ListPeriodsAsync(LenderBook, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LedgerAccountingPeriod(Guid.NewGuid(), LenderBook, 2026, 6, "June", new(2026, 6, 1), MonthEnd, "Open", At, null, 1)]);
        IReadOnlyList<ChartOfAccountsNodeDto> chart =
        [
            new("a-recv", "Assets:Intercompany receivable", "Intercompany receivable", "Asset", IntercompanyRole: IntercompanyAccountRoleDto.Receivable),
            new("a-pay", "Liabilities:Intercompany payable", "Intercompany payable", "Liability", IntercompanyRole: IntercompanyAccountRoleDto.Payable),
            new("a-suspense", "Assets:Suspense", "Suspense", "Asset")
        ];
        var bindings = new Mock<IIntercompanyAccountBindingStore>();
        bindings.Setup(item => item.ListAsync(LenderBook, MonthEnd, It.IsAny<CancellationToken>())).ReturnsAsync(chart
            .Where(account => account.IntercompanyRole is not null)
            .Select(account => new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, LenderBook, account.NodeId,
                account.IntercompanyRole!.Value, account.Path, account.AccountName, account.AccountType, new(2026, 1, 1), null,
                [], Tenant, Company, "reviewer", At, "Synthetic fixture")).ToArray());
        var configuration = new Mock<IAccountingConfigurationService>();
        configuration.Setup(item => item.GetWorkspaceAsync("lender-fund", LenderBook, It.IsAny<CancellationToken>(), Tenant, Company))
            .ReturnsAsync(new AccountingConfigurationWorkspaceDto("lender-fund", LenderBook, default, "v1", At, [], chart, [], [], [], []));
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration.Object, new InMemoryAccountingActionAuditStore());
        return (new IntercompanyConfirmationAdjustmentDraftService(fixture.Service, fixture.Journals.Object, bindings.Object,
            configuration.Object, drafts, workbench), drafts);
    }
}
