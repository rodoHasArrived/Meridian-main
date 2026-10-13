using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterClosePriceServiceTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly DateTimeOffset Close = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApprovalRetainsExactSourceObservationsOverrideAndIndependentRationale()
    {
        var (service, store) = BuildService();
        var manual = new SecurityClosePriceOverrideDto(100.1m, SecurityPriceUnit.PercentOfPar, "Validated close adjustment", "dealer-file/row-17");
        var draft = await service.CreateClosePriceSetAsync(Request(manual));
        draft.Snapshot.Blockers.Should().BeEmpty();
        draft.Snapshot.Lines.Single().Observations.Should().HaveCount(2);
        draft.Snapshot.Lines.Single().ClosePrice.Should().Be(100.1m);
        draft.Snapshot.Lines.Single().Selection!.GoldenCopyPrice.Should().Be(100m);
        draft.Snapshot.Lines.Single().Override.Should().Be(manual);
        RetainForRead(store, draft);

        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft, "maker"))).Should().ThrowAsync<InvalidOperationException>();
        var approved = await service.ApproveClosePriceSetAsync(Approve(draft));
        approved.Approval!.Rationale.Should().Be("Reviewed both source rows and the adjustment");
        approved.Approval.SnapshotSha256.Should().Be(SecurityClosePriceEvidence.Digest(draft.Snapshot));
        await store.Received(1).RetainClosePriceApprovalAsync("account-a", "book-a", DateOnly.FromDateTime(Close.UtcDateTime),
            Arg.Is<SecurityClosePriceApprovalDto>(a => a.Reviewer == "checker"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("stale", "StalePrice")]
    [InlineData("unsupported", "UnsupportedPrice")]
    [InlineData("dispersion", "SourceDispersion")]
    [InlineData("mixed", "IncomparableUnits")]
    [InlineData("override", "UnsupportedOverride")]
    public async Task ExceptionsRemainRetainedBlockersAndCannotBeApproved(string scenario, string blocker)
    {
        var (service, store) = BuildService(scenario);
        var manual = scenario == "override" ? new SecurityClosePriceOverrideDto(100m, SecurityPriceUnit.PercentOfPar, "", "") : null;
        var draft = await service.CreateClosePriceSetAsync(Request(manual));
        draft.Snapshot.Blockers.Select(b => b.Code).Should().Contain(blocker);
        draft.Snapshot.Lines.Single().Observations.Should().NotBeEmpty();
        RetainForRead(store, draft);
        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft))).Should().ThrowAsync<InvalidOperationException>();
        await store.DidNotReceive().RetainClosePriceApprovalAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(),
            Arg.Any<SecurityClosePriceApprovalDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrectionLinksExactScopeAndPreservesSecurityCoverage()
    {
        var (service, store) = BuildService();
        var original = await service.CreateClosePriceSetAsync(Request());
        RetainForRead(store, original);
        var corrected = await service.CreateClosePriceSetAsync(Request() with
        { CorrectsSnapshotId = original.Snapshot.SnapshotId, CorrectionRationale = "Corrected vendor observation" });
        corrected.Snapshot.Version.Should().Be(2);
        corrected.Snapshot.CorrectsSnapshotId.Should().Be(original.Snapshot.SnapshotId);
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with
        { AccountId = "account-b", CorrectsSnapshotId = original.Snapshot.SnapshotId, CorrectionRationale = "Wrong scope" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with
        { Securities = [new(Guid.NewGuid())], CorrectsSnapshotId = original.Snapshot.SnapshotId, CorrectionRationale = "Changed coverage" }))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("negative")]
    public async Task SupportedOverrideAndLargeDispersionLimitCannotHideUnsupportedSourcePrice(string scenario)
    {
        var (service, store) = BuildService(scenario);
        var manual = new SecurityClosePriceOverrideDto(100m, SecurityPriceUnit.PercentOfPar, "Override", "source-file/row-1");
        var draft = await service.CreateClosePriceSetAsync(Request(manual) with { MaxSourceDispersionPercent = 1000m });
        draft.Snapshot.Lines.Single().ClosePrice.Should().Be(100m);
        draft.Snapshot.Blockers.Select(b => b.Code).Should().Contain("UnsupportedPrice");
        RetainForRead(store, draft);
        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RetainedReadAndApprovalRetryDoNotConsultLaterQuotes()
    {
        var (service, store) = BuildService();
        var draft = await service.CreateClosePriceSetAsync(Request());
        RetainForRead(store, draft);
        var approved = await service.ApproveClosePriceSetAsync(Approve(draft));
        // Hydration through source-generated persistence exercises the restart representation.
        var payload = JsonSerializer.Serialize(approved.Snapshot, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto);
        var hydrated = JsonSerializer.Deserialize(payload, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto)!;
        RetainForRead(store, new(hydrated, approved.Approval));
        store.ClearReceivedCalls();
        var restarted = new SecurityMasterPricingService(store,
            Substitute.For<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(), NullLogger<SecurityMasterPricingService>.Instance);
        (await restarted.ApproveClosePriceSetAsync(Approve(draft))).Should().BeEquivalentTo(approved);
        await store.DidNotReceive().GetRawPricesAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
        await restarted.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft) with { Rationale = "Replace review" }))
            .Should().ThrowAsync<InvalidOperationException>();
        (await restarted.GetClosePriceSetAsync("account-b", "book-a", hydrated.ValuationDate, hydrated.SnapshotId)).Should().BeNull();
        (await restarted.GetClosePriceSetAsync("account-a", "book-b", hydrated.ValuationDate, hydrated.SnapshotId)).Should().BeNull();
        (await restarted.GetClosePriceSetAsync("account-a", "book-a", hydrated.ValuationDate.AddDays(1), hydrated.SnapshotId)).Should().BeNull();
    }

    [Fact]
    public async Task MissingScopeAndFutureValuationCutoffCannotCreateCertifiableSet()
    {
        var (service, _) = BuildService();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with { AccountId = "" })).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with { BookId = "" })).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with { EvaluatedAsOf = Close.AddDays(1) })).Should().ThrowAsync<ArgumentException>();
    }

    private static CreateSecurityClosePriceSetRequest Request(SecurityClosePriceOverrideDto? manual = null)
        => new("account-a", "book-a", DateOnly.FromDateTime(Close.UtcDateTime), Close, [new(SecurityId, manual)], 2m, "maker");
    private static ApproveSecurityClosePriceSetRequest Approve(SecurityClosePriceSetDto set, string reviewer = "checker")
        => new(set.Snapshot.AccountId, set.Snapshot.BookId, set.Snapshot.ValuationDate, set.Snapshot.SnapshotId, reviewer,
            "Reviewed both source rows and the adjustment");
    private static void RetainForRead(ISecurityMasterPricingStore store, SecurityClosePriceSetDto set)
        => store.GetClosePriceSetAsync(set.Snapshot.AccountId, set.Snapshot.BookId, set.Snapshot.ValuationDate,
            set.Snapshot.SnapshotId, Arg.Any<CancellationToken>()).Returns(set);

    private static (SecurityMasterPricingService, ISecurityMasterPricingStore) BuildService(string scenario = "fresh")
    {
        var store = Substitute.For<ISecurityMasterPricingStore>();
        var query = Substitute.For<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(
            SecurityId, "Bond", SecurityStatusDto.Active, "Close test", "USD", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }), [], [], 1, Close.AddDays(-10), null));
        store.GetHierarchyAsOfAsync(SecurityId, "account-a", Close, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new SecurityPricingHierarchyDto(SecurityId, "account-a", [new(1, "source", "Source", 1)], Close.AddDays(-10), "operator"));
        var unit = scenario == "unsupported" ? SecurityPriceUnit.Unspecified : SecurityPriceUnit.PercentOfPar;
        store.GetRawPricesAsync(SecurityId, Close, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>()).Returns(new SecurityRawPriceDto[]
        {
            new("source", scenario == "zero" ? 0m : scenario == "negative" ? -100m : 100m,
                scenario == "stale" ? Close.AddDays(-5) : Close.AddHours(-1), unit),
            new("comparison", scenario == "dispersion" ? 110m : 100.2m, Close.AddHours(-2),
                scenario == "mixed" ? SecurityPriceUnit.CurrencyPerUnit : unit)
        });
        return (new(store, query, NullLogger<SecurityMasterPricingService>.Instance), store);
    }
}
