using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Review C3 (owner decision 2026-11-25: observation stream): factors are recorded without a terms
/// revision, a restatement wins in factor history and opens a review case.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityFactorObservationPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityFactorObservationPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public Task Postgres_RestatementWins_AndOpensACase()
        => AssertRestatementAsync(new PostgresSecurityFactorObservationStore(_fixture.Options), new PostgresSecurityMasterExceptionCaseStore(_fixture.Options));

    [Fact]
    public Task InMemory_RestatementWins_AndOpensACase()
        => AssertRestatementAsync(new InMemorySecurityFactorObservationStore(), new InMemorySecurityMasterExceptionCaseStore());

    [Fact]
    public void FactorHistory_ObservationOutranksPaydownAndSchedule()
    {
        var march = new DateOnly(2026, 3, 1);
        var terms = StructuredCashFlowTerms.Empty with { FactorSchedule = [new StructuredFactorScheduleEntry(march, 0.95m)] };

        var history = SecurityFactorHistoryProjector.Build(Guid.NewGuid(), terms, null, [], DateTimeOffset.UtcNow, [(march, 0.93m)]);

        history.FactorAsOf(march).Should().Be(0.93m);
        history.Points.Single().Source.Should().Be(SecurityFactorSource.Observation);
        history.Conflicts.Should().ContainSingle("the declared schedule disagreed with the observation");
    }

    private static async Task AssertRestatementAsync(ISecurityFactorObservationStore store, ISecurityMasterExceptionCaseStore cases)
    {
        var securityId = Guid.NewGuid();
        var march = new DateOnly(2026, 3, 25);
        var service = new SecurityFactorObservationService(store, cases);

        var first = await service.RecordAsync(securityId, march, 0.95m, "trustee", "evidence://march", "ops");
        var restated = await service.RecordAsync(securityId, march, 0.93m, "trustee", "evidence://march-restated", "ops");

        first.IsRestatement.Should().BeFalse();
        restated.IsRestatement.Should().BeTrue();
        restated.PriorFactor.Should().Be(0.95m);
        (await store.ListEffectiveAsync(securityId)).Should().ContainSingle().Which.Factor.Should().Be(0.93m);
        (await store.ListAsync(securityId)).Should().HaveCount(2, "the earlier observation is retained");
        (await cases.ListAsync(SecurityMasterExceptionCaseStatus.Open))
            .Should().ContainSingle(c => c.SecurityId == securityId && c.Category == SecurityFactorObservationService.FactorRestatementCategory);
    }
}
