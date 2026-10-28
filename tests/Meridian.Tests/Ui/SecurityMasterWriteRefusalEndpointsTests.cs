using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

// Imported as single types rather than whole namespaces: Meridian.Application.SecurityMaster also
// declares ISecurityMasterQueryService, which would make the Contracts one this file uses ambiguous.
using AheadOfWriteFixtures = Meridian.Tests.SecurityMaster.Workbench.AheadOfWriteFixtures;
using SecurityEconomicDefinitionAdapter = Meridian.Application.SecurityMaster.SecurityEconomicDefinitionAdapter;
using SecurityMasterMapping = Meridian.Application.SecurityMaster.SecurityMasterMapping;
using SecurityMasterService = Meridian.Application.SecurityMaster.SecurityMasterService;
using SecurityMasterWorkbenchOptions = Meridian.Application.SecurityMaster.SecurityMasterWorkbenchOptions;
using SecurityMasterWriteRefusedException = Meridian.Application.SecurityMaster.SecurityMasterWriteRefusedException;

namespace Meridian.Tests.Ui;

/// <summary>
/// A refused create or term amendment on the direct write routes answers 400 with the refusal reason
/// as the Problem Details <c>detail</c>, behind the shared Problem Details writer and exception handler
/// a deployed host registers. The real service decides what is a refusal: terms this node reads but does
/// not write, a missing term, an F# domain validation error and a round-trip guard naming its repair.
/// A stored row this node cannot decode (even one a round-trip guard would also refuse; an unrecognized
/// asset class rebuilt from its events is refused with 400 before the decode), a stale version the F#
/// validator rejects, a stream naming another security, a duplicate create, an unknown security and a
/// deactivate keep their 500, with the reason kept out of
/// the body; each of those cases also pins, from the exception the shared handler logs, that the fault
/// it means to reach is the one that answered.
/// </summary>
public sealed class SecurityMasterWriteRefusalEndpointsTests
{
    private const string SignedInOperator = "casey.doyle";

    private const string FxPairInvalidReason = "[fx_currency_pair_invalid] BaseCurrency and QuoteCurrency must differ.";

    private const string CreateRoute = "create";
    private const string AmendRoute = "amend";
    private const string TermBlockRoute = "term-block";
    private const string PreferredEquityTermsRoute = "preferred-equity-terms";
    private const string ConvertibleEquityTermsRoute = "convertible-equity-terms";
    private const string LegacyPreferredTermsRoute = "legacy-preferred-terms";

    private const string InactiveSecurityReason = "[security_inactive] Inactive securities cannot be amended.";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly JsonElement StoredPreferredEquity = JsonSerializer.SerializeToElement(new
    {
        schemaVersion = 1,
        shareClass = "A",
        classification = "Preferred",
        preferredTerms = new
        {
            dividendRate = 5.75m,
            dividendType = "Fixed",
            redemptionPrice = 25.00m,
            liquidationPreference = new { kind = "Pari" }
        }
    });

    private static readonly JsonElement StoredCommonEquity = JsonSerializer.SerializeToElement(new
    {
        schemaVersion = 1,
        shareClass = "A",
        classification = "Common"
    });

