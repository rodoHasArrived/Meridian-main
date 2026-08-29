using System.Net;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

/// <summary>
/// W9-GOV-008 criterion 2. The fund-scoped stores refuse an unresolvable tenant scope by throwing
/// rather than returning nothing, because an empty result set is a default the caller cannot
/// distinguish from genuinely having no rows. That refusal has to reach the caller as a refusal.
/// </summary>
/// <remarks>
/// Without this mapping the throw falls through to a 500: the caller is told the server broke, the
/// reason is buried in error logs, and the "rejected rather than defaulted" behaviour is
/// indistinguishable from a bug to everyone who has to operate it. Which is to say the criterion
/// would be met in the store and lost on the wire.
/// </remarks>
public sealed class TenantScopeRefusalProblemDetailsTests
{
    [Fact]
    public async Task ARefusedFundScopedRead_IsReportedAsForbidden()
    {
        await using var app = await CreateAppAsync(
            () => throw new TenantScopeRejectedException("ledger records"));

        using var response = await app.GetTestClient().GetAsync("/probe");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARefusedFundStructureRead_IsReportedAsForbidden()
    {
        await using var app = await CreateAppAsync(
            () => throw new FundStructureTenantScopeException("tenant scope required"));

        using var response = await app.GetTestClient().GetAsync("/probe");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnActualFault_IsStillReportedAsAServerError()
    {
        // The mapping must not widen into "anything from a store is the caller's fault".
        await using var app = await CreateAppAsync(
            () => throw new InvalidOperationException("something genuinely broke"));

        using var response = await app.GetTestClient().GetAsync("/probe");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private static async Task<WebApplication> CreateAppAsync(Func<IResult> handler)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<MeridianApiExceptionHandler>();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/probe", handler);

        await app.StartAsync();
        return app;
    }
}
