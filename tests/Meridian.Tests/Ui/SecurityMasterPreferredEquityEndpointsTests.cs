using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

// Imported as a single type rather than the whole namespace: Meridian.Application.SecurityMaster
// also declares ISecurityMasterQueryService, which would make the Contracts one this file already
// uses ambiguous.
using SecurityMasterWorkbenchOptions = Meridian.Application.SecurityMaster.SecurityMasterWorkbenchOptions;

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
            .Returns(CreatePreferredTerms(securityId, request.ExpectedVersion));
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
                candidate.LiquidationPreferenceKind == "Senior" &&
                // The audit actor comes from the session, not the body: the request said "codex".
                candidate.UpdatedBy == SignedInOperator &&
                // SourceSystem identifies the upstream source for conflict precedence, so it is
                // left as the caller declared it rather than derived from the actor.
                candidate.SourceSystem == "test" &&
                candidate.Reason == "endpoint patch"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A mutation whose author cannot be established is refused rather than recorded against
    /// whatever the request body claimed.
    /// </summary>
    [Fact]
    public async Task PatchPreferredTermsRoute_IsRefused_WhenNoAuthenticatedActorCanBeResolved()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        queryService.GetPreferredEquityTermsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreatePreferredTerms(securityId, BuildRequest().ExpectedVersion));

        await using var app = await CreateAppAsync(queryService, service, signedInAs: null);
        var client = app.GetTestClient();

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"/api/security-master/equities/{securityId}/preferred-terms")
            {
                Content = CreateJsonContent(BuildRequest())
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await service.DidNotReceiveWithAnyArgs().AmendPreferredEquityTermsAsync(default, default!, default);
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

    /// <summary>
    /// Every DIRECT term-amendment route must honour <see cref="SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments"/>.
    /// The legacy <c>/equities/{id}/preferred-terms</c> alias reaches the same
    /// <see cref="ISecurityMasterService.AmendPreferredEquityTermsAsync"/> amendment as the canonical
    /// route, so a deployment that enables the flag to force maker-checker must not retain it as an
    /// ungated path. Table-driven so a newly added amendment route that skips the gate fails here.
    /// </summary>
    [Theory]
    [InlineData("/api/security-master/amend")]
    [InlineData("/api/security-master/{0}/preferred-equity-terms")]
    [InlineData("/api/security-master/{0}/convertible-equity-terms")]
    [InlineData("/api/security-master/equities/{0}/preferred-terms")]
    public async Task DirectTermAmendmentRoutes_AreRefused_WhenGovernedTermAmendmentsRequired(string routeTemplate)
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();

        await using var app = await CreateAppAsync(queryService, service, requireGovernedTermAmendments: true);
        var client = app.GetTestClient();

        var route = string.Format(CultureInfo.InvariantCulture, routeTemplate, securityId);
        var isGenericAmend = route.EndsWith("/amend", StringComparison.Ordinal);
        var method = isGenericAmend ? HttpMethod.Post : HttpMethod.Patch;

        // Each route binds a different request record; send the shape it actually expects so a 403
        // proves the governance gate refused it rather than model binding rejecting the payload.
        var body = route switch
        {
            _ when isGenericAmend => JsonContent.Create(
                BuildGenericAmendRequest(securityId), options: WebJson),
            _ when route.EndsWith("/convertible-equity-terms", StringComparison.Ordinal) => JsonContent.Create(
                BuildConvertibleRequest(), options: WebJson),
            _ => JsonContent.Create(BuildRequest(), options: WebJson)
        };

        using var response = await client.SendAsync(new HttpRequestMessage(method, route)
        {
            Content = body
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "direct term amendments must be refused so the correction passes maker-checker");

        // The refusal must precede the amendment, not merely follow it.
        await service.DidNotReceiveWithAnyArgs().AmendPreferredEquityTermsAsync(default, default!, default);
        await service.DidNotReceiveWithAnyArgs().AmendConvertibleEquityTermsAsync(default, default!, default);
        await service.DidNotReceiveWithAnyArgs().AmendTermsAsync(default!, default);
    }

    /// <summary>
    /// The gate must refuse before the existence probe, so a governed deployment does not disclose
    /// which securities carry preferred terms to a caller whose amendment it will reject anyway.
    /// </summary>
    [Fact]
    public async Task LegacyPatchPreferredTermsRoute_RefusesBeforeExistenceProbe_WhenGovernedTermAmendmentsRequired()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();

        await using var app = await CreateAppAsync(queryService, service, requireGovernedTermAmendments: true);
        var client = app.GetTestClient();

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"/api/security-master/equities/{securityId}/preferred-terms")
            {
                Content = CreateJsonContent(BuildRequest())
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await queryService.DidNotReceiveWithAnyArgs().GetPreferredEquityTermsAsync(default, default);
    }

    private const string SignedInOperator = "casey.doyle";

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterQueryService queryService,
        ISecurityMasterService service,
        bool requireGovernedTermAmendments = false,
        string? signedInAs = SignedInOperator)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        // The option is init-only, so bind it the way the host does rather than mutating it.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SecurityMasterWorkbenchOptions.SectionName}:{nameof(SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments)}"]
                = requireGovernedTermAmendments ? "true" : "false"
        });
        builder.Services.Configure<SecurityMasterWorkbenchOptions>(
            builder.Configuration.GetSection(SecurityMasterWorkbenchOptions.SectionName));
        builder.Services.AddSingleton(queryService);
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ModifySecurityMaster;

            // Mutations stamp their audit actor from the session rather than the request body, so a
            // request with permissions but no identifiable actor is refused. Establish one, as
            // LoginSessionMiddleware would; signedInAs: null models the unauthenticated case.
            if (signedInAs is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = signedInAs;
            }

            await next();
        });
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static PreferredEquityTermsDto CreatePreferredTerms(Guid securityId, long version)
        => new(
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
            Version: version);

    private static JsonContent CreateJsonContent(AmendPreferredEquityTermsRequest request)
        => JsonContent.Create(request, options: WebJson);

    private static AmendSecurityTermsRequest BuildGenericAmendRequest(Guid securityId)
        => new(
            SecurityId: securityId,
            ExpectedVersion: 7,
            CommonTerms: null,
            AssetSpecificTermsPatch: null,
            IdentifiersToAdd: Array.Empty<SecurityIdentifierDto>(),
            IdentifiersToExpire: Array.Empty<SecurityIdentifierDto>(),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "endpoint amend");

    private static AmendConvertibleEquityTermsRequest BuildConvertibleRequest()
        => new(
            ExpectedVersion: 7,
            UnderlyingSecurityId: Guid.NewGuid(),
            ConversionRatio: 2.5m,
            ConversionPrice: 40.00m,
            ConversionStartDate: new DateOnly(2030, 1, 15),
            ConversionEndDate: new DateOnly(2032, 1, 15),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "endpoint patch");

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
