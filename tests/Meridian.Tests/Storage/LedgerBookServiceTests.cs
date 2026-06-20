using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Storage;

public sealed class LedgerBookServiceTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task CreateBookAsync_WhenScopeAlreadyExists_ReturnsExistingBook()
    {
        var store = new InMemoryLedgerJournalStore();
        var service = new PostgresLedgerBookService(store);
        var nodeId = Guid.NewGuid();
        var request = new CreateLedgerBookRequest(
            "alpha-fund",
            nodeId,
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "usd");

        var first = await service.CreateBookAsync(request);
        var second = await service.CreateBookAsync(request with { DisplayName = "Alpha Fund Duplicate" });

        second.LedgerBookId.Should().Be(first.LedgerBookId);
        second.DisplayName.Should().Be("Alpha Fund");
        (await service.ListBooksAsync(new LedgerBookQuery("alpha-fund", nodeId))).Should().ContainSingle();
    }

    [Fact]
    public async Task CreateBookAsync_AllowsParallelBooksForSameNodeByAccountingBasis()
    {
        var store = new InMemoryLedgerJournalStore();
        var service = new PostgresLedgerBookService(store);
        var nodeId = Guid.NewGuid();
        var primary = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            nodeId,
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"));
        var gaap = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            nodeId,
            FundStructureNodeKindDto.Fund,
            "Alpha Fund GAAP",
            "USD",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-default-v1",
            AccountingPolicyVersion: "v1"));

        gaap.LedgerBookId.Should().NotBe(primary.LedgerBookId);
        primary.AccountingBasis.Should().Be(AccountingBasisKindDto.Primary);
        gaap.AccountingBasis.Should().Be(AccountingBasisKindDto.Gaap);
        (await service.ListBooksAsync(new LedgerBookQuery("alpha-fund", nodeId))).Should().HaveCount(2);
        (await service.ListBooksAsync(new LedgerBookQuery("alpha-fund", nodeId, AccountingBasis: AccountingBasisKindDto.Gaap)))
            .Should()
            .ContainSingle(book => book.LedgerBookId == gaap.LedgerBookId);
    }

    [Fact]
    public async Task AppendAsync_WhenJournalBasisDiffersFromBookBasis_RejectsEntry()
    {
        var store = new InMemoryLedgerJournalStore();
        var service = new PostgresLedgerBookService(store);
        var book = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund GAAP",
            "USD",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-default-v1",
            AccountingPolicyVersion: "v1"));
        var period = await service.CreatePeriodAsync(new CreateLedgerPeriodRequest(
            book.LedgerBookId,
            2026,
            5,
            "2026-P05",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31)));

        var act = () => store.AppendAsync(BuildBalancedEntry(
            period.PeriodId,
            revenue: 1_200m,
            expense: 300m,
            timestamp: DateTimeOffset.Parse("2026-05-31T21:00:00Z")));

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*basis 'Primary'*basis 'Gaap'*");
    }

    [Fact]
    public async Task ClosePeriodAsync_SoftClose_PersistsSummaryAndPropagatesInboxWorkItem()
    {
        var store = new InMemoryLedgerJournalStore();
        var inbox = new InMemoryOperatorInboxService();
        var service = new PostgresLedgerBookService(store, inbox);
        await inbox.UpsertItemAsync(new OperatorWorkItemDto(
            WorkItemId: "reconciliation-break-alpha",
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: "Reconciliation break requires review",
            Detail: "Existing cash variance.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-01-15T10:00:00Z"),
            Workspace: "Accounting",
            TargetRoute: UiApiRoutes.ReconciliationBreakQueue,
            TargetPageTag: "FundReconciliation"));

        var book = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"));
        var previous = await store.SavePeriodAsync(new LedgerAccountingPeriod(
            Guid.NewGuid(),
            book.LedgerBookId,
            2026,
            1,
            "2026-P01",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            "Open",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            null,
            0), expectedVersion: 0);
        await store.AppendAsync(BuildBalancedEntry(
            previous.PeriodId,
            revenue: 800m,
            expense: 300m,
            timestamp: DateTimeOffset.Parse("2026-01-31T21:00:00Z")));
        await store.SavePeriodAsync(previous with
        {
            Status = "HardClosed",
            ClosedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
        }, expectedVersion: previous.Version);

        var current = await service.CreatePeriodAsync(new CreateLedgerPeriodRequest(
            book.LedgerBookId,
            2026,
            2,
            "2026-P02",
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 28)));
        await store.AppendAsync(BuildBalancedEntry(current.PeriodId, revenue: 1_200m, expense: 300m));

        var result = await service.ClosePeriodAsync(
            current.PeriodId,
            new CloseLedgerPeriodRequest(
                LedgerPeriodCloseKindDto.SoftClose,
                ClosedBy: "fund-controller",
                Notes: "Month-end soft close.",
                RequiredSignoffRole: "Fund Controller",
                ToleranceProfileId: "month-end-25bp"));

        result.Period.Status.Should().Be(LedgerPeriodStatusDto.SoftClosed);
        result.Summary.TotalDebits.Should().Be(1_500m);
        result.Summary.TotalCredits.Should().Be(1_500m);
        result.Summary.NetIncome.Should().Be(900m);
        result.Summary.PeriodOnPeriodVariance.Should().Be(400m);
        result.Summary.OpenBreakCount.Should().Be(1);
        result.Summary.SignoffStatus.Should().Be(LedgerPeriodSignoffStatusDto.Pending);
        result.Summary.TrialBalance.Should().Contain(row =>
            row.AccountName == "Management fees" &&
            row.AccountType == nameof(LedgerAccountType.Revenue) &&
            row.Balance == 1_200m);
        result.WorkItem.Kind.Should().Be(OperatorWorkItemKindDto.LedgerPeriodClose);
        result.WorkItem.TargetRoute.Should().Be(UiApiRoutes.ReconciliationBreakQueue);
        result.WorkItem.TargetPageTag.Should().Be("FundReconciliation");
        result.WorkItem.Detail.Should().Contain("Fund Controller");
        result.WorkItem.Detail.Should().Contain("month-end-25bp");
        result.WorkItem.Detail.Should().Contain("FundReconciliation");
        result.WorkItem.RequiredSignoffRole.Should().Be("Fund Controller");
        result.WorkItem.ToleranceProfileId.Should().Be("month-end-25bp");
        result.WorkItem.SignoffStatus.Should().Be(nameof(LedgerPeriodSignoffStatusDto.Pending));

        var contributedItems = await inbox.GetItemsAsync();
        contributedItems.Should().ContainSingle(item =>
            item.WorkItemId == result.WorkItem.WorkItemId &&
            item.Kind == OperatorWorkItemKindDto.LedgerPeriodClose &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "FundReconciliation" &&
            item.RequiredSignoffRole == "Fund Controller" &&
            item.ToleranceProfileId == "month-end-25bp");
    }

    [Fact]
    public async Task ClosePeriodAsync_WhenReviewedAutomationOrigin_RejectsBeforePeriodMutation()
    {
        var store = new InMemoryLedgerJournalStore();
        var inbox = new InMemoryOperatorInboxService();
        var service = new PostgresLedgerBookService(store, inbox);
        var book = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"));
        var period = await service.CreatePeriodAsync(new CreateLedgerPeriodRequest(
            book.LedgerBookId,
            2026,
            3,
            "2026-P03",
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31)));

        var act = () => service.ClosePeriodAsync(
            period.PeriodId,
            new CloseLedgerPeriodRequest(
                LedgerPeriodCloseKindDto.HardClose,
                ClosedBy: "assistant",
                ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        await act.Should().ThrowAsync<LedgerBookValidationException>()
            .WithMessage("*Reviewed automation cannot close ledger periods*");
        var retained = await store.GetPeriodAsync(period.PeriodId);
        retained.Should().NotBeNull();
        retained!.Status.Should().Be("Open");
        var inboxItems = await inbox.GetItemsAsync();
        inboxItems.Should().NotContain(item => item.Kind == OperatorWorkItemKindDto.LedgerPeriodClose);
    }

    [Fact]
    public async Task ClosePeriodAsync_WhenPeriodAlreadySoftClosed_RejectsSecondSoftClose()
    {
        var store = new InMemoryLedgerJournalStore();
        var service = new PostgresLedgerBookService(store);
        var book = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"));
        var period = await service.CreatePeriodAsync(new CreateLedgerPeriodRequest(
            book.LedgerBookId,
            2026,
            3,
            "2026-P03",
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31)));

        await service.ClosePeriodAsync(
            period.PeriodId,
            new CloseLedgerPeriodRequest(LedgerPeriodCloseKindDto.SoftClose, "fund-controller"));
        var act = () => service.ClosePeriodAsync(
            period.PeriodId,
            new CloseLedgerPeriodRequest(LedgerPeriodCloseKindDto.SoftClose, "fund-controller"));

        await act.Should().ThrowAsync<LedgerPeriodTransitionException>()
            .WithMessage("*Cannot transition*SoftClosed to SoftClosed*");
    }

    [Fact]
    public async Task AppendAsync_AfterSoftClose_AllowsOnlyAdjustmentPostingKind()
    {
        var store = new InMemoryLedgerJournalStore();
        var service = new PostgresLedgerBookService(store);
        var book = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"));
        var period = await service.CreatePeriodAsync(new CreateLedgerPeriodRequest(
            book.LedgerBookId,
            2026,
            6,
            "2026-P06",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30)));

        await service.ClosePeriodAsync(
            period.PeriodId,
            new CloseLedgerPeriodRequest(LedgerPeriodCloseKindDto.SoftClose, "fund-controller"));

        var originating = BuildBalancedEntry(
            period.PeriodId,
            revenue: 400m,
            expense: 100m,
            timestamp: DateTimeOffset.Parse("2026-06-30T21:00:00Z"));
        var adjustment = BuildBalancedEntry(
            period.PeriodId,
            revenue: 200m,
            expense: 50m,
            timestamp: DateTimeOffset.Parse("2026-06-30T21:00:00Z")) with
        {
            PostingKind = LedgerPostingKindDto.Adjustment,
            AdjustmentApproval = BuildApprovedAdjustmentApproval()
        };

        var originatingAct = () => store.AppendAsync(originating);
        var adjustmentAct = () => store.AppendAsync(adjustment);

        await originatingAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*soft-closed*Adjustment*");
        await adjustmentAct.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateBookAsync_WhenCanceled_PropagatesCancellation()
    {
        var service = new PostgresLedgerBookService(new InMemoryLedgerJournalStore());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LedgerEndpoints_CreateListAndClosePeriod_PropagatesCloseWorkItemToOperatorInbox()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var book = await PostJsonAsync<LedgerBookDto>(
            client,
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                Guid.Parse("59f045cb-f681-4b0c-943d-44c946f78214"),
                FundStructureNodeKindDto.Fund,
                "Alpha Fund",
                "USD"));
        var period = await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                book.LedgerBookId,
                2026,
                4,
                "2026-P04",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 4, 30)));

        var openPeriods = await client.GetFromJsonAsync<IReadOnlyList<LedgerPeriodDto>>(
            $"{UiApiRoutes.LedgerPeriods}?ledgerBookId={book.LedgerBookId:D}&openOnly=true",
            ServerJsonOptions);
        openPeriods.Should().ContainSingle(p => p.PeriodId == period.PeriodId);

        var closeRoute = UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodClose, "periodId", period.PeriodId.ToString());
        var close = await PostJsonAsync<LedgerPeriodCloseResultDto>(
            client,
            closeRoute,
            new CloseLedgerPeriodRequest(
                LedgerPeriodCloseKindDto.HardClose,
                ClosedBy: "fund-controller",
                RequiredSignoffRole: "Fund Controller",
                ToleranceProfileId: "close-tolerance-v1"));

        close.Period.Status.Should().Be(LedgerPeriodStatusDto.HardClosed);
        close.WorkItem.Kind.Should().Be(OperatorWorkItemKindDto.LedgerPeriodClose);
        close.WorkItem.TargetRoute.Should().Be(UiApiRoutes.ReconciliationBreakQueue);
        close.WorkItem.TargetPageTag.Should().Be("FundReconciliation");
        close.WorkItem.RequiredSignoffRole.Should().Be("Fund Controller");
        close.WorkItem.ToleranceProfileId.Should().Be("close-tolerance-v1");

        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            UiApiRoutes.WorkstationOperatorInbox,
            ServerJsonOptions);
        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.WorkItemId == close.WorkItem.WorkItemId &&
            item.Kind == OperatorWorkItemKindDto.LedgerPeriodClose &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "FundReconciliation" &&
            item.RequiredSignoffRole == "Fund Controller" &&
            item.ToleranceProfileId == "close-tolerance-v1");
    }

    [Fact]
    public async Task LedgerEndpoints_ClosePeriod_WhenReviewedAutomationOrigin_ReturnsBadRequestWithoutClosingPeriod()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var book = await PostJsonAsync<LedgerBookDto>(
            client,
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                Guid.Parse("59f045cb-f681-4b0c-943d-44c946f78214"),
                FundStructureNodeKindDto.Fund,
                "Alpha Fund",
                "USD"));
        var period = await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                book.LedgerBookId,
                2026,
                5,
                "2026-P05",
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31)));

        using var closeResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodClose, "periodId", period.PeriodId.ToString()),
            new CloseLedgerPeriodRequest(
                LedgerPeriodCloseKindDto.HardClose,
                ClosedBy: "assistant",
                ActionOrigin: OperationsActionOriginDto.AutomationAssistant),
            ServerJsonOptions);
        var openPeriods = await client.GetFromJsonAsync<IReadOnlyList<LedgerPeriodDto>>(
            $"{UiApiRoutes.LedgerPeriods}?ledgerBookId={book.LedgerBookId:D}&openOnly=true",
            ServerJsonOptions);
        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            UiApiRoutes.WorkstationOperatorInbox,
            ServerJsonOptions);

        closeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        openPeriods.Should().ContainSingle(item => item.PeriodId == period.PeriodId);
        inbox.Should().NotBeNull();
        inbox!.Items.Should().NotContain(item => item.Kind == OperatorWorkItemKindDto.LedgerPeriodClose);
    }

    [Fact]
    public async Task LedgerEndpoints_PeriodReportingRoutes_ReturnTrialBalanceAndPnlSummary()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<ILedgerJournalStore>();

        var book = await PostJsonAsync<LedgerBookDto>(
            client,
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                Guid.Parse("9dc7712e-8aa4-4c65-bc46-f6b8d0884695"),
                FundStructureNodeKindDto.Fund,
                "Alpha Fund",
                "USD",
                AccountingBasis: AccountingBasisKindDto.Gaap,
                AccountingPolicyId: "gaap-close-v1",
                AccountingPolicyVersion: "v1"));
        var prior = await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                book.LedgerBookId,
                2026,
                1,
                "2026-P01",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31)));
        await store.AppendAsync(BuildBalancedEntry(
            prior.PeriodId,
            revenue: 800m,
            expense: 300m,
            timestamp: DateTimeOffset.Parse("2026-01-31T21:00:00Z")) with
        {
            AccountingBasis = AccountingBasisKindDto.Gaap,
            AccountingPolicyId = "gaap-close-v1",
            AccountingPolicyVersion = "v1"
        });
        await PostJsonAsync<LedgerPeriodCloseResultDto>(
            client,
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodClose, "periodId", prior.PeriodId.ToString()),
            new CloseLedgerPeriodRequest(LedgerPeriodCloseKindDto.HardClose, ClosedBy: "fund-controller"));

        var current = await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                book.LedgerBookId,
                2026,
                2,
                "2026-P02",
                new DateOnly(2026, 2, 1),
                new DateOnly(2026, 2, 28)));
        await store.AppendAsync(BuildBalancedEntry(
            current.PeriodId,
            revenue: 1_200m,
            expense: 300m,
            timestamp: DateTimeOffset.Parse("2026-02-28T21:00:00Z")) with
        {
            AccountingBasis = AccountingBasisKindDto.Gaap,
            AccountingPolicyId = "gaap-close-v1",
            AccountingPolicyVersion = "v1"
        });
        await store.AppendAsync(BuildExpenseEntry(
            current.PeriodId,
            accountName: "Accrued performance fee expense",
            amount: 50m,
            timestamp: DateTimeOffset.Parse("2026-02-28T22:00:00Z")) with
        {
            AccountingBasis = AccountingBasisKindDto.Gaap,
            AccountingPolicyId = "gaap-close-v1",
            AccountingPolicyVersion = "v1"
        });
        await PostJsonAsync<LedgerPeriodCloseResultDto>(
            client,
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodClose, "periodId", current.PeriodId.ToString()),
            new CloseLedgerPeriodRequest(LedgerPeriodCloseKindDto.SoftClose, ClosedBy: "fund-controller"));

        var trialBalance = await client.GetFromJsonAsync<IReadOnlyList<LedgerPeriodTrialBalanceLineDto>>(
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodTrialBalance, "periodId", current.PeriodId.ToString()),
            ServerJsonOptions);
        var pnl = await client.GetFromJsonAsync<LedgerPeriodPnlSummaryDto>(
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodPnlSummary, "periodId", current.PeriodId.ToString()),
            ServerJsonOptions);
        var trialBalanceReport = await client.GetFromJsonAsync<LedgerTrialBalanceReportDto>(
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodTrialBalanceReport, "periodId", current.PeriodId.ToString()),
            ServerJsonOptions);

        trialBalance.Should().NotBeNull();
        trialBalance!.Should().Contain(row =>
            row.AccountName == "Management fees" &&
            row.AccountType == nameof(LedgerAccountType.Revenue) &&
            row.Balance == 1_200m &&
            row.AccountingBasis == AccountingBasisKindDto.Gaap &&
            row.AccountingPolicyId == "gaap-close-v1");
        pnl.Should().NotBeNull();
        pnl!.TotalRevenue.Should().Be(1_200m);
        pnl.TotalExpenses.Should().Be(350m);
        pnl.NetIncome.Should().Be(850m);
        pnl.PeriodOnPeriodVariance.Should().Be(350m);
        pnl.RealizedRevenue.Should().Be(1_200m);
        pnl.RealizedExpenses.Should().Be(300m);
        pnl.RealizedNetIncome.Should().Be(900m);
        pnl.AccrualAdjustmentRevenue.Should().Be(0m);
        pnl.AccrualAdjustmentExpenses.Should().Be(50m);
        pnl.AccrualBasisAdjustmentNetImpact.Should().Be(-50m);
        pnl.RevenueLines.Should().ContainSingle(row => row.AccountName == "Management fees");
        pnl.ExpenseLines.Should().Contain(row => row.AccountName == "Operating expense");
        pnl.AccrualAdjustmentLines.Should().ContainSingle(row => row.AccountName == "Accrued performance fee expense");
        trialBalanceReport.Should().NotBeNull();
        trialBalanceReport!.PeriodId.Should().Be(current.PeriodId);
        trialBalanceReport.LedgerBookId.Should().Be(book.LedgerBookId);
        trialBalanceReport.IsPeriodLocked.Should().BeTrue();
        trialBalanceReport.TotalDebits.Should().Be(1_550m);
        trialBalanceReport.TotalCredits.Should().Be(1_550m);
        trialBalanceReport.NetIncome.Should().Be(850m);
        trialBalanceReport.PeriodOnPeriodVariance.Should().Be(350m);
        trialBalanceReport.AccountingBasis.Should().Be(AccountingBasisKindDto.Gaap);
        trialBalanceReport.AccountingPolicyId.Should().Be("gaap-close-v1");
        trialBalanceReport.Lines.Should().Contain(row =>
            row.AccountName == "Management fees" &&
            row.AccountType == nameof(LedgerAccountType.Revenue) &&
            row.Balance == 1_200m);
        trialBalanceReport.Signature.Algorithm.Should().Be("SHA256");
        trialBalanceReport.Signature.PayloadChecksumSha256.Should().HaveLength(64);
        trialBalanceReport.Signature.SignedBy.Should().Be("fund-controller");
    }

    [Fact]
    public async Task LedgerEndpoints_PeriodReportingRoutes_RetainTrialBalanceDimensions()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<ILedgerJournalStore>();

        var book = await PostJsonAsync<LedgerBookDto>(
            client,
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                Guid.Parse("183ef413-9db7-4f43-bc12-bd336d509c2d"),
                FundStructureNodeKindDto.Fund,
                "Alpha Fund",
                "USD",
                AccountingBasis: AccountingBasisKindDto.Gaap,
                AccountingPolicyId: "gaap-close-v1",
                AccountingPolicyVersion: "v1"));
        var period = await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                book.LedgerBookId,
                2026,
                6,
                "2026-P06",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30)));

        await store.AppendAsync(BuildDimensionalRevenueEntry(
            period.PeriodId,
            amount: 100m,
            timestamp: DateTimeOffset.Parse("2026-06-30T21:00:00Z"),
            entityId: "entity-master",
            costCenterId: "cost-center-investment-ops",
            externalGlDepartment: "InvestmentOps") with
        {
            AccountingBasis = AccountingBasisKindDto.Gaap,
            AccountingPolicyId = "gaap-close-v1",
            AccountingPolicyVersion = "v1"
        });
        await store.AppendAsync(BuildDimensionalRevenueEntry(
            period.PeriodId,
            amount: 200m,
            timestamp: DateTimeOffset.Parse("2026-06-30T22:00:00Z"),
            entityId: "entity-parallel",
            costCenterId: "cost-center-fund-accounting",
            externalGlDepartment: "FundAccounting") with
        {
            AccountingBasis = AccountingBasisKindDto.Gaap,
            AccountingPolicyId = "gaap-close-v1",
            AccountingPolicyVersion = "v1"
        });
        await PostJsonAsync<LedgerPeriodCloseResultDto>(
            client,
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodClose, "periodId", period.PeriodId.ToString()),
            new CloseLedgerPeriodRequest(LedgerPeriodCloseKindDto.SoftClose, ClosedBy: "fund-controller"));

        var trialBalance = await client.GetFromJsonAsync<IReadOnlyList<LedgerPeriodTrialBalanceLineDto>>(
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodTrialBalance, "periodId", period.PeriodId.ToString()),
            ServerJsonOptions);
        var trialBalanceReport = await client.GetFromJsonAsync<LedgerTrialBalanceReportDto>(
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodTrialBalanceReport, "periodId", period.PeriodId.ToString()),
            ServerJsonOptions);
        var crossPeriodReport = await client.GetFromJsonAsync<LedgerCrossPeriodTrialBalanceReportDto>(
            $"{UiApiRoutes.LedgerReportsTrialBalance}?ledgerBookId={book.LedgerBookId:D}&accountingBasis=Gaap&startDate=2026-06-01&endDate=2026-06-30",
            ServerJsonOptions);
        var filteredCrossPeriodReport = await client.GetFromJsonAsync<LedgerCrossPeriodTrialBalanceReportDto>(
            $"{UiApiRoutes.LedgerReportsTrialBalance}?ledgerBookId={book.LedgerBookId:D}&accountingBasis=Gaap&startDate=2026-06-01&endDate=2026-06-30&entityId=entity-master&costCenterId=cost-center-investment-ops&externalGl.Department=InvestmentOps",
            ServerJsonOptions);
        var filteredPnlReport = await client.GetFromJsonAsync<LedgerCrossPeriodPnlReportDto>(
            $"{UiApiRoutes.LedgerReportsPnlSummary}?ledgerBookId={book.LedgerBookId:D}&accountingBasis=Gaap&startDate=2026-06-01&endDate=2026-06-30&entityId=entity-master&costCenterId=cost-center-investment-ops&externalGl.Department=InvestmentOps",
            ServerJsonOptions);

        trialBalance.Should().NotBeNull();
        var revenueRows = trialBalance!
            .Where(row => row.AccountName == "Management fees")
            .ToArray();
        revenueRows.Should().HaveCount(2);
        revenueRows.Should().Contain(row =>
            row.Balance == 100m &&
            row.Dimensions != null &&
            row.Dimensions.FundId == "alpha-fund" &&
            row.Dimensions.EntityId == "entity-master" &&
            row.Dimensions.CostCenterId == "cost-center-investment-ops" &&
            row.Dimensions.ExternalGlDimensions["Department"] == "InvestmentOps");
        revenueRows.Should().Contain(row =>
            row.Balance == 200m &&
            row.Dimensions != null &&
            row.Dimensions.FundId == "alpha-fund" &&
            row.Dimensions.EntityId == "entity-parallel" &&
            row.Dimensions.CostCenterId == "cost-center-fund-accounting" &&
            row.Dimensions.ExternalGlDimensions["Department"] == "FundAccounting");

        trialBalanceReport.Should().NotBeNull();
        trialBalanceReport!.Lines
            .Where(row => row.AccountName == "Management fees")
            .Should()
            .HaveCount(2);
        trialBalanceReport.Signature.PayloadChecksumSha256.Should().HaveLength(64);

        crossPeriodReport.Should().NotBeNull();
        crossPeriodReport!.Lines.Should().Contain(row =>
            row.PeriodId == period.PeriodId &&
            row.AccountName == "Management fees" &&
            row.Balance == 100m &&
            row.Dimensions != null &&
            row.Dimensions.EntityId == "entity-master" &&
            row.Dimensions.ExternalGlDimensions["Department"] == "InvestmentOps");
        crossPeriodReport.Lines.Should().Contain(row =>
            row.PeriodId == period.PeriodId &&
            row.AccountName == "Management fees" &&
            row.Balance == 200m &&
            row.Dimensions != null &&
            row.Dimensions.EntityId == "entity-parallel" &&
            row.Dimensions.ExternalGlDimensions["Department"] == "FundAccounting");

        filteredCrossPeriodReport.Should().NotBeNull();
        filteredCrossPeriodReport!.Lines.Should().ContainSingle(row =>
            row.PeriodId == period.PeriodId &&
            row.AccountName == "Management fees" &&
            row.Balance == 100m &&
            row.Dimensions != null &&
            row.Dimensions.EntityId == "entity-master" &&
            row.Dimensions.CostCenterId == "cost-center-investment-ops" &&
            row.Dimensions.ExternalGlDimensions["Department"] == "InvestmentOps");
        filteredCrossPeriodReport.TotalDebits.Should().Be(0m);
        filteredCrossPeriodReport.TotalCredits.Should().Be(100m);
        filteredCrossPeriodReport.NetIncome.Should().Be(100m);

        filteredPnlReport.Should().NotBeNull();
        filteredPnlReport!.Periods.Should().ContainSingle(periodSummary =>
            periodSummary.PeriodId == period.PeriodId &&
            periodSummary.TotalRevenue == 100m &&
            periodSummary.TotalExpenses == 0m &&
            periodSummary.NetIncome == 100m);
        filteredPnlReport.TotalRevenue.Should().Be(100m);
        filteredPnlReport.NetIncome.Should().Be(100m);
    }

    [Fact]
    public async Task LedgerEndpoints_CrossPeriodReportRoutes_ReturnClosedPeriodTrialBalanceAndPnlReports()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<ILedgerJournalStore>();

        var book = await PostJsonAsync<LedgerBookDto>(
            client,
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                Guid.Parse("6575e74d-5665-4f7c-a5d1-2d8f205478ab"),
                FundStructureNodeKindDto.Fund,
                "Alpha Fund",
                "USD",
                AccountingBasis: AccountingBasisKindDto.Gaap,
                AccountingPolicyId: "gaap-close-v1",
                AccountingPolicyVersion: "v1"));

        var first = await CreatePeriodWithPostingAsync(
            client,
            store,
            book,
            fiscalYear: 2026,
            periodNo: 1,
            label: "2026-P01",
            start: new DateOnly(2026, 1, 1),
            end: new DateOnly(2026, 1, 31),
            revenue: 800m,
            expense: 300m,
            closeKind: LedgerPeriodCloseKindDto.HardClose);
        var second = await CreatePeriodWithPostingAsync(
            client,
            store,
            book,
            fiscalYear: 2026,
            periodNo: 2,
            label: "2026-P02",
            start: new DateOnly(2026, 2, 1),
            end: new DateOnly(2026, 2, 28),
            revenue: 1_200m,
            expense: 300m,
            closeKind: LedgerPeriodCloseKindDto.SoftClose);
        await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                book.LedgerBookId,
                2026,
                3,
                "2026-P03",
                new DateOnly(2026, 3, 1),
                new DateOnly(2026, 3, 31)));

        var query = $"?ledgerBookId={book.LedgerBookId:D}&accountingBasis=Gaap&startDate=2026-01-01&endDate=2026-02-28";
        var trialBalance = await client.GetFromJsonAsync<LedgerCrossPeriodTrialBalanceReportDto>(
            UiApiRoutes.LedgerReportsTrialBalance + query,
            ServerJsonOptions);
        var pnl = await client.GetFromJsonAsync<LedgerCrossPeriodPnlReportDto>(
            UiApiRoutes.LedgerReportsPnlSummary + query,
            ServerJsonOptions);

        trialBalance.Should().NotBeNull();
        trialBalance!.Periods.Select(period => period.PeriodId).Should().Equal(first.PeriodId, second.PeriodId);
        trialBalance.Lines.Should().Contain(line =>
            line.PeriodId == first.PeriodId &&
            line.AccountName == "Management fees" &&
            line.Balance == 800m &&
            line.AccountingBasis == AccountingBasisKindDto.Gaap);
        trialBalance.Lines.Should().Contain(line =>
            line.PeriodId == second.PeriodId &&
            line.AccountName == "Operating expense" &&
            line.Balance == 300m);
        trialBalance.TotalDebits.Should().Be(2_600m);
        trialBalance.TotalCredits.Should().Be(2_600m);
        trialBalance.NetIncome.Should().Be(1_400m);

        pnl.Should().NotBeNull();
        pnl!.Periods.Select(period => period.PeriodId).Should().Equal(first.PeriodId, second.PeriodId);
        pnl.TotalRevenue.Should().Be(2_000m);
        pnl.TotalExpenses.Should().Be(600m);
        pnl.NetIncome.Should().Be(1_400m);
        pnl.Periods[1].PeriodOnPeriodVariance.Should().Be(400m);
    }

    [Fact]
    public async Task LedgerEndpoints_CrossPeriodReportRoutes_RejectInvalidDateRange()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var response = await client.GetAsync($"{UiApiRoutes.LedgerReportsPnlSummary}?startDate=2026-03-01&endDate=2026-02-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LedgerEndpoints_CreateBook_WhenUserLacksLedgerMutationPermission_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                Guid.Parse("59f045cb-f681-4b0c-943d-44c946f78214"),
                FundStructureNodeKindDto.Fund,
                "Alpha Fund",
                "USD"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LedgerEndpoints_ListBooks_WhenUserLacksLedgerReadPermission_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.LedgerBooks);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LedgerEndpoints_ListBooksAndPeriods_FilterByAccountingBasis()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var nodeId = Guid.Parse("4b3b9f1f-9637-41a9-947e-42b4a4dc91fc");

        await PostJsonAsync<LedgerBookDto>(
            client,
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                nodeId,
                FundStructureNodeKindDto.Fund,
                "Alpha Fund",
                "USD"));
        var gaapBook = await PostJsonAsync<LedgerBookDto>(
            client,
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                nodeId,
                FundStructureNodeKindDto.Fund,
                "Alpha Fund GAAP",
                "USD",
                AccountingBasis: AccountingBasisKindDto.Gaap,
                AccountingPolicyId: "gaap-default-v1",
                AccountingPolicyVersion: "v1"));
        var gaapPeriod = await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                gaapBook.LedgerBookId,
                2026,
                6,
                "2026-P06",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30)));

        var books = await client.GetFromJsonAsync<IReadOnlyList<LedgerBookDto>>(
            $"{UiApiRoutes.LedgerBooks}?fundProfileId=alpha-fund&fundStructureNodeId={nodeId:D}&accountingBasis=Gaap",
            ServerJsonOptions);
        var periods = await client.GetFromJsonAsync<IReadOnlyList<LedgerPeriodDto>>(
            $"{UiApiRoutes.LedgerPeriods}?fundProfileId=alpha-fund&fundStructureNodeId={nodeId:D}&accountingBasis=Gaap",
            ServerJsonOptions);

        books.Should().NotBeNull();
        books!.Should().ContainSingle(book => book.LedgerBookId == gaapBook.LedgerBookId && book.AccountingBasis == AccountingBasisKindDto.Gaap);
        periods.Should().NotBeNull();
        periods!.Should().ContainSingle(period => period.PeriodId == gaapPeriod.PeriodId && period.AccountingBasis == AccountingBasisKindDto.Gaap);
    }

    [Fact]
    public async Task OperatorInbox_ShouldAttachLedgerPeriodCloseNavigationWhenContributedItemIsSparse()
    {
        await using var app = await CreateAppAsync();
        var inboxService = app.Services.GetRequiredService<IOperatorInboxService>();
        await inboxService.UpsertItemAsync(new OperatorWorkItemDto(
            WorkItemId: "ledger-period-close-sparse",
            Kind: OperatorWorkItemKindDto.LedgerPeriodClose,
            Label: "SoftClosed sign-off required",
            Detail: "Period close requires controller sign-off.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-04-30T16:00:00Z")));

        var inbox = await app.GetTestClient().GetFromJsonAsync<OperatorInboxDto>(
            UiApiRoutes.WorkstationOperatorInbox,
            ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.WorkItemId == "ledger-period-close-sparse" &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "FundReconciliation" &&
            item.Workspace == "Accounting");
    }

    [Fact]
    public void LedgerBookMigration_DefinesLedgerBooksAndBookScopedPeriods()
    {
        var sql = ReadMigration("V_ledger_003__ledger_books.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.ledger_books");
        sql.Should().Contain("fund_structure_node_id uuid not null");
        sql.Should().Contain("add column if not exists ledger_book_id uuid null");
        sql.Should().Contain("ux_accounting_periods_book_fiscal_period");
    }

    [Fact]
    public void AccountingBasisPolicyMigration_DefinesParallelBookPolicyShape()
    {
        var sql = ReadMigration("V_ledger_004__accounting_basis_policies.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.accounting_policies");
        sql.Should().Contain("fund_structure_node_id uuid null");
        sql.Should().Contain("source_event_id uuid null");
        sql.Should().Contain("add column if not exists accounting_basis text not null default 'Primary'");
        sql.Should().Contain("add column if not exists accounting_policy_id text not null default 'legacy-v1'");
        sql.Should().Contain("ux_ledger_books_fund_node_basis");
        sql.Should().Contain("ix_accounting_policies_scope");
        sql.Should().Contain("'Gaap', 'gaap-default-v1', 'v1'");
        sql.Should().Contain("'Cash', 'cash-default-v1', 'v1'");
        sql.Should().Contain("'Tax', 'tax-default-v1', 'v1'");
        sql.Should().Contain("'Statutory', 'stat-default-v1', 'v1'");
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string route, object request)
    {
        using var response = await client.PostAsJsonAsync(route, request, ServerJsonOptions);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<T>(ServerJsonOptions);
        payload.Should().NotBeNull();
        return payload!;
    }

    private static async Task<WebApplication> CreateAppAsync(
        UserPermission permissions = UserPermission.ViewTrades | UserPermission.ManageDirectLending)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddRateLimiter(options =>
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter("ledger-tests")));
        builder.Services.AddSingleton<IOperatorInboxService, InMemoryOperatorInboxService>();
        builder.Services.AddSingleton<ILedgerJournalStore, InMemoryLedgerJournalStore>();
        builder.Services.AddSingleton<ILedgerBookService>(sp =>
            new PostgresLedgerBookService(
                sp.GetRequiredService<ILedgerJournalStore>(),
                sp.GetRequiredService<IOperatorInboxService>()));

        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "fund-controller";
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Accounting;
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            return next();
        });
        app.UseRateLimiter();
        app.MapLedgerEndpoints(ServerJsonOptions);
        app.MapWorkstationEndpoints(ServerJsonOptions);

        await app.StartAsync();
        return app;
    }

    private static async Task<LedgerPeriodDto> CreatePeriodWithPostingAsync(
        HttpClient client,
        ILedgerJournalStore store,
        LedgerBookDto book,
        int fiscalYear,
        int periodNo,
        string label,
        DateOnly start,
        DateOnly end,
        decimal revenue,
        decimal expense,
        LedgerPeriodCloseKindDto closeKind)
    {
        var period = await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                book.LedgerBookId,
                fiscalYear,
                periodNo,
                label,
                start,
                end));
        await store.AppendAsync(BuildBalancedEntry(
            period.PeriodId,
            revenue,
            expense,
            new DateTimeOffset(end.ToDateTime(new TimeOnly(21, 0)), TimeSpan.Zero)) with
        {
            AccountingBasis = book.AccountingBasis,
            AccountingPolicyId = book.AccountingPolicyId,
            AccountingPolicyVersion = book.AccountingPolicyVersion
        });
        await PostJsonAsync<LedgerPeriodCloseResultDto>(
            client,
            UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodClose, "periodId", period.PeriodId.ToString()),
            new CloseLedgerPeriodRequest(closeKind, ClosedBy: "fund-controller"));

        return period;
    }

    private static LedgerJournalEntryWrite BuildBalancedEntry(
        Guid periodId,
        decimal revenue,
        decimal expense,
        DateTimeOffset? timestamp = null)
    {
        var journalEntryId = Guid.NewGuid();
        var occurredAt = timestamp ?? DateTimeOffset.Parse("2026-02-28T21:00:00Z");
        const string description = "Month-end revenue and expense posting";
        var lines = new[]
        {
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                occurredAt,
                new LedgerAccount("Cash", LedgerAccountType.Asset),
                debit: revenue,
                credit: 0m,
                description),
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                occurredAt,
                new LedgerAccount("Management fees", LedgerAccountType.Revenue),
                debit: 0m,
                credit: revenue,
                description),
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                occurredAt,
                new LedgerAccount("Operating expense", LedgerAccountType.Expense),
                debit: expense,
                credit: 0m,
                description),
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                occurredAt,
                new LedgerAccount("Cash", LedgerAccountType.Asset),
                debit: 0m,
                credit: expense,
                description)
        };

        return new LedgerJournalEntryWrite(
            new JournalEntry(journalEntryId, occurredAt, description, lines),
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId);
    }

    private static LedgerJournalEntryWrite BuildExpenseEntry(
        Guid periodId,
        string accountName,
        decimal amount,
        DateTimeOffset timestamp)
    {
        var journalEntryId = Guid.NewGuid();
        var description = $"{accountName} accrual adjustment";
        var lines = new[]
        {
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount(accountName, LedgerAccountType.Expense),
                debit: amount,
                credit: 0m,
                description),
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount("Accrued liabilities", LedgerAccountType.Liability),
                debit: 0m,
                credit: amount,
                description)
        };

        return new LedgerJournalEntryWrite(
            new JournalEntry(journalEntryId, timestamp, description, lines),
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId);
    }

    private static LedgerJournalEntryWrite BuildDimensionalRevenueEntry(
        Guid periodId,
        decimal amount,
        DateTimeOffset timestamp,
        string entityId,
        string costCenterId,
        string externalGlDepartment)
    {
        var journalEntryId = Guid.NewGuid();
        const string description = "Dimension-scoped revenue posting";
        var lines = new[]
        {
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount("Cash", LedgerAccountType.Asset),
                debit: amount,
                credit: 0m,
                description),
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount("Management fees", LedgerAccountType.Revenue),
                debit: 0m,
                credit: amount,
                description,
                new LedgerLineDimensionSet(
                    EntityId: entityId,
                    CostCenterId: costCenterId,
                    ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Department"] = externalGlDepartment
                    }))
        };
        var metadata = new JournalEntryMetadata(
            StrategyId: "strategy-direct-lending",
            FinancialAccountId: "operating-cash",
            CounterpartyAccountId: "counterparty-bank");

        return new LedgerJournalEntryWrite(
            new JournalEntry(journalEntryId, timestamp, description, lines, metadata),
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId);
    }

    private static LedgerAdjustmentApprovalMetadataDto BuildApprovedAdjustmentApproval() =>
        new(
            ApprovalId: "approval-ledger-adjustment-1",
            Status: LedgerAdjustmentApprovalStatusDto.Approved,
            ApprovedBy: "fund-controller",
            ApprovedAt: DateTimeOffset.Parse("2026-06-30T22:00:00Z"),
            ReasonCode: "month-end-true-up",
            GovernanceCaseId: "case-ledger-close-1",
            EvidenceLink: "evidence://ledger/adjustment/approval-1",
            Notes: "Controller approved soft-close true-up.");

    private static string ReadMigration(string fileName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "Ledger", "Migrations", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Meridian.Storage")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }

    private sealed class InMemoryLedgerJournalStore : ILedgerJournalStore
    {
        private readonly Dictionary<Guid, LedgerBookRecord> _books = [];
        private readonly Dictionary<Guid, LedgerAccountingPeriod> _periods = [];
        private readonly Dictionary<Guid, List<LedgerJournalEntryRecord>> _entriesByPeriod = [];
        private long _sequence;

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_periods.TryGetValue(entry.PeriodId, out var period))
            {
                throw new LedgerValidationException($"Accounting period '{entry.PeriodId}' was not found.");
            }

            LedgerPeriodPostingGuard.Validate(entry, period);

            if (period.LedgerBookId is { } ledgerBookId &&
                _books.TryGetValue(ledgerBookId, out var book) &&
                book.AccountingBasis != entry.AccountingBasis)
            {
                throw new LedgerValidationException(
                    $"Journal entry '{entry.Entry.JournalEntryId}' basis '{entry.AccountingBasis}' does not match ledger book '{book.DisplayName}' basis '{book.AccountingBasis}'.");
            }

            if (!_entriesByPeriod.TryGetValue(entry.PeriodId, out var entries))
            {
                entries = [];
                _entriesByPeriod[entry.PeriodId] = entries;
            }

            entries.Add(new LedgerJournalEntryRecord(
                entry.Entry,
                entry.AggregateId,
                entry.PeriodId,
                entry.CommandId,
                entry.CorrelationId,
                ++_sequence,
                DateTimeOffset.UtcNow,
                entry.AccountingBasis,
                entry.AccountingPolicyId,
                entry.AccountingPolicyVersion,
                entry.RuleId,
                entry.RuleVersion,
                entry.SourceEventId,
                entry.SourceJournalEntryId,
                entry.PostingKind,
                entry.AdjustmentApproval));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _entriesByPeriod.TryGetValue(periodId, out var entries)
                    ? entries.ToArray()
                    : []);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var records = _entriesByPeriod.Values
                .SelectMany(static entries => entries)
                .Where(entry => entry.AggregateId == aggregateId)
                .ToArray();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(records);
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_periods.GetValueOrDefault(periodId));
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<LedgerAccountingPeriod> periods = _periods.Values;
            if (ledgerBookId.HasValue)
            {
                periods = periods.Where(period => period.LedgerBookId == ledgerBookId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                periods = periods.Where(period => string.Equals(period.Status, status, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(fundProfileId) || fundStructureNodeId.HasValue)
            {
                periods = periods.Where(period =>
                    period.LedgerBookId is { } id &&
                    _books.TryGetValue(id, out var book) &&
                    (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                    (!fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value));
            }

            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(
                periods
                    .OrderBy(static period => period.StartDate)
                    .ThenBy(static period => period.PeriodNo)
                    .ToArray());
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_periods.TryGetValue(period.PeriodId, out var current))
            {
                if (current.Version != expectedVersion)
                {
                    throw new InvalidOperationException("Simulated period version conflict.");
                }

                var updated = period with { Version = expectedVersion + 1 };
                _periods[period.PeriodId] = updated;
                return Task.FromResult(updated);
            }

            if (expectedVersion != 0)
            {
                throw new InvalidOperationException("Simulated period version conflict.");
            }

            var saved = period with { Version = 1 };
            _periods[period.PeriodId] = saved;
            return Task.FromResult(saved);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_books.GetValueOrDefault(ledgerBookId));
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<LedgerBookRecord> books = _books.Values;
            if (!string.IsNullOrWhiteSpace(fundProfileId))
            {
                books = books.Where(book => string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase));
            }

            if (fundStructureNodeId.HasValue)
            {
                books = books.Where(book => book.FundStructureNodeId == fundStructureNodeId.Value);
            }

            if (fundStructureNodeKind.HasValue)
            {
                books = books.Where(book => book.FundStructureNodeKind == fundStructureNodeKind.Value);
            }

            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>(
                books.OrderBy(static book => book.DisplayName, StringComparer.Ordinal).ToArray());
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _books[book.LedgerBookId] = book;
            return Task.FromResult(book);
        }
    }
}
