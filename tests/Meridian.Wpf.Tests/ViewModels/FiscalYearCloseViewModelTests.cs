using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.Accounting;
using NSubstitute;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FiscalYearCloseViewModelTests
{
    [Fact]
    public async Task ReadyPlanWithoutControllerAuthorityRemainsReadOnly()
    {
        var (model, _, _) = await ReviewAsync(canFinalize: false);
        model.Reason = "Reviewed fiscal balances";

        Assert.Contains("controller authorization", model.StatusText);
        Assert.Equal(5, model.BalanceSections.Count);
        Assert.False(model.FinalizeCommand.CanExecute(null));
    }

    [Fact]
    public async Task UncertainCloseRetriesTheSameRequestAndRefreshesTheRetainedReceipt()
    {
        var (model, api, plan) = await ReviewAsync(canFinalize: true);
        var requests = new List<FinalizeFiscalYearCloseRequestDto>();
        api.FinalizeCloseAsync(plan.LedgerBookId, plan.FiscalYear, Arg.Any<FinalizeFiscalYearCloseRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<FinalizeFiscalYearCloseRequestDto>();
                requests.Add(request);
                return requests.Count == 1
                    ? Task.FromException<FiscalYearCloseReceiptDto>(new IOException("Connection lost"))
                    : Task.FromResult(new FiscalYearCloseReceiptDto(request.CommandId, null, DateTimeOffset.UnixEpoch,
                        "Controller", "Controller", request.Reason, plan, "USD", AccountingBasisKindDto.Gaap,
                        "test-policy", "v1", []));
            });
        model.Reason = "Reviewed fiscal balances";

        await model.FinalizeCommand.ExecuteAsync(null);
        Assert.Equal("Connection lost", model.StatusText);
        await model.FinalizeCommand.ExecuteAsync(null);

        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0], requests[1]);
        Assert.Equal(plan.PlanHashSha256, requests[0].ExpectedPlanHashSha256);
        Assert.Equal(requests[0].CommandId, model.DisplayedReceipt?.ReceiptId);
        Assert.False(model.FinalizeCommand.CanExecute(null));
    }

    private static async Task<(FiscalYearCloseViewModel Model, IWorkstationFiscalYearCloseApiClient Api, FiscalYearClosePlanDto Plan)>
        ReviewAsync(bool canFinalize)
    {
        var bookId = Guid.NewGuid();
        var book = new LedgerBookDto(bookId, "test-fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Test GAAP",
            "USD", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, AccountingBasis: AccountingBasisKindDto.Gaap);
        var plan = new FiscalYearClosePlanDto(bookId, 2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            true, [], [], [], [], [], [], [], 100m, 1000m, 1000m, 1100m, new string('a', 64))
        {
            CanFinalizeFiscalYearClose = canFinalize
        };
        var ledger = Substitute.For<ILedgerReportsApiClient>();
        ledger.GetBooksAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ApiResponse<List<LedgerBookDto>>.Ok([book])));
        var close = Substitute.For<IWorkstationFiscalYearCloseApiClient>();
        close.GetClosePlanAsync(bookId, 2026, Arg.Any<CancellationToken>()).Returns(Task.FromResult(plan));
        close.GetCloseReceiptAsync(bookId, 2026, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<FiscalYearCloseReceiptDto?>(null));
        var model = new FiscalYearCloseViewModel(ledger, close);

        await model.LoadBooksCommand.ExecuteAsync(null);
        model.FiscalYearText = "2026";
        await model.ReviewCommand.ExecuteAsync(null);
        return (model, close, plan);
    }
}
