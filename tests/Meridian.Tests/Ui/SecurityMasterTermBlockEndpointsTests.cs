using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
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

/// <summary>
/// The generic term-block route (<c>PUT /api/security-master/{id}/terms/{blockKey}</c>). A block the
/// class declares but this node only reads (ADR-024, readable before writable) is refused with the
/// shared not-yet-writable reason; any other undeclared block keeps the route's own text.
/// </summary>
public sealed class SecurityMasterTermBlockEndpointsTests
{
    private const string SignedInOperator = "casey.doyle";

    [Theory]
    [InlineData("Bond", 3)]
    [InlineData("CertificateOfDeposit", 3)]
    [InlineData("StructuredCredit", 4)]
    public async Task PutScheduleConventions_OnAWritableClass_DelegatesValidationWithTheSessionActor(
        string assetClass, int writes)
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(CreateDetail(securityId, assetClass));
        service.AmendTermBlockAsync(securityId, "scheduleConventions", Arg.Any<AmendSecurityTermBlockRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateDetail(securityId, assetClass));
        SecurityAssetClassCatalog.GetTermsVersion(assetClass).Should().Be(writes);

        await using var app = await CreateAppAsync(queryService, service);

        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/security-master/{securityId}/terms/scheduleConventions",
            BuildRequest(JsonSerializer.SerializeToElement(new { stubPolicy = "Short", paymentCalendars = new[] { "USNY" } })),
            WebJson);

        // The route now offers this block; the canonical service remains the authority for anchor
        // availability and full schedule semantics, including class-specific refusals.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await service.Received(1).AmendTermBlockAsync(securityId, "scheduleConventions",
            Arg.Is<AmendSecurityTermBlockRequest>(candidate => candidate.ExpectedVersion == 7
                && candidate.UpdatedBy == SignedInOperator
                && candidate.Block.GetProperty("stubPolicy").GetString() == "Short"
                && candidate.Block.GetProperty("paymentCalendars")[0].GetString() == "USNY"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Bond", "callSchedule", 3, """[{"callDate":"2030-06-15","callPrice":101,"exerciseStyle":"European"}]""")]
    [InlineData("Bond", "callSchedule", 3, "[]")]
    [InlineData("Bond", "putSchedule", 3, """[{"putDate":"2029-06-15","exerciseStyle":"European"}]""")]
    [InlineData("CertificateOfDeposit", "callSchedule", 3, """[{"callDate":"2026-09-30","exerciseStyle":"European"}]""")]
    [InlineData("CertificateOfDeposit", "putSchedule", 3, """[{"putDate":"2026-09-30","exerciseStyle":"European"}]""")]
    [InlineData("StructuredCredit", "callSchedule", 4, """[{"callDate":"2030-06-15","callPrice":101,"exerciseStyle":"European"}]""")]
    [InlineData("StructuredCredit", "putSchedule", 4, "[]")]
    public async Task PutCallOrPutSchedule_OnAWritableClass_DelegatesTheUnchangedArrayWithTheSessionActor(
        string assetClass, string blockKey, int writes, string block)
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(CreateDetail(securityId, assetClass));
        service.AmendTermBlockAsync(securityId, blockKey, Arg.Any<AmendSecurityTermBlockRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateDetail(securityId, assetClass));
        SecurityAssetClassCatalog.GetTermsVersion(assetClass).Should().Be(writes);

        await using var app = await CreateAppAsync(queryService, service);

        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/security-master/{securityId}/terms/{blockKey}",
            BuildRequest(JsonDocument.Parse(block).RootElement.Clone()),
            WebJson);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await service.Received(1).AmendTermBlockAsync(securityId, blockKey,
            Arg.Is<AmendSecurityTermBlockRequest>(candidate => candidate.ExpectedVersion == 7
                && candidate.UpdatedBy == SignedInOperator && candidate.Block.GetRawText() == block),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Bond", "scheduleConventions", "12", JsonValueKind.Number)]
    [InlineData("Bond", "callSchedule", "false", JsonValueKind.False)]
    [InlineData("Swap", "legs", "{\"legType\":\"Fixed\"}", JsonValueKind.Object)]
    public async Task PutMalformedBlock_PreservesTheCallerShapeAndReturnsTheCanonicalRefusal(
        string assetClass, string blockKey, string block, JsonValueKind suppliedKind)
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(CreateDetail(securityId, assetClass));
        const string reason = "The supplied block has the wrong declared JSON type.";
        service.AmendTermBlockAsync(securityId, blockKey, Arg.Any<AmendSecurityTermBlockRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SecurityDetailDto>(new ArgumentException(reason)));
        await using var app = await CreateAppAsync(queryService, service);

        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/security-master/{securityId}/terms/{blockKey}",
            BuildRequest(JsonSerializer.Deserialize<JsonElement>(block)), WebJson);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString().Should().Be(reason);
        await service.Received(1).AmendTermBlockAsync(securityId, blockKey,
            Arg.Is<AmendSecurityTermBlockRequest>(candidate => candidate.Block.ValueKind == suppliedKind
                && candidate.Block.GetRawText() == block && candidate.UpdatedBy == SignedInOperator),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    // A block no class declares, a case variant of the readable-only key, the swap leg's element
    // field (a term inside legs, not a block of the class) and a class without the field.
    [InlineData("Bond", "sinkingFundSchedule")]
    [InlineData("Bond", "ScheduleConventions")]
    [InlineData("Swap", "scheduleConventions")]
    [InlineData("Equity", "scheduleConventions")]
    // A case variant of a readable-only schedule, and a class that declares no schedule.
    [InlineData("Bond", "CallSchedule")]
    [InlineData("Swap", "putSchedule")]
    public async Task PutUndeclaredBlock_KeepsTheDeclaresNoTermBlockText(string assetClass, string blockKey)
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(CreateDetail(securityId, assetClass));

        await using var app = await CreateAppAsync(queryService, service);

        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/security-master/{securityId}/terms/{blockKey}",
            BuildRequest(JsonSerializer.SerializeToElement(new { })),
            WebJson);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString()
            .Should().Be($"Asset class '{assetClass}' declares no term block '{blockKey}'.");
        await service.DidNotReceiveWithAnyArgs().AmendTermBlockAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task PutWritableBlock_OnABond_ReachesTheServiceWithTheSessionActor()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(CreateDetail(securityId, "Bond"));
        service.AmendTermBlockAsync(securityId, "principalSchedule", Arg.Any<AmendSecurityTermBlockRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateDetail(securityId, "Bond"));

        await using var app = await CreateAppAsync(queryService, service);

        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/security-master/{securityId}/terms/principalSchedule",
            BuildRequest(JsonSerializer.SerializeToElement(new[] { new { paymentDate = "2029-06-15", amount = 250m } })),
            WebJson);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await service.Received(1).AmendTermBlockAsync(
            securityId,
            "principalSchedule",
            Arg.Is<AmendSecurityTermBlockRequest>(candidate =>
                candidate.ExpectedVersion == 7 &&
                candidate.Block.ValueKind == JsonValueKind.Array &&
                candidate.UpdatedBy == SignedInOperator),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PutScheduleConventions_IsRefusedByTheGovernanceGateFirst_WhenGovernedTermAmendmentsRequired()
    {
        // The governed-route refusal precedes the existence probe and the block check, as on every
        // direct term-amendment route.
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(CreateDetail(securityId, "Bond"));

        await using var app = await CreateAppAsync(queryService, service, requireGovernedTermAmendments: true);

        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/security-master/{securityId}/terms/scheduleConventions",
            BuildRequest(JsonSerializer.SerializeToElement(new { stubPolicy = "Short" })),
            WebJson);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await queryService.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await service.DidNotReceiveWithAnyArgs().AmendTermBlockAsync(default, default!, default!, default);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterQueryService queryService,
        ISecurityMasterService service,
        bool requireGovernedTermAmendments = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        // The option is init-only, so bind it the way the host does rather than mutating it. Off, the
        // governed-route requirement lets the direct term-block route through.
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
            // Mutations stamp their audit actor from the session, as LoginSessionMiddleware would.
            context.Items[LoginSessionMiddleware.CurrentUserKey] = SignedInOperator;
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

    private static AmendSecurityTermBlockRequest BuildRequest(JsonElement block)
        => new(
            ExpectedVersion: 7,
            Block: block,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "untrusted.body.actor",
            SourceRecordId: null,
            Reason: "endpoint block amend");

    private static SecurityDetailDto CreateDetail(Guid securityId, string assetClass)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: $"Meridian {assetClass}",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = $"Meridian {assetClass}",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                termsFormat = "flat",
                maturity = "2031-06-15"
            }),
            Identifiers: new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "037833100", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: 7,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo: null);
}
