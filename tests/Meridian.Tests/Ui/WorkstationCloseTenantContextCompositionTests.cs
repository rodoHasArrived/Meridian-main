using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

[Collection("Sequential")]
public sealed class WorkstationCloseTenantContextCompositionTests
{
    [Fact]
    public async Task RootCloseGuard_ResolvesWithScopeValidation_AndIsolatesConcurrentRequestIdentities()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkstationSharedServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var accessor = provider.GetRequiredService<IWorkstationTenantContextAccessor>();
        var http = provider.GetRequiredService<IHttpContextAccessor>();
        // The real singleton guard factory must resolve at the root with scope validation enabled.
        var guard = provider.GetRequiredService<IClosePublicationReadinessGuard>();
        var scope = new CloseReadinessScopeDto("fund-alpha", Guid.NewGuid(), Guid.NewGuid(), "entity-alpha", "2026-07");
        var bothRequestsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        async Task VerifyRequestAsync(string tenant)
        {
            var context = new DefaultHttpContext();
            context.Items[LoginSessionMiddleware.CurrentUserKey] = $"operator-{tenant}";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenant;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = $"company-{tenant}";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageLedgerReports;
            http.HttpContext = context;
            try
            {
                if (Interlocked.Increment(ref readyCount) == 2)
                    bothRequestsReady.SetResult();
                await bothRequestsReady.Task;
                await Task.Yield();

                var current = accessor.GetRequired();
                current.TenantId.Should().Be(tenant);
                current.CompanyId.Should().Be($"company-{tenant}");
                current.Actor.Should().Be($"operator-{tenant}");
                current.Permissions.Should().Be(UserPermission.ManageLedgerReports);
                (await guard.ValidateAsync(Guid.NewGuid(), 1, scope, "foreign-tenant", "foreign-company"))
                    .Should().ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_MISMATCH");
            }
            finally
            {
                http.HttpContext = null;
            }
        }

        await Task.WhenAll(Task.Run(() => VerifyRequestAsync("tenant-alpha")),
            Task.Run(() => VerifyRequestAsync("tenant-beta")));

        http.HttpContext.Should().BeNull();
        accessor.TryGetCurrent(out var absent).Should().BeFalse();
        absent.HasTenantScope.Should().BeFalse();
        (await guard.ValidateAsync(Guid.NewGuid(), 1, scope)).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_REQUIRED");
    }
}