    // A Step-coupon bond carrying a floatingIndex the Step arm does not read: re-serializing it would
    // drop the index, so the round-trip guard refuses any write that does not repair it.
    private static readonly JsonElement StepBondWithAStrayFloatingIndex = JsonSerializer.SerializeToElement(
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["maturity"] = "2035-06-15",
            ["couponType"] = "Step",
            ["stepSchedule"] = new[]
            {
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["effectiveDate"] = "2030-06-15",
                    ["rate"] = 0.055m
                }
            },
            ["dayCount"] = "30/360",
            ["floatingIndex"] = "SOFR",
            ["isCallable"] = false,
            ["subclass"] = "StepRate"
        });

    // A profile-backed envelope of a class a newer node registered: this node reads an unknown class
    // only as a flat payload at schemaVersion 1 or 2, so it cannot decode the row.
    private static readonly JsonElement NewerNodeProfileEnvelope = JsonSerializer.SerializeToElement(new
    {
        schemaVersion = 3,
        customProfileId = "infrastructure-holding",
        profileVersion = 1,
        profileFields = new { sponsor = "Meridian Infrastructure" }
    });

    private static string UnrecognizedClassReason(Guid securityId)
        => $"Security '{securityId:D}' has asset class 'InfrastructureHolding', which this node does not recognize. " +
           "Amending or deactivating it here would re-serialize the record through the OtherSecurity fallback and rewrite its " +
           "asset class, so the write is refused. Apply the change from a node that supports this asset class.";

    private static string StrayFloatingIndexRepairReason(Guid securityId)
        => $"Security '{securityId:D}' is a bond carrying floatingIndex that the canonical codec does not read for the " +
           "coupon structure the record resolves to, so re-serializing it here would drop that structure. The write is " +
           "refused. To repair the record here, amend it with a COMPLETE asset-terms document naming a declared couponType " +
           "(Fixed, Floating, ZeroCoupon, Step, InflationLinked) and carrying those values in the flat keys — the amendment " +
           "replaces the terms wholesale, so any field the patch omits is dropped with them — and note that a deactivation " +
           "cannot carry a patch, so repair it first.";

    [Fact]
    public async Task Create_StampedAboveTheReadableVersion_Returns400WithTheUnsupportedVersionReason()
    {
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master",
            CreateRequest(securityId, "Bond", AheadOfWriteFixtures.BondTerms(stamp: 4, withBlock: false)),
            WebJson);

        await AssertRefusedAsync(response,
            "Unsupported assetClassTermsVersion '4' for asset class 'Bond'; this node reads versions 1 through 3.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Create_MissingARequiredTerm_Returns400WithTheMissingTermReason()
    {
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master",
            CreateRequest(securityId, "FxSpot", JsonSerializer.SerializeToElement(new { baseCurrency = "EUR" })),
            WebJson);

        await AssertRefusedAsync(response, "Missing required string 'quoteCurrency'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Create_DeclaringANonFlatTermsFormat_Returns400WithWhatToSubmit()
    {
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master",
            CreateRequest(
                securityId,
                "FxSpot",
                JsonSerializer.SerializeToElement(new { termsFormat = "economic", baseCurrency = "EUR", quoteCurrency = "USD" })),
            WebJson);

        await AssertRefusedAsync(
            response,
            "Terms for asset class 'FxSpot' declare termsFormat 'economic', which a write does not accept. " +
            "Submit asset-specific terms: omit termsFormat, or set it to 'flat' or 'customProfile'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_OfAnInactiveSecurity_Returns400WithTheCodedReason()
    {
        // security_inactive describes the stored record, not the stream, so it is a refusal.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "FxSpot", FxSpotTerms("EUR", "USD"), SecurityStatusDto.Inactive);
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, FxSpotTerms("EUR", "GBP")),
            WebJson);

        await AssertRefusedAsync(response, InactiveSecurityReason);
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Create_FailingDomainValidation_Returns400WithTheCodedReason()
    {
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master",
            CreateRequest(securityId, "FxSpot", FxSpotTerms("EUR", "EUR")),
            WebJson);

        await AssertRefusedAsync(response, FxPairInvalidReason);
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_RefusedByTheRoundTripGuard_Returns400WithTheRepairReason()
    {
        // A patch-less amend re-serializes the stored bond, which would drop its floatingIndex: the
        // guard's reason names the repair, so the caller gets it.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "Bond", StepBondWithAStrayFloatingIndex);
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, assetSpecificTermsPatch: null),
            WebJson);

        await AssertRefusedAsync(response, StrayFloatingIndexRepairReason(securityId));
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_FailingDomainValidation_Returns400()
    {
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "FxSpot", FxSpotTerms("EUR", "USD"));
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, FxSpotTerms("EUR", "EUR")),
            WebJson);

        await AssertRefusedAsync(response, FxPairInvalidReason);
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AmendTermBlock_OnARowStampedAboveTheWriteVersion_Returns400WithTheWriteVersionReason()
    {
        // The block passes the route's own pre-check; the service refuses the stamp the block
        // amendment carries over from the stored row. This answered 500 before.
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var terms = JsonNode.Parse(AheadOfWriteFixtures.SwapAtWriteVersion)!.AsObject();
        terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 2;
        var swapTerms = JsonSerializer.SerializeToElement(terms);
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Detail(securityId, "Swap", swapTerms, version: 2));
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "Swap", swapTerms);
        await using var app = await CreateAppAsync(queryService, harness.Service);

        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/security-master/{securityId}/terms/legs",
            TermBlockRequest(expectedVersion: 2, swapTerms.GetProperty("legs")),
            WebJson);

        await AssertRefusedAsync(response,
            "assetClassTermsVersion '2' for asset class 'Swap' is readable but not writable on this node: it reads " +
            "versions 1 through 2 and writes version 1. Submit the terms at version 1, or apply the change from a node that writes version 2.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_OfAStoredRowThisNodeCannotDecode_Returns500WithoutTheReason()
    {
        // The same message family as the missing-term create, but raised rebuilding the STORED row
        // before the request is mapped: a server fault, not something the caller can correct. With no
        // events and no snapshot the rebuild falls back to the projection and decodes it strictly.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "FxSpot", JsonSerializer.SerializeToElement(new { baseCurrency = "EUR" }));
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, FxSpotTerms("EUR", "USD")),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "quoteCurrency");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Missing required string 'quoteCurrency'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_OfAStoredEventThisNodeCannotDecode_Returns500WithoutTheReason()
    {
        // The usual shape of a stored security: the rebuild replays its event, which deserializes the
        // stored terms without decoding them, so the first strict decode is of the current record the
        // amendment starts from. That is still the stored row, not the request.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "FxSpot",
            FxSpotTerms("EUR", "USD"),
            stored => stored with { LegacyAssetSpecificTerms = JsonSerializer.SerializeToElement(new { baseCurrency = "EUR" }) });
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, FxSpotTerms("EUR", "USD")),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "quoteCurrency");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Missing required string 'quoteCurrency'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AmendWithoutAPatch_OfAStoredEventStampedAboveTheWriteVersionThisNodeCannotDecode_Returns500()
    {
        // A patch-less amend of a row stamped above the write version: the decode of the stored row
        // faults before the ahead-of-write guard, which would decode the same row again. The guard
        // ordering itself is pinned by the coupon-guard case below.
        var securityId = Guid.NewGuid();
        var stampedWithoutMaturity = JsonNode.Parse(AheadOfWriteFixtures.BondTerms(stamp: 2, withBlock: false).GetRawText())!.AsObject();
        stampedWithoutMaturity.Remove("maturity");
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "Bond",
            AheadOfWriteFixtures.BondTerms(stamp: 2, withBlock: false),
            stored => stored with { LegacyAssetSpecificTerms = AheadOfWriteFixtures.Parse(stampedWithoutMaturity.ToJsonString()) });
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, assetSpecificTermsPatch: null),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "maturity");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Missing required date 'maturity'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AmendWithoutAPatch_OfAStoredEventThisNodeCannotDecodeThatTheCouponGuardAlsoRefuses_Returns500()
    {
        // The bond coupon guard reads the stored terms without decoding them. Run first, it would answer
        // 400 naming a repair that then fails at the decode, so the stored row is decoded before it.
        var securityId = Guid.NewGuid();
        var strayIndexWithoutMaturity = JsonNode.Parse(StepBondWithAStrayFloatingIndex.GetRawText())!.AsObject();
        strayIndexWithoutMaturity.Remove("maturity");
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "Bond",
            StepBondWithAStrayFloatingIndex,
            stored => stored with { LegacyAssetSpecificTerms = AheadOfWriteFixtures.Parse(strayIndexWithoutMaturity.ToJsonString()) });
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, assetSpecificTermsPatch: null),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "floatingIndex");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Missing required date 'maturity'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AmendWithoutAPatch_OfAStoredEventOfAClassThisNodeDoesNotRecognizeOrDecode_Returns400WithTheClassReason()
    {
        // The unrecognized-class refusal runs before the stored row is decoded: its repair is on a node
        // that supports the class, so it holds even when this node cannot decode the row.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "FxSpot",
            FxSpotTerms("EUR", "USD"),
            stored => stored with { LegacyAssetClass = "InfrastructureHolding", LegacyAssetSpecificTerms = NewerNodeProfileEnvelope });
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, assetSpecificTermsPatch: null),
            WebJson);

        await AssertRefusedAsync(response, UnrecognizedClassReason(securityId));
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_OfAStoredEquityEventWithoutItsRetainedTerms_Returns500WithoutTheReason()
    {
        // Replaying an equity event that lacks its retained flat terms fails while the current record
        // is rebuilt from the event, before any guard or the request is read.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new { schemaVersion = 1, shareClass = "A", classification = "Common" }),
            stored => stored with { LegacyAssetSpecificTerms = null });
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, assetSpecificTermsPatch: null),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "retained legacy asset-specific terms");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be(
                "Equity economic replay requires retained legacy asset-specific terms; " +
                "the economic payload cannot reconstruct the complete classification and preferred/convertible terms.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_WithAStaleVersionAndAPatchTheFSharpValidatorRefuses_IsNotAReportedRefusal()
    {
        // The F# validator reports version_conflict beside the pair error; the stale version wins, so
        // the amend is not reported as refused for its terms.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "FxSpot", FxSpotTerms("EUR", "USD"));
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 1, FxSpotTerms("EUR", "EUR")),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "fx_currency_pair_invalid");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be(
                "[version_conflict] ExpectedVersion does not match the current security version.; " + FxPairInvalidReason);
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_WithAStaleVersionAndAPatchTheMappingRefuses_Returns400WithTheRefusal()
    {
        // /amend compares the expected version only in the F# validator, after the request is mapped. A
        // patch the mapping refuses is refused whatever the version, so that reason answers first. The
        // term-block, preferred and convertible routes compare the version before building the patch.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "FxSpot", FxSpotTerms("EUR", "USD"));
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 1, JsonSerializer.SerializeToElement(new { baseCurrency = "EUR" })),
            WebJson);

        await AssertRefusedAsync(response, "Missing required string 'quoteCurrency'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Amend_OfAStreamWhoseStoredDefinitionNamesAnotherSecurity_IsNotAReportedRefusal()
    {
        // security_id_mismatch describes the stream, not the submitted terms, so it wins over the pair
        // error the F# validator reports beside it.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "FxSpot",
            FxSpotTerms("EUR", "USD"),
            stored => stored with { SecurityId = Guid.NewGuid() });
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, FxSpotTerms("EUR", "EUR")),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "fx_currency_pair_invalid");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be(
                "[security_id_mismatch] AmendTerms.SecurityId must match the current aggregate SecurityId.; " + FxPairInvalidReason);
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Create_OfAnAlreadyMasteredSecurity_IsNotAReportedRefusal()
    {
        var securityId = Guid.NewGuid();
        var conflict = new SecurityMasterStreamVersionConflictException(securityId, 0, 1);
        using var harness = new CanonicalServiceHarness();
        harness.EventStore
            .AppendAsync(securityId, 0, Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(conflict);
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master",
            CreateRequest(securityId, "FxSpot", FxSpotTerms("EUR", "USD")),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "stream version conflict");
        fault.Should().BeSameAs(conflict);
        await harness.EventStore.Received(1).AppendAsync(
            securityId, 0, Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Amend_OfAnUnknownSecurity_IsNotAReportedRefusal()
    {
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns((SecurityProjectionRecord?)null);
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, FxSpotTerms("EUR", "USD")),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "was not found");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be($"Security '{securityId}' was not found.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Deactivate_RefusedByTheRoundTripGuard_StillReturns500()
    {
        // Deactivate raises the same refusal type as the patch-less amend above, but its route is out
        // of scope and keeps the shared handler's 500.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "Bond", StepBondWithAStrayFloatingIndex);
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/deactivate",
            new DeactivateSecurityRequest(securityId, 2, DateTimeOffset.UtcNow, "test", "codex", null, "deactivate"),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "floatingIndex");
        fault.Should().BeOfType<SecurityMasterWriteRefusedException>()
            .Which.Message.Should().Be(StrayFloatingIndexRepairReason(securityId));
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Deactivate_OfAStoredEventThisNodeCannotDecodeThatTheCouponGuardAlsoRefuses_FaultsWithTheDecodeError()
    {
        // Deactivation decodes the stored row before the guards too, so the fault it logs is the decode
        // error rather than a refusal of a row it cannot read.
        var securityId = Guid.NewGuid();
        var strayIndexWithoutMaturity = JsonNode.Parse(StepBondWithAStrayFloatingIndex.GetRawText())!.AsObject();
        strayIndexWithoutMaturity.Remove("maturity");
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "Bond",
            StepBondWithAStrayFloatingIndex,
            stored => stored with { LegacyAssetSpecificTerms = AheadOfWriteFixtures.Parse(strayIndexWithoutMaturity.ToJsonString()) });
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/deactivate",
            new DeactivateSecurityRequest(securityId, 2, DateTimeOffset.UtcNow, "test", "codex", null, "deactivate"),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "floatingIndex");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Missing required date 'maturity'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AmendWithoutAPatch_OfAProjectionOnlyRowOfAClassThisNodeDoesNotRecognizeOrDecode_Returns500()
    {
        // With no events or snapshot the rebuild falls back to the projection and decodes it, before
        // the class check can run, so the class refusal is reached only for a security rebuilt from its
        // events, as a newer node writes it.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "InfrastructureHolding", NewerNodeProfileEnvelope);
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/amend",
            AmendRequest(securityId, expectedVersion: 2, assetSpecificTermsPatch: null),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "InfrastructureHolding");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Unsupported schemaVersion '3' for asset class 'InfrastructureHolding'.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Deactivate_OfAStoredEventOfAClassThisNodeDoesNotRecognizeOrDecode_FaultsWithTheClassRefusal()
    {
        // Deactivation checks the class before the decode too; its route keeps the 500.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "FxSpot",
            FxSpotTerms("EUR", "USD"),
            stored => stored with { LegacyAssetClass = "InfrastructureHolding", LegacyAssetSpecificTerms = NewerNodeProfileEnvelope });
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), harness.Service, handlerLog);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/deactivate",
            new DeactivateSecurityRequest(securityId, 2, DateTimeOffset.UtcNow, "test", "codex", null, "deactivate"),
            WebJson);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "InfrastructureHolding");
        fault.Should().BeOfType<SecurityMasterWriteRefusedException>()
            .Which.Message.Should().Be(UnrecognizedClassReason(securityId));
        await harness.AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData(PreferredEquityTermsRoute)]
    [InlineData(LegacyPreferredTermsRoute)]
    public async Task PreferredEquityTermsRoute_OfAnInactiveSecurity_Returns400ThroughTheService(string route)
    {
        // The route builds the patch from the stored row and amends through the shared seam, whose F#
        // validator refuses the inactive record.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "Equity", StoredPreferredEquity, SecurityStatusDto.Inactive);
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetPreferredEquityTermsAsync(securityId, Arg.Any<CancellationToken>()).Returns(PreferredTerms(securityId));
        await using var app = await CreateAppAsync(queryService, harness.Service);

        using var response = await app.GetTestClient().SendAsync(
            JsonRequest(HttpMethod.Patch, PreferredRoutePath(route, securityId), PreferredRequest() with { ExpectedVersion = 2 }));

        await AssertRefusedAsync(response, InactiveSecurityReason);
        await harness.AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData(PreferredEquityTermsRoute)]
    [InlineData(LegacyPreferredTermsRoute)]
    [InlineData(ConvertibleEquityTermsRoute)]
    public async Task EquityTermsRoute_WhoseStoredRowLacksTheTerms_Returns500WithoutTheReason(string route)
    {
        // The read model says the terms exist; the stored row the patch is built from does not hold them.
        // The patch builders read the stored row, so their failure is a fault, not a refusal.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.Seed(securityId, "Equity", StoredCommonEquity);
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetPreferredEquityTermsAsync(securityId, Arg.Any<CancellationToken>()).Returns(PreferredTerms(securityId));
        queryService.GetConvertibleEquityTermsAsync(securityId, Arg.Any<CancellationToken>()).Returns(ConvertibleTerms(securityId));
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(queryService, harness.Service, handlerLog);

        using var response = await app.GetTestClient().SendAsync(route == ConvertibleEquityTermsRoute
            ? JsonRequest(HttpMethod.Patch, $"/api/security-master/{securityId}/convertible-equity-terms", ConvertibleRequest() with { ExpectedVersion = 2 })
            : JsonRequest(HttpMethod.Patch, PreferredRoutePath(route, securityId), PreferredRequest() with { ExpectedVersion = 2 }));

        var terms = route == ConvertibleEquityTermsRoute ? "convertible-equity terms" : "preferred-equity terms";
        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: terms);
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be($"Security '{securityId}' does not currently have {terms}.");
        await harness.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AmendTermBlock_OfAStoredEquityEventWithoutItsRetainedTerms_Returns500WithoutTheReason()
    {
        // The block patch is built from the stored projection, which decodes; the amendment then rebuilds
        // the record from its event, which cannot be replayed. A fault, as on /amend.
        var securityId = Guid.NewGuid();
        using var harness = new CanonicalServiceHarness();
        harness.SeedEventStream(
            securityId,
            "Equity",
            StoredPreferredEquity,
            stored => stored with { LegacyAssetSpecificTerms = null });
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(PreferredEquityDetail(securityId));
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(queryService, harness.Service, handlerLog);

        using var response = await app.GetTestClient().SendAsync(JsonRequest(
            HttpMethod.Put,
            $"/api/security-master/{securityId}/terms/preferredTerms",
            TermBlockRequest(expectedVersion: 2, JsonSerializer.SerializeToElement(new { dividendRate = 6.25m, dividendType = "Cumulative" }))));

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "retained legacy asset-specific terms");
        fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be(
                "Equity economic replay requires retained legacy asset-specific terms; " +
                "the economic payload cannot reconstruct the complete classification and preferred/convertible terms.");
        await harness.AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData(CreateRoute)]
    [InlineData(AmendRoute)]
    [InlineData(TermBlockRoute)]
    [InlineData(PreferredEquityTermsRoute)]
    [InlineData(ConvertibleEquityTermsRoute)]
    [InlineData(LegacyPreferredTermsRoute)]
    public async Task DirectTermWriteRoute_ServiceRefusal_Returns400WithTheReason(string route)
    {
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        using var request = ArrangeServiceThrowing(
            route, queryService, service, new SecurityMasterWriteRefusedException("refusal reason"));
        await using var app = await CreateAppAsync(queryService, service);

        using var response = await app.GetTestClient().SendAsync(request);

        await AssertRefusedAsync(response, "refusal reason");
    }

    [Theory]
    [InlineData(CreateRoute)]
    [InlineData(AmendRoute)]
    [InlineData(TermBlockRoute)]
    [InlineData(PreferredEquityTermsRoute)]
    [InlineData(ConvertibleEquityTermsRoute)]
    [InlineData(LegacyPreferredTermsRoute)]
    public async Task DirectTermWriteRoute_PlainInvalidOperation_Returns500WithoutTheMessage(string route)
    {
        // Only the refusal type is translated: the base type it derives from still reaches the shared
        // handler, which hides the message.
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        var thrown = new InvalidOperationException("sensitive implementation detail");
        using var request = ArrangeServiceThrowing(route, queryService, service, thrown);
        var handlerLog = new RecordingLogger<MeridianApiExceptionHandler>();
        await using var app = await CreateAppAsync(queryService, service, handlerLog);

        using var response = await app.GetTestClient().SendAsync(request);

        var fault = await AssertServerFaultAsync(response, handlerLog, undisclosed: "sensitive implementation detail");
        fault.Should().BeSameAs(thrown);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string expectedDetail)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().Should().Be(ApiProblemTypes.Validation);
        problem.RootElement.GetProperty("title").GetString().Should().Be("Validation Failed");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        problem.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);
    }

    /// <summary>
    /// Asserts the shared handler's 500 with the reason kept out of the body, and returns the exception
    /// the handler logged so the caller can pin which fault produced it: any unhandled exception would
    /// pass the response checks alone.
    /// </summary>
    private static async Task<Exception> AssertServerFaultAsync(
        HttpResponseMessage response,
        RecordingLogger<MeridianApiExceptionHandler> handlerLog,
        string undisclosed)
    {
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(undisclosed);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("type").GetString().Should().Be(ApiProblemTypes.Internal);
        problem.RootElement.GetProperty("title").GetString().Should().Be("Internal Server Error");
        problem.RootElement.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");

        var logged = handlerLog.Entries.Should().ContainSingle().Which;
        logged.LogLevel.Should().Be(LogLevel.Error);
        logged.Exception.Should().NotBeNull();
        return logged.Exception!;
    }

    /// <summary>
    /// Sets the substitute service to throw <paramref name="thrown"/> from the write <paramref name="route"/>
    /// reaches, satisfies the route's existence and block pre-checks, and builds a request it binds.
    /// </summary>
    private static HttpRequestMessage ArrangeServiceThrowing(
        string route,
        ISecurityMasterQueryService queryService,
        ISecurityMasterService service,
        Exception thrown)
    {
        var securityId = Guid.NewGuid();
        switch (route)
        {
            case CreateRoute:
                service.CreateAsync(Arg.Any<CreateSecurityRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(thrown);
                return JsonRequest(
                    HttpMethod.Post,
                    "/api/security-master",
                    CreateRequest(securityId, "FxSpot", FxSpotTerms("EUR", "USD")));

            case AmendRoute:
                service.AmendTermsAsync(Arg.Any<AmendSecurityTermsRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(thrown);
                return JsonRequest(
                    HttpMethod.Post,
                    "/api/security-master/amend",
                    AmendRequest(securityId, expectedVersion: 7, FxSpotTerms("EUR", "USD")));

            case TermBlockRoute:
                queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(PreferredEquityDetail(securityId));
                service.AmendTermBlockAsync(securityId, "preferredTerms", Arg.Any<AmendSecurityTermBlockRequest>(), Arg.Any<CancellationToken>())
                    .ThrowsAsync(thrown);
                return JsonRequest(
                    HttpMethod.Put,
                    $"/api/security-master/{securityId}/terms/preferredTerms",
                    TermBlockRequest(
                        expectedVersion: 7,
                        JsonSerializer.SerializeToElement(new { dividendRate = 6.25m, dividendType = "Cumulative" })));

            case PreferredEquityTermsRoute:
            case LegacyPreferredTermsRoute:
                queryService.GetPreferredEquityTermsAsync(securityId, Arg.Any<CancellationToken>())
                    .Returns(PreferredTerms(securityId));
                service.AmendPreferredEquityTermsAsync(securityId, Arg.Any<AmendPreferredEquityTermsRequest>(), Arg.Any<CancellationToken>())
                    .ThrowsAsync(thrown);
                return JsonRequest(
                    HttpMethod.Patch,
                    route == LegacyPreferredTermsRoute
                        ? $"/api/security-master/equities/{securityId}/preferred-terms"
                        : $"/api/security-master/{securityId}/preferred-equity-terms",
                    PreferredRequest());

            case ConvertibleEquityTermsRoute:
                queryService.GetConvertibleEquityTermsAsync(securityId, Arg.Any<CancellationToken>())
                    .Returns(ConvertibleTerms(securityId));
                service.AmendConvertibleEquityTermsAsync(securityId, Arg.Any<AmendConvertibleEquityTermsRequest>(), Arg.Any<CancellationToken>())
                    .ThrowsAsync(thrown);
                return JsonRequest(
                    HttpMethod.Patch,
                    $"/api/security-master/{securityId}/convertible-equity-terms",
                    ConvertibleRequest());

            default:
                throw new ArgumentOutOfRangeException(nameof(route), route, null);
        }
    }

    private static string PreferredRoutePath(string route, Guid securityId)
        => route == LegacyPreferredTermsRoute
            ? $"/api/security-master/equities/{securityId}/preferred-terms"
            : $"/api/security-master/{securityId}/preferred-equity-terms";

    private static HttpRequestMessage JsonRequest<T>(HttpMethod method, string path, T body)
        => new(method, path) { Content = JsonContent.Create(body, options: WebJson) };

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterQueryService queryService,
        ISecurityMasterService service,
        ILogger<MeridianApiExceptionHandler>? handlerLog = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        // The option is init-only, so bind it the way the host does. Off, the governed-route
        // requirement lets the direct term-amendment routes through to the service.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SecurityMasterWorkbenchOptions.SectionName}:{nameof(SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments)}"] = "false"
        });
        builder.Services.Configure<SecurityMasterWorkbenchOptions>(
            builder.Configuration.GetSection(SecurityMasterWorkbenchOptions.SectionName));
        builder.Services.AddSingleton(queryService);
        builder.Services.AddSingleton(service);
        // The deployed host's Problem Details writer and exception handler, so a refusal and a fault
        // are both answered as a real request would be.
        builder.Services.AddMeridianApiProblemDetails();
        if (handlerLog is not null)
        {
            // The shared handler logs the exception it hides behind the 500; capturing it lets a case
            // pin which fault answered.
            builder.Services.AddSingleton(handlerLog);
        }

        var app = builder.Build();
        app.UseExceptionHandler();
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

    private static JsonElement FxSpotTerms(string baseCurrency, string quoteCurrency)
        => JsonSerializer.SerializeToElement(new { baseCurrency, quoteCurrency });

    private static CreateSecurityRequest CreateRequest(Guid securityId, string assetClass, JsonElement assetSpecificTerms)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = $"Meridian {assetClass}",
                currency = "USD"
            }),
            AssetSpecificTerms: assetSpecificTerms,
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MRDN", true, DateTimeOffset.UtcNow.AddDays(-1))
            ],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "untrusted.body.actor",
            SourceRecordId: null,
            Reason: "endpoint create");

    private static AmendSecurityTermsRequest AmendRequest(Guid securityId, long expectedVersion, JsonElement? assetSpecificTermsPatch)
        => new(
            SecurityId: securityId,
            ExpectedVersion: expectedVersion,
            CommonTerms: null,
            AssetSpecificTermsPatch: assetSpecificTermsPatch,
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "untrusted.body.actor",
            SourceRecordId: null,
            Reason: "endpoint amend");

    private static AmendSecurityTermBlockRequest TermBlockRequest(long expectedVersion, JsonElement block)
        => new(
            ExpectedVersion: expectedVersion,
            Block: block,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "untrusted.body.actor",
            SourceRecordId: null,
            Reason: "endpoint block amend");

    private static AmendPreferredEquityTermsRequest PreferredRequest()
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
            UpdatedBy: "untrusted.body.actor",
            SourceRecordId: null,
            Reason: "endpoint patch");

    private static AmendConvertibleEquityTermsRequest ConvertibleRequest()
        => new(
            ExpectedVersion: 7,
            UnderlyingSecurityId: Guid.NewGuid(),
            ConversionRatio: 2.5m,
            ConversionPrice: 40.00m,
            ConversionStartDate: new DateOnly(2030, 1, 15),
            ConversionEndDate: new DateOnly(2032, 1, 15),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "untrusted.body.actor",
            SourceRecordId: null,
            Reason: "endpoint patch");

    private static PreferredEquityTermsDto PreferredTerms(Guid securityId)
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
            Version: 7);

    private static ConvertibleEquityTermsDto ConvertibleTerms(Guid securityId)
        => new(
            SecurityId: securityId,
            Classification: "Convertible",
            UnderlyingSecurityId: Guid.NewGuid(),
            ConversionRatio: 2.0m,
            ConversionPrice: 35.00m,
            ConversionStartDate: new DateOnly(2029, 1, 15),
            ConversionEndDate: new DateOnly(2031, 1, 15),
            Version: 7);

    private static SecurityDetailDto BondDetail(Guid securityId)
        => Detail(
            securityId,
            "Bond",
            AheadOfWriteFixtures.BondTerms(stamp: 2, withBlock: false),
            version: 2);

    private static SecurityDetailDto PreferredEquityDetail(Guid securityId)
        => Detail(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "A",
                classification = "Preferred",
                preferredTerms = new
                {
                    dividendRate = 5.75m,
                    dividendType = "Fixed"
                }
            }),
            version: 7);

    private static SecurityDetailDto Detail(Guid securityId, string assetClass, JsonElement assetSpecificTerms, long version)
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
            AssetSpecificTerms: assetSpecificTerms,
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MRDN", true, DateTimeOffset.UtcNow.AddDays(-10))
            ],
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: version,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo: null);

    /// <summary>
    /// The real canonical service over substitute stores. With no events and no snapshot the rebuilder
    /// falls back to the seeded projection, so the stored document reaches the guards verbatim.
    /// </summary>
    private sealed class CanonicalServiceHarness : IDisposable
    {
        public CanonicalServiceHarness()
        {
            EventStore = Substitute.For<ISecurityMasterEventStore>();
            Store = Substitute.For<ISecurityMasterStore>();
            var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
            Service = new SecurityMasterService(
                EventStore,
                snapshotStore,
                Store,
                new SecurityMasterAggregateRebuilder(EventStore, snapshotStore),
                new SecurityMasterOptions
                {
                    SnapshotIntervalVersions = 50,
                    ResolveInactiveByDefault = true
                },
                NullLogger<SecurityMasterService>.Instance);
        }

        public ISecurityMasterEventStore EventStore { get; }

        public ISecurityMasterStore Store { get; }

        public SecurityMasterService Service { get; }

        /// <summary>
        /// Stores the security as a stream holding one economic-definition event at version 2: the
        /// definition a write of <paramref name="terms"/> records, changed by <paramref name="stored"/> to
        /// what the stored event carries. With an event to replay the rebuild never decodes the projection,
        /// which still holds <paramref name="terms"/> and supplies only the aliases.
        /// </summary>
        public void SeedEventStream(
            Guid securityId,
            string assetClass,
            JsonElement terms,
            Func<SecurityEconomicDefinitionRecord, SecurityEconomicDefinitionRecord> stored)
        {
            var projection = Seed(securityId, assetClass, terms);
            var envelope = SecurityMasterMapping.ToEventEnvelope(
                stored(SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection)),
                "TermsAmended",
                "codex",
                "test",
                reason: null,
                projection.Version);
            EventStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
                .Returns(new[] { envelope with { SecurityId = securityId } });
        }

        /// <summary>Stores a security at version 2, active unless <paramref name="status"/> says otherwise, whose asset-specific terms are exactly <paramref name="terms"/>.</summary>
        public SecurityProjectionRecord Seed(
            Guid securityId,
            string assetClass,
            JsonElement terms,
            SecurityStatusDto status = SecurityStatusDto.Active)
        {
            var projection = new SecurityProjectionRecord(
                securityId,
                assetClass,
                status,
                $"Meridian {assetClass}",
                "USD",
                "Ticker",
                "MRDN",
                JsonSerializer.SerializeToElement(new
                {
                    displayName = $"Meridian {assetClass}",
                    currency = "USD",
                    exchange = "XNYS",
                    lotSize = 1,
                    tickSize = 0.01m
                }),
                terms,
                JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "test",
                    asOf = DateTimeOffset.UtcNow,
                    updatedBy = "codex"
                }),
                2,
                DateTimeOffset.UtcNow.AddDays(-1),
                null,
                [
                    new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MRDN", true, DateTimeOffset.UtcNow.AddDays(-1))
                ],
                Array.Empty<SecurityAliasDto>());
            Store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);
            return projection;
        }

        public async Task AssertNothingWrittenAsync()
        {
            await EventStore.DidNotReceiveWithAnyArgs().AppendAsync(default, default, default!, default);
            await Store.DidNotReceiveWithAnyArgs().UpsertProjectionAsync(default!, default);
        }

        public void Dispose() => Service.Dispose();
    }
}
