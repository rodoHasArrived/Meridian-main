using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.FinancialOperations.Banking;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Payment approval is a human-only banking action. The request bodies default their
/// <c>ActionOrigin</c> to <c>HumanOperator</c>, so the endpoints must derive the origin from the
/// authenticated principal; otherwise an API key holding <c>ManageDirectLending</c> could submit
/// an ordinary approval body and pass the service's human-only gate.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BankingActionOriginTests
{
    private static readonly Guid EntityId = Guid.Parse("4f2b7d0e-8c1a-4e55-9d3b-2a6c7e9f1b04");

    [Fact]
    public async Task ApiKeyPrincipal_CannotApproveAPayment_WithTheDefaultHumanOrigin()
    {
        var banking = new InMemoryBankingService();
        await using var app = await CreateAppAsync(banking, apiKeyPrincipal: true);
        var pending = await InitiateAsync(banking);

        using var response = await app.GetTestClient().PostAsync(
            $"/api/banking/payments/{pending.PendingPaymentId:D}/approve?entityId={EntityId:D}",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await banking.GetPaymentAsync(pending.PendingPaymentId, CancellationToken.None))!
            .Status.Should().Be(PaymentApprovalStatus.Pending);
    }

    [Fact]
    public async Task InteractiveSession_CanStillApproveAPayment()
    {
        var banking = new InMemoryBankingService();
        await using var app = await CreateAppAsync(banking, apiKeyPrincipal: false);
        var pending = await InitiateAsync(banking);

        using var response = await app.GetTestClient().PostAsync(
            $"/api/banking/payments/{pending.PendingPaymentId:D}/approve?entityId={EntityId:D}",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await banking.GetPaymentAsync(pending.PendingPaymentId, CancellationToken.None))!
            .Status.Should().Be(PaymentApprovalStatus.Approved);
    }

    private static Task<PendingPaymentDto> InitiateAsync(InMemoryBankingService banking) =>
        banking.InitiatePaymentAsync(
            EntityId,
            new InitiatePaymentRequest(1_000m, new DateOnly(2026, 11, 6), "REF-1", null, "USD"),
            CancellationToken.None);

    private static async Task<WebApplication> CreateAppAsync(InMemoryBankingService banking, bool apiKeyPrincipal)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IBankingService>(banking);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                UserPermission.ViewDirectLending | UserPermission.ManageDirectLending;
            if (apiKeyPrincipal)
            {
                // The shape ApiKeyMiddleware leaves on the request.
                context.Items[LoginSessionMiddleware.CurrentUserKey] = ApiKeyMiddleware.ApiKeyActor;
                context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = true;
            }
            else
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "treasury-operator";
                context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Accounting;
            }

            await next(context);
        });
        app.MapBankingEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }
}
