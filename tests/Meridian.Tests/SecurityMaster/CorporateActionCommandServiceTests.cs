using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class CorporateActionCommandServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AppendAsync_ValidAction_AppendsThroughEventStoreAndAuditsSource()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var action = CreateAction("Dividend", dividendPerShare: 0.24m);

        var result = await service.AppendAsync(SecurityId, action, "ops.user", "provider-backfill");

        result.Succeeded.Should().BeTrue();
        result.ValidationError.Should().BeNull();
        await store.Received(1).AppendCorporateActionAsync(action, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendAsync_InvalidAction_ReturnsValidationErrorWithoutAppending()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var action = CreateAction("StockSplit", splitRatio: 0m);

        var result = await service.AppendAsync(SecurityId, action, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Be("StockSplit SplitRatio must be greater than 0 and less than or equal to 1000.");
        await store.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_RouteSecurityMismatch_ReturnsValidationErrorWithoutAppending()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var action = CreateAction("Dividend", dividendPerShare: 0.24m) with
        {
            SecurityId = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };

        var result = await service.AppendAsync(SecurityId, action, "ops.user", "import");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Be("Corporate action SecurityId must match route parameter.");
        await store.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_UnknownEventType_ReturnsReasonListingAcceptedTypes()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var action = CreateAction("Divvy", dividendPerShare: 0.24m);

        var result = await service.AppendAsync(SecurityId, action, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().StartWith("Unsupported corporate action EventType 'Divvy'.");
        result.ValidationError.Should().Contain("StockSplit");
        await store.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_BondCallOnEquitySecurity_RejectedByAssetClassValidity()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        var projectionStore = Substitute.For<ISecurityMasterStore>();
        projectionStore.GetProjectionAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection("Equity"));
        var service = new CorporateActionCommandService(
            store, NullLogger<CorporateActionCommandService>.Instance, projectionStore);
        var action = CreateAction("BondCall") with { RedemptionPricePercentOfPar = 101.5m };

        var result = await service.AppendAsync(SecurityId, action, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Contain("not valid for asset class 'Equity'");
        await store.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_BondCallWithoutRedemptionPrice_RejectedByRequiredFields()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var action = CreateAction("BondCall");

        var result = await service.AppendAsync(SecurityId, action, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Be("BondCall corporate actions must include a positive RedemptionPricePercentOfPar.");
    }

    [Fact]
    public async Task AppendAsync_UnknownAssetClass_SkipsAssetClassCheckAndAppends()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        var projectionStore = Substitute.For<ISecurityMasterStore>();
        projectionStore.GetProjectionAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns((SecurityProjectionRecord?)null);
        var service = new CorporateActionCommandService(
            store, NullLogger<CorporateActionCommandService>.Instance, projectionStore);
        var action = CreateAction("BondCall") with { RedemptionPricePercentOfPar = 100m };

        var result = await service.AppendAsync(SecurityId, action, "ops.user", "http");

        result.Succeeded.Should().BeTrue();
        await store.ReceivedWithAnyArgs(1).AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_SupersedingAmendment_AppendsAndSurfacesRestatementDecision()
    {
        var original = CreateAction("Dividend", dividendPerShare: 0.24m);
        var store = Substitute.For<ISecurityMasterEventStore>();
        store.LoadCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { original });
        var decision = new SecurityMasterRestatementDecision(RestatementRequired: true, Candidates: []);
        var trigger = Substitute.For<ICorporateActionRestatementTrigger>();
        trigger.OnSupersededAsync(
                Arg.Any<CorporateActionDto>(), Arg.Any<CorporateActionDto>(),
                Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(decision);
        var service = new CorporateActionCommandService(
            store, NullLogger<CorporateActionCommandService>.Instance, restatementTrigger: trigger);
        var amendment = CreateAction("Dividend", dividendPerShare: 0.26m) with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = original.CorpActId
        };

        var result = await service.AppendAsync(SecurityId, amendment, "ops.user", "provider-correction");

        result.Succeeded.Should().BeTrue();
        result.RestatementDecision.Should().BeSameAs(decision);
        await store.Received(1).AppendCorporateActionAsync(
            Arg.Is<CorporateActionDto>(dto => dto.SupersedesCorpActId == original.CorpActId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendAsync_SupersedeUnknownTarget_Rejected()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        store.LoadCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionDto>());
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var amendment = CreateAction("Dividend", dividendPerShare: 0.26m) with
        {
            SupersedesCorpActId = Guid.NewGuid()
        };

        var result = await service.AppendAsync(SecurityId, amendment, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Contain("supersedes unknown corporate action");
        await store.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_SupersedeNonTip_Rejected()
    {
        var original = CreateAction("Dividend", dividendPerShare: 0.24m);
        var firstAmendment = CreateAction("Dividend", dividendPerShare: 0.25m) with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = original.CorpActId
        };
        var store = Substitute.For<ISecurityMasterEventStore>();
        store.LoadCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { original, firstAmendment });
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var secondAmendment = CreateAction("Dividend", dividendPerShare: 0.26m) with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = original.CorpActId
        };

        var result = await service.AppendAsync(SecurityId, secondAmendment, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Contain("already superseded");
    }

    [Fact]
    public async Task AppendAsync_SupersedeChangingEventType_Rejected()
    {
        var original = CreateAction("Dividend", dividendPerShare: 0.24m);
        var store = Substitute.For<ISecurityMasterEventStore>();
        store.LoadCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { original });
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var reclassification = CreateAction("StockSplit", splitRatio: 2m) with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = original.CorpActId
        };

        var result = await service.AppendAsync(SecurityId, reclassification, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Contain("must retain event type 'Dividend'");
    }

    [Fact]
    public async Task AppendAsync_CancelledWithoutSupersede_Rejected()
    {
        var store = Substitute.For<ISecurityMasterEventStore>();
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var action = CreateAction("Dividend", dividendPerShare: 0.24m) with
        {
            LifecycleState = CorporateActionLifecycleStates.Cancelled
        };

        var result = await service.AppendAsync(SecurityId, action, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Contain("must supersede the event they cancel");
    }

    [Fact]
    public async Task AppendAsync_LifecycleRegression_Rejected()
    {
        var original = CreateAction("Dividend", dividendPerShare: 0.24m);
        var store = Substitute.For<ISecurityMasterEventStore>();
        store.LoadCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { original });
        var service = new CorporateActionCommandService(store, NullLogger<CorporateActionCommandService>.Instance);
        var regression = CreateAction("Dividend", dividendPerShare: 0.26m) with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = original.CorpActId,
            LifecycleState = CorporateActionLifecycleStates.Announced
        };

        var result = await service.AppendAsync(SecurityId, regression, "ops.user", "http");

        result.Succeeded.Should().BeFalse();
        result.ValidationError.Should().Contain("may not move the lifecycle backwards");
    }

    private static SecurityProjectionRecord CreateProjection(string assetClass)
        => new(
            SecurityId: SecurityId,
            AssetClass: assetClass,
            Status: default,
            DisplayName: "Test Security",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "TEST",
            CommonTerms: default,
            AssetSpecificTerms: default,
            Provenance: default,
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddYears(-1),
            EffectiveTo: null,
            Identifiers: [],
            Aliases: []);

    private static CorporateActionDto CreateAction(
        string eventType,
        decimal? dividendPerShare = null,
        decimal? splitRatio = null)
        => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            SecurityId,
            eventType,
            new DateOnly(2026, 3, 15),
            new DateOnly(2026, 3, 31),
            dividendPerShare,
            "USD",
            splitRatio,
            null,
            null,
            null,
            null,
            null,
            null);
}
