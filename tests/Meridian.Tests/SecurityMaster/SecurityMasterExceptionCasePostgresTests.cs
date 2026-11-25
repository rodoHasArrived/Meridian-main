using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Review D2 (owner decision 2026-11-25: dedicated global queue): conflicts and data-quality violations
/// become deduplicated cases with SLA due dates; re-running is idempotent and a resolved condition closes
/// its case.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterExceptionCasePostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterExceptionCasePostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public Task PostgresStore_SyncIsIdempotent_AndClosesResolvedCases()
        => AssertLifecycleAsync(new PostgresSecurityMasterExceptionCaseStore(_fixture.Options));

    [Fact]
    public Task InMemoryStore_SyncIsIdempotent_AndClosesResolvedCases()
        => AssertLifecycleAsync(new InMemorySecurityMasterExceptionCaseStore());

    private static async Task AssertLifecycleAsync(ISecurityMasterExceptionCaseStore store)
    {
        var securityId = Guid.NewGuid();
        var conflict = new SecurityMasterConflict(Guid.NewGuid(), securityId, SecurityMasterConflictKinds.EconomicTermMismatch,
            "EconomicTerms.couponRate", "Bloomberg", "4.25", "Polygon", "4.50", DateTimeOffset.UtcNow.AddDays(-10), "Open");
        var conflicts = Substitute.For<ISecurityMasterConflictService>();
        conflicts.GetOpenConflictsAsync(Arg.Any<CancellationToken>()).Returns([conflict]);
        var quality = Substitute.For<ISecurityMasterDataQualityService>();
        quality.GetLatestReportAsync(Arg.Any<CancellationToken>()).Returns(new SecurityMasterQualityReportDto(DateTimeOffset.UtcNow, 1, 1,
        [
            new DataQualityRuleViolationDto("MA001", "Missing currency", DataQualityRuleCategory.MinimumAttribute, securityId, "currency",
                "Currency is missing.", DataQualityRuleSeverity.HardBlock, DateTimeOffset.UtcNow)
        ]));
        var sync = new SecurityMasterExceptionCaseSync(store, conflicts, quality);

        var first = await sync.SyncAsync();
        var second = await sync.SyncAsync();

        first.Opened.Should().Be(2);
        second.Opened.Should().Be(0, "re-running is idempotent");
        second.Refreshed.Should().Be(2);
        var open = await store.ListAsync(SecurityMasterExceptionCaseStatus.Open);
        open.Where(c => c.SecurityId == securityId).Should().HaveCount(2);
        open.Single(c => c.CaseKey == $"conflict:{conflict.ConflictId:D}").IsBreached(DateTimeOffset.UtcNow)
            .Should().BeTrue("a field conflict detected ten days ago is past its three-day SLA");

        var mine = open.First(c => c.SecurityId == securityId);
        (await store.AssignAsync(mine.CaseId, "ops.analyst")).Should().BeTrue();

        conflicts.GetOpenConflictsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var third = await sync.SyncAsync();

        third.Closed.Should().BeGreaterThanOrEqualTo(1, "resolving the conflict closes its case");
        (await store.ListAsync(SecurityMasterExceptionCaseStatus.Open))
            .Should().NotContain(c => c.CaseKey == $"conflict:{conflict.ConflictId:D}");
    }
}
