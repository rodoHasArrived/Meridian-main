using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class SecurityInstrumentTermsEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Effective = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task Read_ExposesExactValuesNestedSchedulesAndVersionBoundEvidence()
    {
        var id = Guid.NewGuid();
        const string terms = """{"termsFormat":"flat","assetClassTermsVersion":3,"couponRate":5.1234567890123456789012345678,"maturity":"2031-06-15","scheduleConventions":{"couponRateUnit":"Percent"},"callSchedule":[{"callDate":"2030-06-15","callPrice":101.12345678901234567890123456,"priceBasis":"PercentOfPar","exerciseStyle":"European"}],"unregisteredValue":false}""";
        var detail = Detail(id, "Bond", terms);
        var query = Query(detail, Economic(detail));
        await using var app = await App(query);

        using var response = await app.GetTestClient().GetAsync($"/api/security-master/{id}/instrument-terms");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var result = (await response.Content.ReadFromJsonAsync<SecurityInstrumentTermsDto>(Json))!;
        result.SecurityId.Should().Be(id);
        result.Version.Should().Be(7);
        result.Fingerprint.Should().MatchRegex("^[a-f0-9]{64}$");
        result.Source.Status.Should().Be(SecurityInstrumentTermsSourceStatus.Matched);
        result.Source.ProvenanceJson.Should().Contain("custodian-file-17");
        result.AssetSpecificTermsJson.Should().Be(terms);
        var coupon = result.Fields.Single(field => field.Key == "couponRate");
        coupon.ValueText.Should().Be("5.1234567890123456789012345678");
        coupon.Unit.Should().Be("Percent");
        coupon.UnitPath.Should().Be("/assetSpecificTerms/scheduleConventions/couponRateUnit");
        coupon.UnitStatus.Should().Be(SecurityInstrumentTermUnitStatus.Explicit);
        var call = result.Fields.Single(field => field.Key == "callSchedule").Children.Single();
        call.Children.Single(field => field.Key == "callPrice").ValueText.Should().Be("101.12345678901234567890123456");
        call.Children.Single(field => field.Key == "callPrice").Unit.Should().Be("PercentOfPar");
        result.Fields.Single(field => field.Key == "unregisteredValue").ValueText.Should().Be("false");
        result.Fields.Single(field => field.Key == "unregisteredValue").IsDeclared.Should().BeFalse();
    }

    [Fact]
    public void Projection_DistinguishesMissingNullZeroFalseAndAmbiguousAliasesWithoutCoercion()
    {
        var detail = Detail(Guid.NewGuid(), "Bond", """{"couponRate":0,"maturity":null,"parValue":100,"parvalue":101,"callable":false,"spreadBps":"25"}""");
        var result = SecurityInstrumentTermsReadService.Project(detail, null, null, Effective);
        result.Fields.Single(field => field.Key == "couponRate").ValueText.Should().Be("0");
        result.Fields.Single(field => field.Key == "couponRate").State.Should().Be(SecurityInstrumentTermState.Present);
        result.Fields.Single(field => field.Key == "couponRate").UnitStatus.Should().Be(SecurityInstrumentTermUnitStatus.Unspecified);
        result.Fields.Single(field => field.Key == "maturity").State.Should().Be(SecurityInstrumentTermState.Null);
        result.Fields.Single(field => field.Key == "couponType").State.Should().Be(SecurityInstrumentTermState.Missing);
        result.Fields.Where(field => field.Key.Equals("parValue", StringComparison.OrdinalIgnoreCase)).Should().OnlyContain(field => field.State == SecurityInstrumentTermState.Ambiguous);
        result.Fields.Single(field => field.Key == "callable").ValueText.Should().Be("false");
        result.Fields.Single(field => field.Key == "spreadBps").RawJson.Should().Be("\"25\"");
        result.Fields.Single(field => field.Key == "spreadBps").State.Should().Be(SecurityInstrumentTermState.Invalid);
    }

    [Fact]
    public void Projection_PreservesOverpreciseValuesButDoesNotMarkThemValidDecimals()
    {
        const string exact = "0.1234567890123456789012345678901";
        var result = SecurityInstrumentTermsReadService.Project(Detail(Guid.NewGuid(), "Bond", "{\"couponRate\":" + exact + "}"), null, null, Effective);
        var field = result.Fields.Single(field => field.Key == "couponRate");
        field.ValueText.Should().Be(exact);
        field.State.Should().Be(SecurityInstrumentTermState.Invalid);
    }

    [Fact]
    public void Projection_WithholdsForeignOrDifferentVersionProvenanceAndBindsFingerprintToTerms()
    {
        var detail = Detail(Guid.NewGuid(), "Option", """{"expiry":"2027-06-15","strike":123.45678901234567890123456789,"underlyingId":"SPX"}""");
        var result = SecurityInstrumentTermsReadService.Project(detail, Economic(detail) with { Version = 8 }, null, Effective);
        result.Source.Status.Should().Be(SecurityInstrumentTermsSourceStatus.VersionMismatch);
        result.Source.ProvenanceJson.Should().BeNull();
        result.Fields.Single(field => field.Key == "strike").Unit.Should().BeNull("reporting currency does not establish strike currency");
        var newer = SecurityInstrumentTermsReadService.Project(detail with { Version = 8 }, null, null, Effective);
        newer.Fingerprint.Should().NotBe(result.Fingerprint);
        var foreign = SecurityInstrumentTermsReadService.Project(detail, Economic(detail) with { SecurityId = Guid.NewGuid() }, null, Effective);
        foreign.Source.Status.Should().Be(SecurityInstrumentTermsSourceStatus.VersionMismatch);
        foreign.Source.ProvenanceJson.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"termsFormat\":\"future\",\"opaqueSchedule\":[1.1234567890123456789012345678]}")]
    [InlineData("{\"termsFormat\":\"flat\",\"assetClassTermsVersion\":999,\"couponRate\":5}")]
    [InlineData("{\"termsFormat\":\"flat\",\"assetClassTermsVersion\":\"3\",\"couponRate\":5}")]
    [InlineData("{\"termsFormat\":\"flat\",\"schemaVersion\":999,\"couponRate\":5}")]
    public void Projection_UnsupportedEnvelopeRetainsEverySuppliedValue(string terms)
    {
        var result = SecurityInstrumentTermsReadService.Project(Detail(Guid.NewGuid(), "Bond", terms), null, null, Effective);
        result.Status.Should().Be(SecurityInstrumentTermsStatus.Unsupported);
        result.AssetSpecificTermsJson.Should().Be(terms);
        result.Fields.Should().NotBeEmpty();
    }

    [Fact]
    public void Projection_UsesTheExactProfileVersionWithoutLatestFallback()
    {
        var catalog = StaticSecurityAssetProfileCatalog.CreateDefault();
        var knownProfile = catalog.GetProfiles().First();
        var declaredField = knownProfile.Fields.First();
        var terms = JsonSerializer.Serialize(new { termsFormat = "customProfile", customProfileId = knownProfile.ProfileId, profileVersion = knownProfile.Version, profileFields = new Dictionary<string, object?> { [declaredField.Key] = "Retained label", ["extraField"] = 123.456m } });
        var detail = Detail(Guid.NewGuid(), "CustomAsset", terms);
        var result = SecurityInstrumentTermsReadService.Project(detail, null, catalog, Effective);
        result.Schema.ProfileId.Should().Be(knownProfile.ProfileId);
        result.Schema.ProfileVersion.Should().Be(knownProfile.Version);
        var fields = result.Fields.Single(field => field.Key == "profileFields").Children;
        fields.Single(field => field.Key == declaredField.Key).Label.Should().Be(declaredField.Label);
        fields.Single(field => field.Key == declaredField.Key).IsDeclared.Should().BeTrue();
        fields.Single(field => field.Key == "extraField").IsDeclared.Should().BeFalse();
        fields.Should().Contain(field => field.State == SecurityInstrumentTermState.Missing && field.Required);
        var unknown = detail with { AssetSpecificTerms = Element(terms.Replace($"\"profileVersion\":{knownProfile.Version}", "\"profileVersion\":999", StringComparison.Ordinal)) };
        var unavailable = SecurityInstrumentTermsReadService.Project(unknown, null, catalog, Effective);
        unavailable.Status.Should().Be(SecurityInstrumentTermsStatus.Unsupported);
        unavailable.Schema.ProfileStatus.Should().BeNull();
    }

    [Fact]
    public void Projection_UsesSharedScheduleDiagnosticsAndKeepsInvalidSourceRows()
    {
        const string terms = """{"assetClassTermsVersion":3,"principalSchedule":[{"paymentDate":"2028-01-01","amount":-1}]}""";
        var result = SecurityInstrumentTermsReadService.Project(Detail(Guid.NewGuid(), "Bond", terms), null, null, Effective);
        result.Diagnostics.Should().Contain(message => message.Contains("SM_SCHEDULE_", StringComparison.Ordinal));
        result.AssetSpecificTermsJson.Should().Be(terms);
        result.Status.Should().Be(SecurityInstrumentTermsStatus.ReviewRequired);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(UserPermission.None, HttpStatusCode.Forbidden)]
    public async Task Read_RequiresSecurityMasterAuthorityBeforeReading(UserPermission? permissions, HttpStatusCode expected)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        await using var app = await App(query, permissions);
        using var response = await app.GetTestClient().GetAsync($"/api/security-master/{Guid.NewGuid()}/instrument-terms");
        response.StatusCode.Should().Be(expected);
        await query.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Read_UnknownSecurityIsNotFoundAndUnconfiguredServiceDoesNotMap()
    {
        await using var app = await App(Substitute.For<ISecurityMasterQueryService>());
        using var missing = await app.GetTestClient().GetAsync($"/api/security-master/{Guid.NewGuid()}/instrument-terms");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var unconfigured = await App(null);
        using var absent = await unconfigured.GetTestClient().GetAsync($"/api/security-master/{Guid.NewGuid()}/instrument-terms");
        absent.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Read_RefusesForeignCanonicalIdentityBeforeReadingItsProvenance()
    {
        var requestedId = Guid.NewGuid();
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(requestedId, Arg.Any<CancellationToken>()).Returns(Detail(Guid.NewGuid(), "Bond", "{}"));
        var service = new SecurityInstrumentTermsReadService(query);
        var action = () => service.ReadAsync(requestedId);
        await action.Should().ThrowAsync<InvalidOperationException>();
        await query.DidNotReceiveWithAnyArgs().GetEconomicDefinitionByIdAsync(default, default);
    }

    private static ISecurityMasterQueryService Query(SecurityDetailDto detail, SecurityEconomicDefinitionRecord? economic)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(detail.SecurityId, Arg.Any<CancellationToken>()).Returns(detail);
        query.GetEconomicDefinitionByIdAsync(detail.SecurityId, Arg.Any<CancellationToken>()).Returns(economic);
        return query;
    }

    private static async Task<WebApplication> App(ISecurityMasterQueryService? query, UserPermission? permissions = UserPermission.ViewSecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (query is not null) builder.Services.AddSingleton(query);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (permissions.HasValue) context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions.Value;
            await next();
        });
        app.MapSecurityInstrumentTermsEndpoints(Json);
        await app.StartAsync();
        return app;
    }

    private static SecurityDetailDto Detail(Guid id, string assetClass, string terms)
        => new(id, assetClass, SecurityStatusDto.Active, "Retained instrument", "USD", Element("{\"currency\":\"USD\"}"), Element(terms), [], [], 7, Effective, null);
    private static SecurityEconomicDefinitionRecord Economic(SecurityDetailDto detail)
        => new(detail.SecurityId, detail.AssetClass, null, "", "", null, null, detail.Status, detail.DisplayName, detail.Currency,
            Element("{}"), detail.CommonTerms, detail.AssetSpecificTerms, Element("{\"sourceSystem\":\"custodian-file-17\"}"),
            detail.Version, detail.EffectiveFrom, detail.EffectiveTo, [], detail.AssetClass, detail.AssetSpecificTerms);
    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
