using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FundAccounts;
using Moq;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

public sealed class PostgresFundAccountServiceTests
{
    [Fact]
    public async Task CreateAccountAsync_AtomicCreateRefused_ThrowsWithoutUpserting()
    {
        var store = new Mock<IFundAccountStore>(MockBehavior.Strict);
        var request = MakeAccountRequest();
        store.Setup(s => s.TryCreateAccountAsync(It.Is<AccountSummaryDto>(a => a.AccountId == request.AccountId), default))
            .ReturnsAsync(false);
        var service = new PostgresFundAccountService(store.Object);

        var act = () => service.CreateAccountAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"Account {request.AccountId} already exists.");
        store.VerifyAll();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordBalanceSnapshotAsync_FundAccount_RetainsFundScope()
    {
        var store = new Mock<IFundAccountStore>(MockBehavior.Strict);
        var account = MakeAccount();
        store.Setup(s => s.GetAccountAsync(account.AccountId, default)).ReturnsAsync(account);
        store.Setup(s => s.InsertBalanceSnapshotAsync(It.Is<AccountBalanceSnapshotDto>(snapshot => snapshot.FundId == account.FundId), default))
            .Returns(Task.CompletedTask);
        var service = new PostgresFundAccountService(store.Object);

        var snapshot = await service.RecordBalanceSnapshotAsync(new(account.AccountId, new(2026, 10, 7), "USD", 125m, "manual"));

        snapshot.FundId.Should().Be(account.FundId);
        store.VerifyAll();
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("custodian")]
    [InlineData("bank")]
    [InlineData("reconcile")]
    [InlineData("sync")]
    [InlineData("margin")]
    public async Task ChildMutation_UnknownAccount_RejectsBeforeWriting(string operation)
    {
        var store = new Mock<IFundAccountStore>(MockBehavior.Strict);
        var accountId = Guid.NewGuid();
        store.Setup(s => s.GetAccountAsync(accountId, default)).ReturnsAsync((AccountSummaryDto?)null);
        var service = new PostgresFundAccountService(store.Object);

        var act = () => MutateAsync(service, operation, accountId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"Account {accountId} not found.");
        store.VerifyAll();
        store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update-custodian")]
    [InlineData("update-bank")]
    [InlineData("deactivate")]
    [InlineData("balance")]
    [InlineData("custodian")]
    [InlineData("bank")]
    [InlineData("reconcile")]
    [InlineData("sync")]
    [InlineData("margin")]
    public async Task Mutation_PreCancelledToken_DoesNotAccessStore(string operation)
    {
        var store = new Mock<IFundAccountStore>(MockBehavior.Strict);
        var service = new PostgresFundAccountService(store.Object);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var act = () => MutateAsync(service, operation, Guid.NewGuid(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IngestBankStatementAsync_ForeignAccountLine_RejectsBeforeAccessingStore()
    {
        var store = new Mock<IFundAccountStore>(MockBehavior.Strict);
        var service = new PostgresFundAccountService(store.Object);
        var batchId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 7);
        var line = new BankStatementLineDto(Guid.NewGuid(), batchId, Guid.NewGuid(), date, date, 50m, "USD", "credit", "Deposit", null, null);

        var act = () => service.IngestBankStatementAsync(new(batchId, Guid.NewGuid(), date, "Bank", null, [line], "ops"));

        await act.Should().ThrowAsync<ArgumentException>();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSyncHistoryAsync_CapabilityWithWhitespace_PassesNormalizedFilter()
    {
        var store = new Mock<IFundAccountStore>(MockBehavior.Strict);
        var accountId = Guid.NewGuid();
        store.Setup(s => s.GetSyncHistoryAsync(accountId, "balances", default)).ReturnsAsync(Array.Empty<AccountSyncHistoryEntryDto>());
        var service = new PostgresFundAccountService(store.Object);

        await service.GetSyncHistoryAsync(accountId, " balances ");
        await service.GetLatestSyncHistoryAsync(accountId, " balances ");

        store.Verify(s => s.GetSyncHistoryAsync(accountId, "balances", default), Times.Exactly(2));
        store.VerifyNoOtherCalls();
    }

    private static Task MutateAsync(PostgresFundAccountService service, string operation, Guid accountId, CancellationToken ct = default)
        => operation switch
        {
            "create" => service.CreateAccountAsync(MakeAccountRequest() with { AccountId = accountId }, ct),
            "update-custodian" => service.UpdateCustodianDetailsAsync(accountId, new(new(null, null, null, null, null, null, null, null), "ops"), ct),
            "update-bank" => service.UpdateBankDetailsAsync(accountId, new(new(null, null, null, null, null, null, null, null, null, null, null), "ops"), ct),
            "deactivate" => service.DeactivateAccountAsync(accountId, "ops", ct),
            "balance" => service.RecordBalanceSnapshotAsync(new(accountId, new(2026, 10, 7), "USD", 125m, "manual"), ct),
            "custodian" => service.IngestCustodianStatementAsync(new(Guid.NewGuid(), accountId, new(2026, 10, 7), "Custodian", "csv", null, [], "ops"), ct),
            "bank" => service.IngestBankStatementAsync(new(Guid.NewGuid(), accountId, new(2026, 10, 7), "Bank", null, [], "ops"), ct),
            "reconcile" => service.ReconcileAccountAsync(new(accountId, new(2026, 10, 7), "ops"), ct),
            "sync" => service.RecordSyncHistoryAsync(new(accountId, "balances", AccountSyncStatusDto.Succeeded), ct),
            "margin" => service.RecordMarginSnapshotAsync(new(accountId, DateTimeOffset.UtcNow, "USD", MarginModelTypeDto.RegT), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static CreateAccountRequest MakeAccountRequest()
        => new(Guid.NewGuid(), AccountTypeDto.Custody, $"CUST-{Guid.NewGuid():N}", "Custody", "USD", DateTimeOffset.UtcNow, "ops");

    private static AccountSummaryDto MakeAccount()
        => new(Guid.NewGuid(), AccountTypeDto.Custody, null, Guid.NewGuid(), null, null, "CUST-001", "Custody", "USD", "Custodian", true, DateTimeOffset.UtcNow, null, null, null, null, null);
}
