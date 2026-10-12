using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class AccountingPolicyAuthorityLeaseTests
{
    private static readonly AccountingPolicyQuery Query = new(AccountingBasisKindDto.Primary,
        new DateOnly(2026, 10, 1), "consolidation-v1");

    [Fact]
    public async Task AuthorityLease_PreservesReviewedPolicyUntilReleased()
    {
        var service = new AccountingPolicyService();
        var reviewed = await service.ResolvePolicyAsync(Query);
        await using var lease = await service.AcquireConsolidationAuthorityLeaseAsync();

        var mutation = service.CreatePolicyAsync(Replacement());

        Assert.False(mutation.IsCompleted);
        Assert.Same(reviewed, await service.ResolvePolicyAsync(Query));
        await lease.DisposeAsync();

        var replacement = await mutation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(replacement, await service.ResolvePolicyAsync(Query));
        Assert.NotEqual(reviewed.DisplayName, replacement.DisplayName);
    }

    [Fact]
    public async Task AuthorityLease_CanceledMutationCannotReplaceReviewedPolicy()
    {
        var service = new AccountingPolicyService();
        var reviewed = await service.ResolvePolicyAsync(Query);
        await using var lease = await service.AcquireConsolidationAuthorityLeaseAsync();
        using var cancellation = new CancellationTokenSource();

        var mutation = service.CreatePolicyAsync(Replacement(), cancellation.Token);
        Assert.False(mutation.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutation);
        Assert.Same(reviewed, await service.ResolvePolicyAsync(Query));
        await lease.DisposeAsync();

        var replacement = await service.CreatePolicyAsync(Replacement()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(replacement, await service.ResolvePolicyAsync(Query));
    }

    [Fact]
    public async Task AuthorityLease_CanceledAcquisitionDoesNotReleaseCurrentLease()
    {
        var service = new AccountingPolicyService();
        await using var lease = await service.AcquireConsolidationAuthorityLeaseAsync();
        using var cancellation = new CancellationTokenSource();

        var waitingLease = service.AcquireConsolidationAuthorityLeaseAsync(cancellation.Token);
        Assert.False(waitingLease.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingLease);

        var mutation = service.CreatePolicyAsync(Replacement());
        Assert.False(mutation.IsCompleted);
        await lease.DisposeAsync();
        await mutation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AuthorityLease_DoubleDisposalCannotReleaseAnotherLease()
    {
        var service = new AccountingPolicyService();
        var first = await service.AcquireConsolidationAuthorityLeaseAsync();
        await first.DisposeAsync();
        await using var second = await service.AcquireConsolidationAuthorityLeaseAsync();

        await first.DisposeAsync();
        var mutation = service.CreatePolicyAsync(Replacement());

        Assert.False(mutation.IsCompleted);
        await second.DisposeAsync();
        await mutation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreatePolicyAsync_InvalidPolicyDoesNotRetainAuthorityLease()
    {
        var service = new AccountingPolicyService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreatePolicyAsync(Replacement() with { DisplayName = " " }));

        await using var lease = await service.AcquireConsolidationAuthorityLeaseAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(await service.ResolvePolicyAsync(Query));
    }

    [Fact]
    public async Task AuthorityLease_UnsupportedProviderFailsClosed()
    {
        IAccountingPolicyService service = new UnsupportedPolicyProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcquireConsolidationAuthorityLeaseAsync());
    }

    private static CreateAccountingPolicyRequest Replacement() => new(
        AccountingBasisKindDto.Primary, "consolidation-v1", "w10-v1", "Changed consolidation policy",
        new DateOnly(1900, 1, 1));

    private sealed class UnsupportedPolicyProvider : IAccountingPolicyService
    {
        public Task<AccountingPolicyDto> CreatePolicyAsync(CreateAccountingPolicyRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AccountingPolicyDto> ResolvePolicyAsync(AccountingPolicyQuery query,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AccountingPolicyDto>> ListPoliciesAsync(
            AccountingBasisKindDto? accountingBasis = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
