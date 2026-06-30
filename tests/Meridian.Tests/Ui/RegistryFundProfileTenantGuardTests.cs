using FluentAssertions;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Meridian.Tests.Ui;

/// <summary>
/// The registry-backed tenant guard claims a fund for the caller on first use and denies only when the
/// fund is already owned by a different tenant. Own/unknown funds, a blank scope, a caller with no tenant
/// scope, and an unavailable registry are all allowed, so a legitimate edit is never blocked.
/// </summary>
public sealed class RegistryFundProfileTenantGuardTests
{
    [Fact]
    public async Task OwnFund_IsAllowed()
    {
        var (guard, registry) = Build();
        registry.BindAsync("fund-x", "tenant-acme", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-x", "tenant-acme", "company-acme"));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ForeignFund_IsDenied()
    {
        var (guard, registry) = Build();
        registry.BindAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-x", "tenant-other", "company-other"));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task UnboundFund_IsClaimedForCaller_AndAllowed()
    {
        var (guard, registry) = Build();
        // BindAsync claims an unbound fund for the caller and returns the caller as owner.
        registry.BindAsync("fund-new", "tenant-acme", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new FundProfileOwnership("fund-new", (string)ci[1], null));

        var decision = await guard.EvaluateAsync(Caller(), "fund-new");

        decision.IsAllowed.Should().BeTrue();
        await registry.Received(1).BindAsync("fund-new", "tenant-acme", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BlankFund_IsAllowed_WithoutTouchingRegistry()
    {
        var (guard, registry) = Build();

        var decision = await guard.EvaluateAsync(Caller(), "   ");

        decision.IsAllowed.Should().BeTrue();
        await registry.DidNotReceive().BindAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoTenantScope_IsAllowed_WithoutTouchingRegistry()
    {
        var (guard, registry) = Build();

        var decision = await guard.EvaluateAsync(Caller(tenant: null), "fund-x");

        decision.IsAllowed.Should().BeTrue();
        await registry.DidNotReceive().BindAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistryUnavailable_IsAllowed_DeferringToDeploymentBoundary()
    {
        var (guard, registry) = Build();
        registry.BindAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("registry down"));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var (guard, registry) = Build();
        registry.BindAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await guard.Invoking(g => g.EvaluateAsync(Caller(), "fund-x"))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private static (RegistryFundProfileTenantGuard Guard, IFundProfileTenancyRegistry Registry) Build()
    {
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        var guard = new RegistryFundProfileTenantGuard(registry, NullLogger<RegistryFundProfileTenantGuard>.Instance);
        return (guard, registry);
    }

    private static WorkstationTenantContext Caller(string? tenant = "tenant-acme", string? company = "company-acme")
        => new(
            TenantId: tenant,
            CompanyId: company,
            Actor: "ops.analyst",
            RoleProfileName: null,
            Permissions: UserPermission.ModifySecurityMaster);
}
