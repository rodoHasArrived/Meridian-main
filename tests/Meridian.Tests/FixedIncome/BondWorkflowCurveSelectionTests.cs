using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondWorkflowCurveSelectionTests
{
    private static readonly Guid Security = Guid.Parse("cf85dc0c-5502-4109-ab88-f9ca3ead71a0");
    private static readonly DateTimeOffset KnownAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static BondWorkflowRequest Request => new(Security, new(KnownAt, new(2026, 1, 1), KnownAt), 1000m, 100m);

    [Fact]
    public async Task AutomaticSelectionIgnoresDefinitionsCreatedAfterTheKnowledgeCutoff()
    {
        var fixture = new Fixture();
        var future = fixture.AddCurve("USD-FUTURE", SecurityCurveKind.Discount, null, KnownAt.AddTicks(1));

        var result = (await fixture.Service.CalculateAsync(Request))!;

        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().NotBeNull();
        result.Inputs.Should().ContainSingle(input => input.Kind == "Curve:Discount" && input.VersionId == fixture.Discount.Version.CurveVersionId);
        result.Context.Curves.Should().ContainSingle().Which.Should().Be(new BondPinnedCurveInput("Discount",
            fixture.Discount.Definition.CurveId, fixture.Discount.Version.CurveVersionId));
        await fixture.Store.DidNotReceive().GetLatestVersionAsOfAsync(future.Definition.CurveId, Arg.Any<DateOnly>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewDefinitionCannotSupplyAnOldDatedVersionBeforeItsCreation()
    {
        var fixture = new Fixture();
        fixture.Catalog.Clear();
        var future = fixture.AddCurve("USD-FUTURE-ONLY", SecurityCurveKind.Discount, null, KnownAt.AddDays(1));

        var result = (await fixture.Service.CalculateAsync(Request))!;

        result.IsBlocked.Should().BeFalse("cash projection and quote yield remain independently available");
        result.PresentValue.Should().BeNull();
        result.Dv01.Should().BeNull();
        result.Inputs.Should().NotContain(input => input.Kind == "Curve:Discount");
        result.Request!.Context.Curves.Should().BeNull();
        await fixture.Store.DidNotReceive().GetLatestVersionAsOfAsync(future.Definition.CurveId, Arg.Any<DateOnly>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExplicitPinCannotAdmitAFutureCreatedDefinitionEvenWithAnOldVersion()
    {
        var fixture = new Fixture();
        var future = fixture.AddCurve("USD-FUTURE-PIN", SecurityCurveKind.Discount, null, KnownAt.AddTicks(1));
        var request = Request with
        {
            Context = Request.Context with
            { Curves = [new("Discount", future.Definition.CurveId, future.Version.CurveVersionId)] }
        };

        var result = (await fixture.Service.CalculateAsync(request))!;

        result.IsBlocked.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Reason.Contains("cutoff", StringComparison.Ordinal));
        result.Inputs.Should().NotContain(input => input.Kind == "Curve:Discount");
    }

    [Theory]
    [InlineData(BondWorkflowModelVersions.Legacy)]
    [InlineData(BondWorkflowModelVersions.Current)]
    public async Task ResolvedPinsKeepReplayIndependentOfLaterCatalogAmbiguity(string model)
    {
        var fixture = new Fixture();
        var retained = (await fixture.Service.CalculateAsync(Request with { ModelVersion = model }))!;
        retained.Request!.Context.Curves.Should().Equal(retained.Context.Curves!);
        fixture.AddCurve("USD-ANOTHER-ELIGIBLE", SecurityCurveKind.Discount, null, KnownAt.AddDays(-1));
        fixture.Store.ClearReceivedCalls();

        var replay = await fixture.Service.ReplayAsync(retained);

        JsonSerializer.Serialize(replay).Should().Be(JsonSerializer.Serialize(retained));
        await fixture.Store.DidNotReceive().ListDefinitionsAsync(Arg.Any<SecurityCurveKind?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await fixture.Store.DidNotReceive().GetLatestVersionAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalUnpinnedV1ReceiptRecoversItsExactManifestAndPreservesOriginalJson(bool omittedResolvedContext)
    {
        var fixture = new Fixture();
        var retained = await LegacyReceipt(fixture);
        if (omittedResolvedContext)
            retained = retained with
            {
                Request = retained.Request! with
                {
                    Context = retained.Request!.Context with
                    { KnownAt = null, SettlementDate = null, SecurityVersion = null }
                }
            };
        fixture.AddCurve("USD-LATER-ELIGIBLE", SecurityCurveKind.Discount, null, KnownAt.AddDays(-1));
        fixture.Store.ClearReceivedCalls();

        var replay = await fixture.Service.ReplayAsync(retained);

        retained.Request!.ModelVersion.Should().BeNull();
        retained.Request.Context.Curves.Should().BeNull();
        retained.Context.Curves.Should().BeNull();
        JsonSerializer.Serialize(replay).Should().Be(JsonSerializer.Serialize(retained));
        await fixture.Store.DidNotReceive().GetLatestVersionAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HistoricalUnusedCurveRoleRemainsInTheOriginalRequestWithoutBecomingACalculationDependency()
    {
        var fixture = new Fixture();
        var retained = await LegacyReceipt(fixture);
        var unused = new BondPinnedCurveInput("Forward:UNUSED", Guid.NewGuid(), Guid.NewGuid());
        var originalContext = retained.Context with { Curves = [unused] };
        retained = retained with { Context = originalContext, Request = retained.Request! with { Context = originalContext } };

        var replay = await fixture.Service.ReplayAsync(retained);

        JsonSerializer.Serialize(replay).Should().Be(JsonSerializer.Serialize(retained));
        replay!.Request!.Context.Curves.Should().Equal(unused);
        await fixture.Store.DidNotReceive().GetDefinitionAsync(unused.CurveId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("missing-version")]
    [InlineData("different-hash")]
    [InlineData("different-key")]
    [InlineData("duplicate-role")]
    [InlineData("conflicting-pin")]
    [InlineData("future-definition")]
    [InlineData("different-cutoff")]
    public async Task HistoricalReplayRefusesChangedOrAmbiguousCurveEvidenceWithoutLatestFallback(string defect)
    {
        var fixture = new Fixture();
        var retained = await LegacyReceipt(fixture);
        var dependency = retained.Inputs.Single(input => input.Kind == "Curve:Discount");
        var replacement = defect switch
        {
            "missing-version" => dependency with { VersionId = null },
            "different-hash" => dependency with { ContentSha256 = new string('b', 64) },
            "different-key" => dependency with { Key = "USD-NOT-THE-RETAINED-CURVE" },
            _ => dependency
        };
        retained = retained with { Inputs = retained.Inputs.Select(input => input == dependency ? replacement : input).ToArray() };
        if (defect == "duplicate-role")
            retained = retained with { Inputs = retained.Inputs.Append(dependency with { Key = "USD-DUPLICATE" }).ToArray() };
        if (defect == "conflicting-pin")
            retained = retained with
            {
                Request = retained.Request! with
                {
                    Context = retained.Request!.Context with
                    { Curves = [new("Discount", fixture.Discount.Definition.CurveId, Guid.NewGuid())] }
                }
            };
        if (defect == "future-definition")
            fixture.Catalog[0] = fixture.Discount.Definition with { CreatedAt = KnownAt.AddTicks(1) };
        if (defect == "different-cutoff")
            retained = retained with
            {
                Request = retained.Request! with
                { Context = retained.Request!.Context with { KnownAt = KnownAt.AddDays(1) } }
            };
        fixture.Store.ClearReceivedCalls();

        var replay = () => fixture.Service.ReplayAsync(retained);

        await replay.Should().ThrowAsync<ArgumentException>();
        await fixture.Store.DidNotReceive().GetLatestVersionAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FloatingProjectionRetainsBothForecastAndDiscountVersionsExactlyOnce()
    {
        var fixture = new Fixture(floating: true);
        var forward = fixture.AddCurve("USD-SOFR", SecurityCurveKind.Forward, "SOFR", KnownAt.AddDays(-1));
        var retained = (await fixture.Service.CalculateAsync(Request))!;
        retained.IsBlocked.Should().BeFalse(string.Join("; ", retained.Issues.Select(issue => issue.Reason)));
        retained.Request!.Context.Curves.Should().HaveCount(2);
        retained.Request.Context.Curves.Should().ContainSingle(pin => pin.Role == "Forward:SOFR"
            && pin.CurveId == forward.Definition.CurveId && pin.CurveVersionId == forward.Version.CurveVersionId);
        retained.Request.Context.Curves.Should().ContainSingle(pin => pin.Role == "Discount"
            && pin.CurveVersionId == fixture.Discount.Version.CurveVersionId);
        fixture.AddCurve("USD-SOFR-ANOTHER", SecurityCurveKind.Forward, "SOFR", KnownAt.AddDays(-1));

        var replay = await fixture.Service.ReplayAsync(retained);

        JsonSerializer.Serialize(replay).Should().Be(JsonSerializer.Serialize(retained));
    }

    private static async Task<BondWorkflowResult> LegacyReceipt(Fixture fixture)
    {
        var result = (await fixture.Service.CalculateAsync(Request with { ModelVersion = BondWorkflowModelVersions.Legacy }))!;
        result.IsBlocked.Should().BeFalse();
        var unpinnedContext = result.Context with { Curves = null };
        return result with { Context = unpinnedContext, Request = result.Request! with { ModelVersion = null, Context = unpinnedContext } };
    }

    private sealed class Fixture
    {
        public ISecurityCurveStore Store { get; } = Substitute.For<ISecurityCurveStore>();
        public List<SecurityCurveDefinitionDto> Catalog { get; } = [];
        private readonly Dictionary<Guid, SecurityCurveVersionDto> _versions = [];
        public (SecurityCurveDefinitionDto Definition, SecurityCurveVersionDto Version) Discount { get; }
        public BondWorkflowService Service { get; }

        public Fixture(bool floating = false)
        {
            Discount = AddCurve("USD-SYNTHETIC", SecurityCurveKind.Discount, null, KnownAt.AddDays(-1));
            Store.ListDefinitionsAsync(Arg.Any<SecurityCurveKind?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(info => (IReadOnlyList<SecurityCurveDefinitionDto>)Catalog.Where(definition =>
                    definition.Kind == info.ArgAt<SecurityCurveKind?>(0) && definition.Currency == info.ArgAt<string?>(1)
                    && definition.IndexName == info.ArgAt<string?>(2)).ToArray());
            Store.GetDefinitionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(info => Catalog.SingleOrDefault(definition => definition.CurveId == info.ArgAt<Guid>(0)));
            Store.GetVersionByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(info => _versions.Values.SingleOrDefault(version => version.CurveId == info.ArgAt<Guid>(0)
                    && version.CurveVersionId == info.ArgAt<Guid>(1)));
            Store.GetLatestVersionAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
                .Returns(info => _versions.Values.SingleOrDefault(version => version.CurveId == info.ArgAt<Guid>(0)));
            var bond = new SecurityDetailDto(Security, "Bond", SecurityStatusDto.Active, "Synthetic retained curve bond", "USD",
                JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
                {
                    assetClassTermsVersion = 2,
                    issueDate = "2026-01-01",
                    maturity = "2028-01-01",
                    couponType = floating ? "Floating" : "Fixed",
                    couponRate = 5m,
                    dayCount = "30/360",
                    par = 100m,
                    paymentFrequency = "Annual",
                    isCallable = false,
                    floatingIndex = floating ? "SOFR" : null,
                    spreadBps = 0m,
                    currentIndexRate = 4m,
                    currentIndexResetDate = "2026-01-01",
                    currentIndexRateSource = "synthetic-exact-observed-reset",
                    scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
                }), [], [], 9, KnownAt, null);
            var securities = Substitute.For<ISecurityMasterQueryService>();
            securities.GetRecordedByIdAsOfAsync(Security, KnownAt, Arg.Any<CancellationToken>()).Returns(bond);
            Service = new(securities, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), Store);
        }

        public (SecurityCurveDefinitionDto Definition, SecurityCurveVersionDto Version) AddCurve(
            string key, SecurityCurveKind kind, string? index, DateTimeOffset createdAt)
        {
            var definition = new SecurityCurveDefinitionDto(Guid.NewGuid(), key, kind, "USD", index, null,
                "ACT/365", SecurityCurveInterpolation.Linear, "synthetic", null, "synthetic-operator", createdAt);
            SecurityCurvePointDto[] points = kind == SecurityCurveKind.Discount
                ? [new(new(2026, 1, 1), 1m), new(new(2027, 1, 1), .95m), new(new(2028, 1, 1), .9m)]
                : [new(new(2026, 1, 1), .03m), new(new(2028, 1, 1), .03m)];
            var date = new DateOnly(2026, 1, 1);
            var version = new SecurityCurveVersionDto(Guid.NewGuid(), definition.CurveId, key, 1, date, KnownAt,
                KnownAt, "synthetic", "synthetic-operator", points, SecurityCurveRules.ContentSha256(date, points));
            Catalog.Add(definition);
            _versions.Add(version.CurveVersionId, version);
            return (definition, version);
        }
    }
}
