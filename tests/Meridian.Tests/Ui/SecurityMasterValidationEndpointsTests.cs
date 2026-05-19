using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
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

public sealed class SecurityMasterValidationEndpointsTests
{
    [Fact]
    public async Task MapSecurityMasterEndpoints_ValidationRoute_ReturnsStructuredIssues()
    {
        var securityId = Guid.NewGuid();
        var validationService = Substitute.For<ISecurityValidationService>();
        validationService.ValidateSecurityAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SecurityValidationReportDto(
                SecurityId: securityId,
                Scope: "Security",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: true,
                CriticalIssueCount: 1,
                ErrorIssueCount: 0,
                Issues:
                [
                    new SecurityValidationIssueDto(
                        SecurityValidationSeverityDto.Critical,
                        "SM_DUPLICATE_CANONICAL_IDENTIFIER",
                        "Canonical identifier maps to multiple securities",
                        "ISIN 'US0378331005' is assigned to more than one SecurityId.",
                        ["identifiers.Isin"],
                        "Merge duplicate instruments or expire the losing identifier.",
                        [
                            new SecurityEvidenceLinkDto(
                                "SecurityMasterRecord",
                                Guid.NewGuid().ToString(),
                                "/api/security-master/related",
                                "Related instrument")
                        ])
                ])));

        await using var app = await CreateAppAsync(validationService);
        var client = app.GetTestClient();

        using var response = await client.GetAsync($"/api/security-master/{securityId}/validation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SecurityValidationReportDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload.Should().NotBeNull();
        payload!.SecurityId.Should().Be(securityId);
        payload.HasBlockingIssues.Should().BeTrue();
        payload.Issues.Should().ContainSingle(static issue =>
            issue.Code == "SM_DUPLICATE_CANONICAL_IDENTIFIER"
            && issue.Severity == SecurityValidationSeverityDto.Critical);
    }

    private static async Task<WebApplication> CreateAppAsync(ISecurityValidationService validationService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ContractsSecurityMasterQueryService>());
        builder.Services.AddSingleton(validationService);
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterConflictService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterIngestStatusService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterImportService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterEventStore>());

        var app = builder.Build();
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }
}
