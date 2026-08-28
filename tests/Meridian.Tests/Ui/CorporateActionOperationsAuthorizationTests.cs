using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class CorporateActionOperationsAuthorizationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ScopedRoutes_DeclareStandardTenantScopeMetadata()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        await using var app = await CreateAppAsync(service, UserPermission.ViewCorporateActions);
        string[] scopedEndpointNames =
        [
            "GetSecurityMasterCorporateActionInbox",
            "RetiredSecurityMasterCorporateActionInboxApply",
            "ResolveCorporateActionCaseConflict",
            "AcceptCorporateActionSourceProposal",
            "ListCorporateActionCases",
            "GetCorporateActionCase",
            "ListCorporateActionCaseConflicts",
            "GetCorporateActionCaseConflict",
            "AddCorporateActionCaseEvidence",
            "RecordCorporateActionCaseConflict",
            "UpsertCorporateActionCaseOption",
            "TransitionCorporateActionCase",
        ];

        foreach (var endpointName in scopedEndpointNames)
        {
            var endpoint = GetEndpoint(app, endpointName);
            endpoint.Metadata.GetMetadata<WorkstationTenantScopeMetadata>()
                .Should().NotBeNull($"{endpointName} operates on tenant/company-scoped state");
        }
    }

    [Fact]
    public async Task AcceptAndRejectRoutes_DeclareOneCombinedAllOfPermissionRequirement()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        await using var app = await CreateAppAsync(service, UserPermission.ViewCorporateActions);
        string[] endpointNames =
        [
            "RetiredSecurityMasterCorporateActionInboxApply",
            "AcceptCorporateActionSourceProposal",
            "RejectCorporateActionSourceProposal",
        ];

        foreach (var endpointName in endpointNames)
        {
            var declarations = GetEndpoint(app, endpointName).Metadata
                .GetOrderedMetadata<EndpointAuthorizationMetadata>();
            var declaration = declarations.Should().ContainSingle().Subject;
            declaration.RequireAll.Should().BeTrue();
            declaration.Permissions.Should().Equal(
                UserPermission.ModifySecurityMaster,
                UserPermission.ResolveCorporateActionTerms);
        }
    }

    [Fact]
    public async Task Inbox_WhenCorporateActionPersistenceIsNotConfigured_ReturnsServiceUnavailable()
    {
        await using var app = await CreateAppAsync(
            new NullCorporateActionOperationsService(),
            UserPermission.ViewCorporateActions);

        var response = await app.GetTestClient().GetAsync("/api/security-master/corporate-actions/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CorporateActionProblemCodes.PersistenceUnavailable);
    }

    private static Endpoint GetEndpoint(WebApplication app, string endpointName) =>
        app.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Single(candidate => string.Equals(
                candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                endpointName,
                StringComparison.Ordinal));

    private static async Task<WebApplication> CreateAppAsync(
        ICorporateActionOperationsService service,
        UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddMutationRateLimiter();
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton(Substitute.For<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>());

        var app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operations-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            await next(context);
        });
        app.MapSecurityMasterEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }
}
