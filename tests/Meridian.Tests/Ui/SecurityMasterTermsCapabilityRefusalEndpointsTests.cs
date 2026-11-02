using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

using SecurityMasterTermsCapabilityRefusedException = Meridian.Application.SecurityMaster.SecurityMasterTermsCapabilityRefusedException;
using SecurityMasterTermsCapabilityVerifier = Meridian.Application.SecurityMaster.SecurityMasterTermsCapabilityVerifier;
using SecurityMasterWorkbenchOptions = Meridian.Application.SecurityMaster.SecurityMasterWorkbenchOptions;

namespace Meridian.Tests.Ui;

/// <summary>
/// A per-request terms capability floor refusal is a temporary refusal of this node, not a fault: the
/// shared exception handler answers 503 with an operator reason that names no connection, token or
/// startup wording, where it used to fall through to the generic 500.
/// </summary>
public sealed class SecurityMasterTermsCapabilityRefusalEndpointsTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Create_RefusedByTheCapabilityFloor_Returns503WithTheOperatorReason()
    {
        var service = Substitute.For<ISecurityMasterService>();
        service.CreateAsync(Arg.Any<CreateSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<SecurityDetailDto>>(_ => throw MissingFloorRefusal());
        await using var app = await CreateAppAsync(service);

        using var response = await app.GetTestClient().PostAsJsonAsync("/api/security-master", CreateRequest(), WebJson);

        await AssertServiceUnavailableAsync(response);
    }

    [Fact]
    public async Task Amend_RefusedByTheCapabilityFloor_Returns503WithTheOperatorReason()
    {
        var securityId = Guid.NewGuid();
        var service = Substitute.For<ISecurityMasterService>();
        service.AmendTermsAsync(Arg.Any<AmendSecurityTermsRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<SecurityDetailDto>>(_ => throw MissingFloorRefusal());
        await using var app = await CreateAppAsync(service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            new AmendSecurityTermsRequest(securityId, 1, null, null, [], [], DateTimeOffset.UtcNow, "test", "test", null, "amend"),
            WebJson);

        await AssertServiceUnavailableAsync(response);
    }

    // The verifier's own refusal, so the case pins the type the real command boundary throws.
    private static Exception MissingFloorRefusal()
    {
        try
        {
            SecurityMasterTermsCapabilityVerifier.EnsureCompatible(new SecurityMasterTermsCapabilityState(true, null), true, NullLogger.Instance);
        }
        catch (SecurityMasterTermsCapabilityRefusedException refused)
        {
            return refused;
        }

        throw new InvalidOperationException("The missing floor was expected to refuse a node that writes declared terms.");
    }

    private static async Task AssertServiceUnavailableAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("type").GetString().Should().Be(ApiProblemTypes.ServiceUnavailable);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.RootElement.GetProperty("detail").GetString().Should().Be(SecurityMasterTermsCapabilityRefusedException.RefusalMessage);
        body.Should().NotContain("startup", "the per-request reason is not the host-start wording");
    }

    private static CreateSecurityRequest CreateRequest()
        => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: "Bond",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Meridian Bond", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { maturity = "2030-01-01" }),
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MRDN", true, DateTimeOffset.UtcNow.AddDays(-1))],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "test",
            SourceRecordId: null,
            Reason: "create");

    private static async Task<WebApplication> CreateAppAsync(ISecurityMasterService service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SecurityMasterWorkbenchOptions.SectionName}:{nameof(SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments)}"] = "false"
        });
        builder.Services.Configure<SecurityMasterWorkbenchOptions>(builder.Configuration.GetSection(SecurityMasterWorkbenchOptions.SectionName));
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());
        builder.Services.AddSingleton(service);
        builder.Services.AddMeridianApiProblemDetails();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ModifySecurityMaster;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "casey.doyle";
            await next();
        });
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }
}
