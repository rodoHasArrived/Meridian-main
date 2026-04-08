using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterPreferredEquityEndpointsTests
{
    [Fact]
    public async Task MapSecurityMasterEndpoints_PatchPreferredTermsRoute_UsesSpecializedService()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        var request = BuildRequest();
        queryService.GetPreferredEquityTermsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new PreferredEquityTermsDto(
                SecurityId: securityId,
                Classification: "Preferred",
                DividendRate: 5.75m,
                DividendType: "Fixed",
                IsCumulative: false,
                RedemptionPrice: 25.00m,
                RedemptionDate: null,
                CallableDate: null,
                ParticipatesInCommonDividends: false,
                AdditionalDividendThreshold: null,
                LiquidationPreferenceKind: "Pari",
                LiquidationPreferenceMultiple: null,
                Version: request.ExpectedVersion));
        service.AmendPreferredEquityTermsAsync(securityId, Arg.Any<AmendPreferredEquityTermsRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateDetail(securityId));

        await using var app = await CreateAppAsync(queryService, service);
        var client = app.GetTestClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/security-master/equities/{securityId}/preferred-terms")
        {
            Content = CreateJsonContent(request)
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<SecurityDetailDto>();
        detail.Should().NotBeNull();
        detail!.SecurityId.Should().Be(securityId);

        await service.Received(1).AmendPreferredEquityTermsAsync(
            securityId,
            Arg.Is<AmendPreferredEquityTermsRequest>(candidate =>
                candidate.ExpectedVersion == request.ExpectedVersion &&
                candidate.DividendType == "Cumulative" &&
                candidate.LiquidationPreferenceKind == "Senior"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MapSecurityMasterEndpoints_PatchPreferredTermsRoute_ReturnsNotFound_WhenSecurityHasNoPreferredTerms()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        queryService.GetPreferredEquityTermsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns((PreferredEquityTermsDto?)null);

        await using var app = await CreateAppAsync(queryService, service);
        var client = app.GetTestClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/security-master/equities/{securityId}/preferred-terms")
        {
            Content = CreateJsonContent(BuildRequest())
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await service.DidNotReceiveWithAnyArgs().AmendPreferredEquityTermsAsync(default, default!, default);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterQueryService queryService,
        ISecurityMasterService service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(queryService);
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static JsonContent CreateJsonContent(AmendPreferredEquityTermsRequest request)
        => JsonContent.Create(
            request,
            options: new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static AmendPreferredEquityTermsRequest BuildRequest()
        => new(
            ExpectedVersion: 7,
            DividendRate: 6.25m,
            DividendType: "Cumulative",
            RedemptionPrice: 26.00m,
            RedemptionDate: new DateOnly(2032, 1, 15),
            CallableDate: new DateOnly(2030, 1, 15),
            ParticipatesInCommonDividends: true,
            AdditionalDividendThreshold: 1.50m,
            LiquidationPreferenceKind: "Senior",
            LiquidationPreferenceMultiple: 1.00m,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "endpoint patch");

    private static SecurityDetailDto CreateDetail(Guid securityId)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Meridian Preferred",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Meridian Preferred",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "A",
                classification = "Preferred",
                preferredTerms = new
                {
                    dividendRate = 6.25m,
                    dividendType = "Cumulative",
                    redemptionPrice = 26.00m,
                    liquidationPreference = new
                    {
                        kind = "Senior",
                        multiple = 1.00m
                    }
                }
            }),
            Identifiers: new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MPFD", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: 8,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo: null);
}
