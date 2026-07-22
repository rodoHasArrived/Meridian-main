using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class StrategyLifecycleEndpointsTests
{
    [Theory]
    [InlineData("/api/strategies/strategy-1/pause")]
    [InlineData("/api/strategies/strategy-1/stop")]
    [InlineData("/api/strategies/strategy-1/reconcile")]
    public async Task LifecycleMutation_WhenUserLacksManageStrategiesPermission_ReturnsForbidden(string route)
    {
        await using var app = await CreateAppAsync(UserPermission.ViewStrategies);

        var response = await app.GetTestClient().PostAsync(route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void CreateEndpointFailureOutcome_ForBlockedPrerequisite_ReturnsValidReceipt()
    {
        var outcome = StrategyLifecycleEndpoints.CreateEndpointFailureOutcome(
            "strategy-1",
            "pause",
            OperationTerminalState.Blocked,
            "Strategy is not registered.",
            exception: null,
            externalStateMayHaveChanged: false);

        outcome.State.Should().Be(OperationTerminalState.Blocked);
        outcome.Issues.Should().ContainSingle().Which.IsBlocking.Should().BeTrue();
        outcome.Recovery.Should().ContainSingle().Which.Retryable.Should().BeTrue();
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public void CreateEndpointFailureOutcome_WhenExternalStateMayHaveChanged_RequiresReconciliation()
    {
        var outcome = StrategyLifecycleEndpoints.CreateEndpointFailureOutcome(
            "strategy-1",
            "stop",
            OperationTerminalState.Failed,
            "The stop request failed before a retained terminal receipt was returned.",
            new IOException("Persistence unavailable."),
            externalStateMayHaveChanged: true);

        outcome.State.Should().Be(OperationTerminalState.Failed);
        outcome.Recovery.Should().ContainSingle().Which.Should().Match<OperationRecoveryAction>(action =>
            action.ActionId == "reconcile-before-retry" &&
            !action.Retryable &&
            action.Guidance.Contains("Do not repeat", StringComparison.Ordinal) &&
            action.Route == "/api/strategies/strategy-1/reconcile");
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapStrategyLifecycleEndpoints(new JsonSerializerOptions());
        await app.StartAsync();
        return app;
    }
}
