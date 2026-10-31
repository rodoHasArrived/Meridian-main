using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Exercises the same committed corpus against persisted projection rows, including transaction
/// rollback when invalid replacement evidence reaches the projection writer during replay.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityEconomicSchedulePostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityEconomicSchedulePostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task AcceptedCorpus_PersistsTheSameTypedValuesAndRetainsSourceEvidence()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        foreach (var fixture in SecurityEconomicScheduleFixtureCorpus.All.Where(fixture => fixture.IsValid))
        {
            var record = fixture.Record();
            await store.UpsertProjectionAsync(record);

            var retained = await store.GetProjectionAsync(record.SecurityId);
            retained.Should().NotBeNull(fixture.Id);
            retained!.Version.Should().Be(record.Version, fixture.Id);
            JsonNode.DeepEquals(JsonNode.Parse(retained.AssetSpecificTerms.GetRawText()), JsonNode.Parse(record.AssetSpecificTerms.GetRawText()))
                .Should().BeTrue("PostgreSQL may normalize JSON formatting but must not rewrite economic evidence: {0}", fixture.Id);
            fixture.AssertTerms(StructuredCashFlowTermsResolver.Resolve(SecurityEconomicScheduleFixtureCorpus.Detail(retained)));
            await AssertStoredRowsAsync(fixture, record.SecurityId);
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task RejectedCorpus_RollsBackNewRowsAndCannotPartiallyReplaceAnExistingSchedule()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        foreach (var fixture in SecurityEconomicScheduleFixtureCorpus.All.Where(fixture => !fixture.IsValid))
        {
            var rejected = fixture.Record();
            Func<Task> insert = () => store.UpsertProjectionAsync(rejected);
            fixture.AssertDiagnostics((await insert.Should().ThrowAsync<SecurityScheduleValidationException>(fixture.Id)).Which.Diagnostics);
            (await store.GetProjectionAsync(rejected.SecurityId)).Should().BeNull("a rejected schedule cannot leave its parent record behind: {0}", fixture.Id);

            var baseline = SecurityEconomicScheduleFixtureCorpus.Get(fixture.AssetClass switch
            {
                "StructuredCredit" => "typed-factors-before-reference",
                "DirectLoan" => "principal-same-date-aggregation",
                _ => "steps-unsorted-including-zero"
            });
            var original = baseline.Record(rejected.SecurityId);
            await store.UpsertProjectionAsync(original);
            Func<Task> replace = () => store.UpsertProjectionAsync(rejected with { Version = 2 });
            fixture.AssertDiagnostics((await replace.Should().ThrowAsync<SecurityScheduleValidationException>(fixture.Id)).Which.Diagnostics);

            var retained = await store.GetProjectionAsync(rejected.SecurityId);
            retained.Should().NotBeNull(fixture.Id);
            retained!.Version.Should().Be(1, "the parent version and child rows must roll back together: {0}", fixture.Id);
            JsonNode.DeepEquals(JsonNode.Parse(retained.AssetSpecificTerms.GetRawText()), JsonNode.Parse(original.AssetSpecificTerms.GetRawText()))
                .Should().BeTrue("the prior accepted revision remains authoritative after a refused amendment: {0}", fixture.Id);
            await AssertStoredRowsAsync(baseline, rejected.SecurityId);
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task HistoricalBatchReplay_InvalidLaterRecordRollsBackEarlierRowsAndCheckpoint()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() }.Order().ToArray();
        var accepted = SecurityEconomicScheduleFixtureCorpus.Get("typed-factors-before-reference").Record(ids[0]);
        var rejectedFixture = SecurityEconomicScheduleFixtureCorpus.Get("factor-duplicate-forward");
        var rejected = rejectedFixture.Record(ids[1]);
        var projectionName = $"schedule-replay-{Guid.NewGuid():N}";
        await store.SaveCheckpointAsync(projectionName, 7);

        // The store processes IDs in order. The valid record is written first in this transaction;
        // the second record must roll back both its predecessor and the pending replay checkpoint.
        Func<Task> replay = () => store.PersistProjectionBatchAsync(projectionName, 9, [accepted, rejected]);
        rejectedFixture.AssertDiagnostics((await replay.Should().ThrowAsync<SecurityScheduleValidationException>()).Which.Diagnostics);

        (await store.GetProjectionAsync(accepted.SecurityId)).Should().BeNull();
        (await store.GetProjectionAsync(rejected.SecurityId)).Should().BeNull();
        (await store.GetCheckpointAsync(projectionName)).Should().Be(7);
        var factors = new PostgresStructuredCreditReferenceProjectionStore(_fixture.Options);
        (await factors.GetFactorScheduleAsync(accepted.SecurityId)).Should().BeEmpty();
    }

    private async Task AssertStoredRowsAsync(SecurityEconomicScheduleFixture fixture, Guid securityId)
    {
        if (fixture.AssetClass == "StructuredCredit")
        {
            var reader = new PostgresStructuredCreditReferenceProjectionStore(_fixture.Options);
            (await reader.GetStructuredCreditAsync(securityId)).Should().NotBeNull(fixture.Id);
            var rows = await reader.GetFactorScheduleAsync(securityId);
            rows.Select(row => row.AsOfDate).Should().Equal(fixture.Factors.Select(value => value.ParsedDate), fixture.Id);
            rows.Select(row => row.Factor).Should().Equal(fixture.Factors.Select(value => value.Value), fixture.Id);
            rows.Select(row => row.Ordinal).Should().Equal(Enumerable.Range(0, fixture.Factors.Count), fixture.Id);
            foreach (var expected in fixture.Factors)
            {
                var effective = await reader.GetFactorAsOfAsync(securityId, expected.ParsedDate);
                effective.Should().NotBeNull(fixture.Id);
                effective!.Factor.Should().Be(expected.Value, fixture.Id);
            }
        }
        else if (fixture.AssetClass == "DirectLoan")
        {
            var reader = new PostgresDirectLoanReferenceProjectionStore(_fixture.Options);
            (await reader.GetDirectLoanAsync(securityId)).Should().NotBeNull(fixture.Id);
            var rows = await reader.GetPrincipalScheduleAsync(securityId);
            rows.Select(row => row.PaymentDate).Should().Equal(fixture.Principal.Select(value => value.ParsedDate), fixture.Id);
            rows.Select(row => row.Amount).Should().Equal(fixture.Principal.Select(value => value.Value), fixture.Id);
            rows.Select(row => row.Ordinal).Should().Equal(Enumerable.Range(0, fixture.Principal.Count), fixture.Id);
        }
    }
}
