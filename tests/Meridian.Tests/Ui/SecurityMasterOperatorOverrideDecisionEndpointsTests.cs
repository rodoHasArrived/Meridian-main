using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ContractsSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterOperatorOverrideDecisionEndpointsTests
{
    [Fact]
    public async Task DecisionRoute_RecordsApproval_WhenOverridesExist()
    {
        var securityId = Guid.NewGuid();
        var store = new InMemoryOperatorOverridesStore();
        store.Seed(securityId);

        await using var app = await CreateAppAsync(store);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            DecisionRoute(securityId),
            new { decision = "Approved", reasonCode = "provider-symbol-confirmed", comment = "Verified against custodian." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<OperatorOverridesDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().NotBeNull();
        payload!.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Approved);
        payload.ReviewedBy.Should().Be("security-admin");
        payload.ReasonCode.Should().Be("provider-symbol-confirmed");
    }

    [Fact]
    public async Task DecisionRoute_ReturnsNotFound_WhenNoOverridesExist()
    {
        var store = new InMemoryOperatorOverridesStore();

        await using var app = await CreateAppAsync(store);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            DecisionRoute(Guid.NewGuid()),
            new { decision = "Rejected", reasonCode = "insufficient-evidence" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DecisionRoute_ReturnsForbidden_WithoutModifyPermission()
    {
        var securityId = Guid.NewGuid();
        var store = new InMemoryOperatorOverridesStore();
        store.Seed(securityId);

        await using var app = await CreateAppAsync(store, UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            DecisionRoute(securityId),
            new { decision = "Approved" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string DecisionRoute(Guid securityId)
        => UiApiRoutes.SecurityMasterOperatorOverrideDecision.Replace(
            "{securityId:guid}",
            securityId.ToString("D"),
            StringComparison.Ordinal);

    private static async Task<WebApplication> CreateAppAsync(
        IOperatorOverridesStore store,
        UserPermission permissions = UserPermission.ModifySecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        // Registering a query service keeps MapSecurityMasterEndpoints from early-returning.
        builder.Services.AddSingleton(Substitute.For<ContractsSecurityMasterQueryService>());
        builder.Services.AddSingleton(store);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "security-admin";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private sealed class InMemoryOperatorOverridesStore : IOperatorOverridesStore
    {
        private readonly Dictionary<Guid, OperatorOverridesDto> _overrides = [];

        public void Seed(Guid securityId)
        {
            _overrides[securityId] = new OperatorOverridesDto(
                securityId,
                new Dictionary<string, string> { ["sector"] = "Technology" },
                "security-steward",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
                ReasonCode = "provider-symbol-confirmed"
            };
        }

        public Task<OperatorOverridesDto?> GetAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(_overrides.GetValueOrDefault(securityId));

        public Task<OperatorOverridesDto> PatchAsync(
            Guid securityId,
            OperatorOverridesPatchRequest request,
            string updatedBy,
            CancellationToken ct = default,
            long? expectedCanonicalVersion = null)
            => throw new NotSupportedException();

        public Task<OperatorOverridesDto> RecordApprovalDecisionAsync(
            Guid securityId,
            OperatorOverrideDecision decision,
            CancellationToken ct = default)
        {
            if (decision.Decision is not (SecurityOverrideApprovalStatusDto.Approved or SecurityOverrideApprovalStatusDto.Rejected))
            {
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Decision, "Approval decision must be Approved or Rejected.");
            }

            if (!_overrides.TryGetValue(securityId, out var existing))
            {
                throw new InvalidOperationException($"No operator overrides exist for security '{securityId}'.");
            }

            var reviewedAt = DateTimeOffset.UtcNow;
            var updated = existing with
            {
                ApprovalStatus = decision.Decision,
                ReviewedBy = decision.Reviewer,
                ReviewedAt = reviewedAt
            };
            _overrides[securityId] = updated;
            return Task.FromResult(updated);
        }
    }
}
