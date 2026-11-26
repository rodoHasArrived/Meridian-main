using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityVanillaSwapProfileTests
{
    private static readonly DateOnly Issue = new(2026, 1, 1);
    private static readonly DateTimeOffset KnownAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GovernedLegDeclarations_RoundTripCalculateAndRetainTheExistingReplayPacket(bool fixedAsFraction)
    {
        var security = Security();
        if (fixedAsFraction)
        {
            var terms = JsonNode.Parse(security.AssetSpecificTerms.GetRawText())!;
            terms["legs"]![0]!["fixedRate"] = .0365m;
            terms["legs"]![0]!["scheduleConventions"]!["couponRateUnit"] = "Fraction";
            security = security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
        }
        SecurityAssetPackAdmission.EnsureSubmittedFields(security.AssetClass, security.AssetSpecificTerms);
        var command = SecurityMasterMapping.ToCreateCommand(new(security.SecurityId, security.AssetClass,
            JsonSerializer.SerializeToElement(new { displayName = security.DisplayName, currency = security.Currency }),
            security.AssetSpecificTerms, [new(SecurityIdentifierKind.InternalCode, "SYNTHETIC-IRS", true, security.EffectiveFrom)],
            security.EffectiveFrom, "Synthetic", "preparer", null, "Reviewed vanilla leg terms"));
        var created = SecurityMasterCommandFacade.Create(command);
        created.IsSuccess.Should().BeTrue(string.Join("; ", created.ErrorDetails.Select(error => error.Message)));
        var projection = SecurityMasterMapping.ToProjection(created.Snapshot);
        SecurityAssetPackAdmission.EnsureAdmitted(projection, security.EffectiveFrom);
        security = SecurityMasterMapping.ToDetail(projection);

        var curves = Substitute.For<ISecurityCurveStore>();
        var curveId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        curves.GetDefinitionAsync(curveId, Arg.Any<CancellationToken>()).Returns(new SecurityCurveDefinitionDto(
            curveId, "USD-SOFR", SecurityCurveKind.Forward, "USD", "SOFR", null, "ACT/360",
            SecurityCurveInterpolation.Linear, "Synthetic", null, "reviewer", KnownAt));
        curves.GetVersionByIdAsync(curveId, versionId, Arg.Any<CancellationToken>()).Returns(new SecurityCurveVersionDto(
            versionId, curveId, "USD-SOFR", 1, Issue, KnownAt, null, "Synthetic", "reviewer",
            [new(Issue, .05m), new(new(2026, 4, 1), .05m)], "synthetic-forward-content"));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(security.SecurityId, KnownAt, Arg.Any<CancellationToken>()).Returns(security);
        var calendars = new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
        var service = new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>(), curves, calendars);
        var request = new ContractualCashFlowRequest(Issue, KnownAt, CurvePins: [new("SOFR", curveId, versionId)])
        { CalculationProfile = SecurityVanillaSwapProfile.Id };

        var result = (await service.PreviewSecurityAsync(security.SecurityId, request))!;

        result.Blockers.Should().BeEmpty();
        result.IsPostable.Should().BeFalse();
        // Fixed ACT/365 pay: 100,000 x 3.65% x 90 / 365 = -900.
        // Floating ACT/360 receive at 5%: 100,000 x 5% x 90 / 360 = 1,250.
        result.Events.Sum(flow => flow.NetCash).Should().BeApproximately(350m, .000001m);
        result.Events.Sum(flow => flow.ScheduledPrincipal + flow.RedeemedPrincipal + flow.ForecastPrepayment).Should().Be(0m);
        result.Evidence.Calendars.Should().NotBeEmpty();
        result.ResolvedContract!.Streams.Select(stream => stream.PaymentMonths).Should().Equal(3, 1);
        var run = new ContractualCashFlowRun(Guid.NewGuid(), "Security", security.SecurityId, KnownAt,
            result.ResolvedContract, request, result, "reviewer");
        var directory = Path.Combine(Path.GetTempPath(), "vanilla-irs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new FileContractualCashFlowRunStore(new StorageOptions { RootPath = directory }).SaveAsync(run);
            var restored = await new FileContractualCashFlowRunStore(new StorageOptions { RootPath = directory })
                .GetAsync("Security", security.SecurityId, run.RunId);
            restored!.Request.CalculationProfile.Should().Be(SecurityVanillaSwapProfile.Id);
            ContractualCashFlowEngine.Verify(restored).Should().BeTrue();
            ContractualCashFlowEngine.Replay(restored).Evidence.OutputHash.Should().Be(result.Evidence.OutputHash);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "legId", "null")]
    [InlineData(1, "legId", "\"fixed-pay\"")]
    [InlineData(1, "direction", "\"Pay\"")]
    [InlineData(1, "currency", "\"EUR\"")]
    [InlineData(1, "notional", "999")]
    [InlineData(0, "exchangesPrincipal", "true")]
    [InlineData(1, "scheduleConventions", "null")]
    [InlineData(0, "fixedRate", "null")]
    [InlineData(1, "index", "null")]
    [InlineData(1, "legType", "\"Fixed\"")]
    [InlineData(0, "legType", "\"Floating\"")]
    [InlineData(1, "notionalSchedule", "[{\"effectiveDate\":\"2026-02-01\",\"notional\":90000}]")]
    [InlineData(1, "currentIndexRate", "0.05")]
    [InlineData(1, "resetSchedule", "[{\"resetDate\":\"2026-01-01\",\"fixingRate\":0.05,\"fee\":100}]")]
    public async Task UnsupportedShape_IsRefusedByBothAuthoringAndTheExplicitProfile(int leg, string key, string value)
    {
        var security = Security(leg, key, value);
        var write = () => SecurityAssetPackAdmission.EnsureSubmittedFields(security.AssetClass, security.AssetSpecificTerms);
        write.Should().Throw<InvalidOperationException>();
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(security.SecurityId, KnownAt, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>())
            .PreviewSecurityAsync(security.SecurityId, new(Issue, KnownAt) { CalculationProfile = SecurityVanillaSwapProfile.Id });
        result!.Blockers.Should().NotBeEmpty();
        result.Events.Should().BeEmpty();
        result.IsPostable.Should().BeFalse();
    }

    [Fact]
    public void UndeclaredLegResidue_CannotActivateOnAnUnrelatedAmendment()
    {
        var retained = JsonNode.Parse(Security().AssetSpecificTerms.GetRawText())!.AsObject();
        retained[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 1;
        var document = JsonSerializer.SerializeToElement(retained);
        var amend = () => ScheduleTermsWriteRules.EnsureAmendmentDoesNotPromoteStrayContainers("Swap", document, null);
        amend.Should().Throw<InvalidOperationException>().WithMessage("*undeclared leg conventions*unrelated amendment*");
        var reviewed = () => ScheduleTermsWriteRules.EnsureAmendmentDoesNotPromoteStrayContainers("Swap", document,
            Security().AssetSpecificTerms);
        reviewed.Should().NotThrow();
        var profile = () => SecurityVanillaSwapProfile.FromSecurity(Security() with { AssetSpecificTerms = document });
        profile.Should().Throw<InvalidOperationException>().WithMessage("*undeclared*terms version*");
    }

    [Theory]
    [InlineData(false, "ForwardCurveMissing")]
    [InlineData(true, "HistoricalFixingMissing")]
    public async Task MissingRateEvidence_RefusesTheWholeProfile(bool missingHistoricalReset, string code)
    {
        var security = missingHistoricalReset ? Security(1, "resetSchedule", "[]") : Security();
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(security.SecurityId, KnownAt, Arg.Any<CancellationToken>()).Returns(security);
        var calendars = new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>(), calendars: calendars)
            .PreviewSecurityAsync(security.SecurityId, new(missingHistoricalReset ? Issue.AddDays(1) : Issue, KnownAt)
            { CalculationProfile = SecurityVanillaSwapProfile.Id });
        result!.Blockers.Should().ContainSingle().Which.Code.Should().Be(code);
        result.Events.Should().BeEmpty("one blocked leg cannot leave the other leg's cash behind");
    }

    [Fact]
    public void FullAdmission_RejectsTwoForeignLegsAgainstTheSecurityCurrency()
    {
        var security = Security();
        var terms = JsonNode.Parse(security.AssetSpecificTerms.GetRawText())!;
        terms["legs"]![0]!["currency"] = "EUR";
        terms["legs"]![1]!["currency"] = "EUR";
        var payload = JsonSerializer.SerializeToElement(terms);
        SecurityAssetPackAdmission.EnsureSubmittedFields("Swap", payload);
        var projection = new SecurityProjectionRecord(security.SecurityId, "Swap", SecurityStatusDto.Active,
            security.DisplayName, "USD", "InternalCode", "SYNTHETIC", security.CommonTerms, payload,
            JsonSerializer.SerializeToElement(new { }), 1, security.EffectiveFrom, null, [], []);
        var write = () => SecurityAssetPackAdmission.EnsureAdmitted(projection, security.EffectiveFrom);
        write.Should().Throw<SecurityMasterWriteRefusedException>().WithMessage("*Cross-currency*");
    }

    [Fact]
    public void NormalizedProfile_CannotAdmitStepRatesOrFormulaAmortization()
    {
        var contract = SecurityVanillaSwapProfile.FromSecurity(Security());
        var request = new ContractualCashFlowRequest(Issue, KnownAt) { CalculationProfile = SecurityVanillaSwapProfile.Id };
        var withSteps = contract with { Streams = [contract.Streams[0] with { RateSteps = [new(Issue, .08m)] }, contract.Streams[1]] };
        var steps = () => ContractualCashFlowEngine.Validate(withSteps, request);
        steps.Should().Throw<InvalidOperationException>();
        var withAmortization = contract with { Streams = [contract.Streams[0] with { Amortization = CashFlowAmortization.EqualPrincipal }, contract.Streams[1]] };
        var amortization = () => ContractualCashFlowEngine.Validate(withAmortization, request);
        amortization.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExistingRequestWireShape_OmitsAnUnselectedProfile()
    {
        var request = new ContractualCashFlowRequest(Issue, KnownAt);
        var original = JsonSerializer.SerializeToNode(request, ContractualCashFlowJsonContext.Default.ContractualCashFlowRequest)!;
        original.AsObject().ContainsKey("calculationProfile").Should().BeFalse();
        var selected = JsonSerializer.SerializeToNode(request with { CalculationProfile = SecurityVanillaSwapProfile.Id },
            ContractualCashFlowJsonContext.Default.ContractualCashFlowRequest)!;
        selected["calculationProfile"]!.GetValue<string>().Should().Be(SecurityVanillaSwapProfile.Id);
        selected.AsObject().Remove("calculationProfile");
        JsonNode.DeepEquals(original, selected).Should().BeTrue("the optional profile adds no other request fields");
    }

    [Theory]
    [InlineData("{\"fundingFee\":100}")]
    [InlineData("{\"embeddedOption\":{\"strike\":100}}")]
    public void SupplementalCommonEconomics_AreOutsideTheBoundedSwapProfile(string common)
    {
        var security = Security() with { CommonTerms = JsonSerializer.Deserialize<JsonElement>(common) };
        var project = () => SecurityVanillaSwapProfile.FromSecurity(security);
        project.Should().Throw<InvalidOperationException>().WithMessage("*Supplemental common-term economics*");
    }

    private static SecurityDetailDto Security(int? leg = null, string? key = null, string? value = null)
    {
        var terms = JsonNode.Parse("""
            {"termsFormat":"flat","assetClassTermsVersion":2,"effectiveDate":"2026-01-01","maturityDate":"2026-04-01","legs":[
             {"legId":"fixed-pay","legType":"Fixed","currency":"USD","direction":"Pay","notional":100000,
              "fixedRate":3.65,"paymentFrequency":"Quarterly","dayCount":"ACT/365","exchangesPrincipal":false,
              "scheduleConventions":{"couponRateUnit":"Percent","couponCashBasis":"DayCount","accrualAdjusted":false}},
             {"legId":"floating-receive","legType":"Floating","currency":"USD","direction":"Receive","notional":100000,
              "index":"SOFR","paymentFrequency":"Monthly","dayCount":"ACT/360","exchangesPrincipal":false,
              "resetSchedule":[{"resetDate":"2026-01-01","fixingRate":0.05}],
              "scheduleConventions":{"couponRateUnit":"Fraction","couponCashBasis":"DayCount","businessDayConvention":"ModifiedFollowing",
               "paymentCalendars":["WEEKENDS"],"accrualAdjusted":false}}]}
            """)!.AsObject();
        if (leg is { } index)
            terms["legs"]![index]![key!] = JsonNode.Parse(value!);
        return new(Guid.NewGuid(), "Swap", SecurityStatusDto.Active, "Synthetic IRS", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 1,
            KnownAt.AddDays(-1), null);
    }
}
