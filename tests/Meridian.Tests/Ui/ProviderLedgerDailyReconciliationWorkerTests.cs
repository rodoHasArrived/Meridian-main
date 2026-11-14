using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Tenancy;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ProviderLedgerDailyReconciliationWorkerTests
{
    private static readonly ProviderLedgerDailyReconciliationTarget Target = new("tenant-a", "company-a", Guid.NewGuid().ToString("D"), Guid.NewGuid());
    private static readonly DateTimeOffset Due = new(2026, 11, 12, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OncePerUtcDay_RestartReusesTheDurableOperationIdentity()
    {
        var ids = new List<string>();
        var durable = new HashSet<string>(StringComparer.Ordinal);
        Task<OperationTerminalState> Compare(ProviderLedgerDailyReconciliationTarget target, string id, CancellationToken ct)
        {
            ids.Add(id);
            durable.Add(id);
            return Task.FromResult(OperationTerminalState.Succeeded);
        }
        var worker = Worker(Compare);
        await worker.RunDueAsync(Due.AddHours(-1));
        ids.Should().BeEmpty();
        await worker.RunDueAsync(Due);
        await worker.RunDueAsync(Due.AddMinutes(1));
        ids.Should().ContainSingle();
        await Worker(Compare).RunDueAsync(Due.AddMinutes(2));
        ids.Should().HaveCount(2);
        ids[0].Should().Be(ids[1]);
        durable.Should().ContainSingle();
        await worker.RunDueAsync(Due.AddDays(1));
        durable.Should().HaveCount(2);
    }

    [Fact]
    public async Task FailureRetry_IsBoundedAndUsesTheSameDailyOperation()
    {
        var ids = new List<string>();
        var worker = Worker((_, id, _) =>
        {
            ids.Add(id);
            return Task.FromResult(ids.Count == 1 ? OperationTerminalState.Failed : OperationTerminalState.Succeeded);
        });
        await worker.RunDueAsync(Due);
        await worker.RunDueAsync(Due.AddMinutes(5));
        ids.Should().ContainSingle();
        await worker.RunDueAsync(Due.AddMinutes(15));
        ids.Should().HaveCount(2);
        ids[0].Should().Be(ids[1]);
    }

    [Fact]
    public async Task NoTargets_IsIdle()
    {
        var worker = new ProviderLedgerDailyReconciliationWorker(new(), TimeProvider.System,
            NullLogger<ProviderLedgerDailyReconciliationWorker>.Instance,
            (_, _, _) => throw new InvalidOperationException("An idle worker must not resolve financial services."));
        await worker.RunDueAsync(Due);
    }

    [Fact]
    public async Task MissingRetainedOwnership_RefusesBeforeEnteringTenantAuthorityOrReadingAccounts()
    {
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ProviderLedgerDailyReconciliationWorker.RunTargetAsync(
            services, Target, "daily-operation", CancellationToken.None));
        FundScopeTenantAuthority.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public async Task AccountRead_UsesVerifiedRetainedTenantAndAlwaysRestoresAuthority()
    {
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        registry.ResolveAsync(Target.FundProfileId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(Target.FundProfileId, Target.TenantId, Target.CompanyId));
        var accounts = Substitute.For<IFundAccountService>();
        string? observedTenant = null;
        accounts.GetAccountAsync(Target.AccountId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            observedTenant = FundScopeTenantAuthority.CurrentTenantId;
            return Task.FromResult<AccountSummaryDto?>(null);
        });
        using var services = new ServiceCollection().AddSingleton(registry).AddSingleton(accounts).BuildServiceProvider();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ProviderLedgerDailyReconciliationWorker.RunTargetAsync(
            services, Target, "daily-operation", CancellationToken.None));
        observedTenant.Should().Be(Target.TenantId);
        FundScopeTenantAuthority.CurrentTenantId.Should().BeNull();
    }

    private static ProviderLedgerDailyReconciliationWorker Worker(
        Func<ProviderLedgerDailyReconciliationTarget, string, CancellationToken, Task<OperationTerminalState>> run)
        => new(new ProviderLedgerDailyReconciliationOptions { UtcHour = 23, RetryMinutes = 15, Targets = [Target] },
            TimeProvider.System, NullLogger<ProviderLedgerDailyReconciliationWorker>.Instance, run);
}
