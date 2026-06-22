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
