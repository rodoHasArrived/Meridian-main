using FluentAssertions;
using Meridian.Contracts.Tenancy;

namespace Meridian.Tests.Storage;

/// <summary>
/// W9-GOV-008 criterion 2, background-reader half. Fund-scoped stores resolve the caller's tenant
/// from an ambient accessor that is HTTP-backed and returns null outside a request, and
/// <c>ILedgerJournalStore</c> alone serves roughly fifty internal and worker call sites. The moment an
/// unresolved tenant fails closed, every one of those loses access — so a job that legitimately holds
/// retained authority has to be able to declare it.
/// </summary>
/// <remarks>
/// The tempting shortcut is to exempt background callers instead. That reintroduces the fail-open path
/// the criterion exists to remove, on the code path least likely to be looked at again. These tests
/// pin the alternative: authority is scoped, explicit, and absent unless declared.
/// </remarks>
public sealed class FundScopeTenantAuthorityTests
{
    [Fact]
    public void CurrentTenantId_IsNullWithoutADeclaredAuthority()
        => FundScopeTenantAuthority.CurrentTenantId.Should().BeNull(
            "a job that has declared nothing must still fail closed");

    [Fact]
    public void Enter_MakesTheTenantAmbientForTheScope()
    {
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "nightly-close"))
        {
            FundScopeTenantAuthority.CurrentTenantId.Should().Be("tenant-alpha");
            FundScopeTenantAuthority.CurrentReason.Should().Be("nightly-close");
        }

        FundScopeTenantAuthority.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public void Enter_TrimsTheDeclaredTenantToMatchTheReadPredicate()
    {
        using var scope = FundScopeTenantAuthority.Enter("  tenant-alpha  ", "nightly-close");

        FundScopeTenantAuthority.CurrentTenantId.Should().Be("tenant-alpha");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Enter_RefusesABlankTenant(string? tenantId)
    {
        // A blank authority resolves to nothing, which is indistinguishable from having entered no
        // scope at all — it would fail closed later, far from the mistake that caused it.
        var enter = () => FundScopeTenantAuthority.Enter(tenantId!, "nightly-close");

        enter.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Enter_RestoresTheEnclosingAuthorityOnDispose()
    {
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "outer"))
        {
            using (FundScopeTenantAuthority.Enter("tenant-beta", "inner"))
            {
                FundScopeTenantAuthority.CurrentTenantId.Should().Be("tenant-beta");
            }

            FundScopeTenantAuthority.CurrentTenantId.Should().Be("tenant-alpha");
            FundScopeTenantAuthority.CurrentReason.Should().Be("outer");
        }
    }

    [Fact]
    public async Task Enter_FlowsIntoAwaitedWorkAndNotOutOfIt()
    {
        string? insideScope;
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "nightly-close"))
        {
            // The store read happens several awaits deep inside the job, which is the only reason an
            // ambient authority is usable at all.
            insideScope = await Task.Run(async () =>
            {
                await Task.Yield();
                return FundScopeTenantAuthority.CurrentTenantId;
            });
        }

        insideScope.Should().Be("tenant-alpha");
        FundScopeTenantAuthority.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public async Task Enter_DoesNotLeakIntoConcurrentWorkStartedOutsideTheScope()
    {
        var outsideStarted = new TaskCompletionSource();
        var scopeEntered = new TaskCompletionSource();

        var outside = Task.Run(async () =>
        {
            outsideStarted.SetResult();
            await scopeEntered.Task;
            return FundScopeTenantAuthority.CurrentTenantId;
        });

        await outsideStarted.Task;
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "nightly-close"))
        {
            scopeEntered.SetResult();
            (await outside).Should().BeNull(
                "an authority must not reach work that was never given it");
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var scope = FundScopeTenantAuthority.Enter("tenant-alpha", "nightly-close");
        scope.Dispose();
        scope.Dispose();

        FundScopeTenantAuthority.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public void ShouldRejectRead_IsSatisfiedByADeclaredAuthority()
    {
        // The point of the whole mechanism, stated end to end: the same background read that would be
        // refused undeclared is served once the job names the tenant it is acting for.
        TenantReadPredicate.ShouldRejectRead(
                FundScopeTenantAuthority.CurrentTenantId, TenantScopeEnforcementMode.FailClosed)
            .Should().BeTrue();

        using var scope = FundScopeTenantAuthority.Enter("tenant-alpha", "nightly-close");

        TenantReadPredicate.ShouldRejectRead(
                FundScopeTenantAuthority.CurrentTenantId, TenantScopeEnforcementMode.FailClosed)
            .Should().BeFalse();
    }
}
