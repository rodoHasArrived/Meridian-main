using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Curves;
using Meridian.Application.SecurityMaster.Valuation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Stored curves in PostgreSQL: definitions are write-once, versions append and are read
/// bitemporally, identical content is one row, the table refuses mutation, every write lands on the
/// <c>Curves</c> governance stream, and the pinned swap values to -162.50 through the real stores.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityCurvesPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private static readonly DateOnly June30 = new(2026, 6, 30);
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityCurvesPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task TypedRateEvidence_RoundTripsImmutably_AndVintageChangesAppendAVersion()
    {
        var store = new PostgresSecurityCurveStore(_fixture.Options);
        var definition = await store.DefineAsync(new DefineSecurityCurveRequest(UniqueKey("USD-SOFR-RATES"),
            SecurityCurveKind.Forward, "USD", "SOFR", null, "ACT/360", SecurityCurveInterpolation.Linear,
            "fred", "Typed rate source evidence", "operator"));
        var observation = new SecurityRateObservationDto("fred", "SOFR", 4.125m, "4.125", SecurityRateUnit.Percent,
            "Percent", June30, null, June30, new DateOnly(9999, 12, 31), DateTimeOffset.UtcNow,
            "lin (untransformed provider level)", SecurityRateObservationKind.ObservedFixing, "USD");
        var request = new IngestSecurityRatesRequest(definition.CurveId, June30,
            SecurityRateConstructionMethod.ObservedFixingFlatForecast, "Reviewed flat forecast assumption",
            [new SecurityRateIngestionPointDto(June30.AddYears(1), observation)]);
        var v1 = await store.PublishVersionAsync(SecurityRateIngestionRules.BuildPublication(request, definition, "operator"));
        var reloaded = await store.GetVersionByIdAsync(definition.CurveId, v1.CurveVersionId);
        reloaded!.Points[0].RateEvidence!.Observation.Should().BeEquivalentTo(observation);
        reloaded.Points[0].Value.Should().Be(0.04125m);
        var same = await store.PublishVersionAsync(SecurityRateIngestionRules.BuildPublication(request, definition, "operator"));
        same.CurveVersionId.Should().Be(v1.CurveVersionId);
        var revised = request with { Points = [new SecurityRateIngestionPointDto(June30.AddYears(1), observation with { VintageDate = June30.AddDays(1) })] };
        var v2 = await store.PublishVersionAsync(SecurityRateIngestionRules.BuildPublication(revised, definition, "operator"));
        v2.CurveVersionId.Should().NotBe(v1.CurveVersionId);
        v2.Points[0].Value.Should().Be(v1.Points[0].Value, "the source vintage is independently significant even when values agree");
        (await store.GetVersionByIdAsync(definition.CurveId, v1.CurveVersionId))!.Points[0].RateEvidence!.Observation.VintageDate.Should().Be(June30);
    }

    [SecurityMasterDatabaseFact]
    public async Task TypedRateRetry_WithNewRetrievalTime_ReturnsOriginalFullVersionAndEvidence()
    {
        var store = new PostgresSecurityCurveStore(_fixture.Options);
        var governance = new PostgresSecurityGovernanceEventLog(_fixture.Options);
        var definition = await DefineTypedRateCurveAsync(store);
        var request = TypedRatePublication(definition, 4.125m, "4.125");
        var first = await store.PublishVersionAsync(request);
        var retryRequest = WithRetrievalTime(request, request.Points[0].RateEvidence!.Observation.RetrievedAt.AddHours(1));
        SecurityCurveRules.ContentSha256(retryRequest.AsOf, retryRequest.Points).Should().NotBe(first.ContentSha256,
            "hashes still bind full retrieval evidence; semantic retry recognition does not weaken canonical hashes");
        var retry = await store.PublishVersionAsync(retryRequest);
        retry.Should().BeEquivalentTo(first, "the retry returns original timestamps, evidence, identity and hash unchanged");
        (await store.ListVersionsAsync(definition.CurveId)).Should().ContainSingle();
        (await CurveEventsAsync(governance, definition.CurveId)).Should().HaveCount(2,
            "only the definition and original publication produce governance events");
    }

    [SecurityMasterDatabaseFact]
    public async Task TypedRateRetry_AfterDifferentPublication_ReturnsOriginalWithoutRestoringItAsLatest()
    {
        var store = new PostgresSecurityCurveStore(_fixture.Options);
        var definition = await DefineTypedRateCurveAsync(store);
        var requestA = TypedRatePublication(definition, 4.125m, "4.125");
        var first = await store.PublishVersionAsync(requestA);
        var second = await store.PublishVersionAsync(TypedRatePublication(definition, 4.25m, "4.25"));
        var retry = await store.PublishVersionAsync(WithRetrievalTime(requestA,
            requestA.Points[0].RateEvidence!.Observation.RetrievedAt.AddHours(2)));
        retry.Should().BeEquivalentTo(first);
        (await store.GetLatestVersionAsOfAsync(definition.CurveId, June30))!.CurveVersionId.Should().Be(second.CurveVersionId);
        (await store.ListVersionsAsync(definition.CurveId)).Select(static version => version.CurveVersionId)
            .Should().Equal(first.CurveVersionId, second.CurveVersionId);
    }

    [SecurityMasterDatabaseFact]
    public async Task TypedRateConcurrentRetries_WithDifferentRetrievalTimes_AppendOnlyOneVersion()
    {
        var store = new PostgresSecurityCurveStore(_fixture.Options);
        var governance = new PostgresSecurityGovernanceEventLog(_fixture.Options);
        var definition = await DefineTypedRateCurveAsync(store);
        var request = TypedRatePublication(definition, 4.125m, "4.125");
        var retrievedAt = request.Points[0].RateEvidence!.Observation.RetrievedAt;
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => store.PublishVersionAsync(WithRetrievalTime(request, retrievedAt.AddSeconds(index)))));
        results.Select(static version => version.CurveVersionId).Distinct().Should().ContainSingle();
        foreach (var result in results)
            result.Should().BeEquivalentTo(results[0], "every caller receives the same original full evidence");
        (await store.ListVersionsAsync(definition.CurveId)).Should().ContainSingle();
        (await CurveEventsAsync(governance, definition.CurveId)).Should().HaveCount(2);
    }

    private static Task<SecurityCurveDefinitionDto> DefineTypedRateCurveAsync(PostgresSecurityCurveStore store)
        => store.DefineAsync(new DefineSecurityCurveRequest(UniqueKey("USD-SOFR-RETRY"), SecurityCurveKind.Forward,
            "USD", "SOFR", null, "ACT/360", SecurityCurveInterpolation.Linear, "fred", "Atomic typed-rate retry", "operator"));

    private static PublishSecurityCurveVersionRequest TypedRatePublication(SecurityCurveDefinitionDto definition, decimal value, string originalValue)
    {
        var observation = new SecurityRateObservationDto("fred", "SOFR", value, originalValue, SecurityRateUnit.Percent,
            "Percent", June30, null, June30, new DateOnly(9999, 12, 31),
            new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), "lin (untransformed provider level)",
            SecurityRateObservationKind.ObservedFixing, "USD");
        return SecurityRateIngestionRules.BuildPublication(new IngestSecurityRatesRequest(definition.CurveId, June30,
            SecurityRateConstructionMethod.ObservedFixingFlatForecast, "Reviewed flat forecast assumption",
            [new SecurityRateIngestionPointDto(June30.AddYears(1), observation)]), definition, "operator");
    }

    private static PublishSecurityCurveVersionRequest WithRetrievalTime(PublishSecurityCurveVersionRequest request, DateTimeOffset retrievedAt)
        => request with
        {
            Points = request.Points.Select(point => point with
            {
                RateEvidence = point.RateEvidence! with
                {
                    Observation = point.RateEvidence.Observation with { RetrievedAt = retrievedAt }
                }
            }).ToArray()
        };

    [SecurityMasterDatabaseFact]
    public async Task Versions_AreAppended_ReadBitemporally_AndIdenticalContentIsRetainedNotRepeated()
    {
        var store = new PostgresSecurityCurveStore(_fixture.Options);
        var governance = new PostgresSecurityGovernanceEventLog(_fixture.Options);
        var key = UniqueKey("USD-DISCOUNT");

        var definition = await store.DefineAsync(new DefineSecurityCurveRequest(key, SecurityCurveKind.Discount, "usd", null, null, "ACT/360", SecurityCurveInterpolation.Linear, "test-vendor", "bitemporal test", "casey"));
        definition.Currency.Should().Be("USD");
        definition.DayCount.Should().Be("Actual360");
        (await store.GetDefinitionAsync(definition.CurveId)).Should().BeEquivalentTo(definition);
        (await store.ListDefinitionsAsync(SecurityCurveKind.Discount, "USD")).Should().Contain(d => d.CurveId == definition.CurveId);

        var v1 = await store.PublishVersionAsync(new PublishSecurityCurveVersionRequest(definition.CurveId, June30,
            [new(June30, 1m), new(June30.AddYears(1), 0.97m)], "test-vendor", new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero), "casey"));
        await Task.Delay(20);
        var v2 = await store.PublishVersionAsync(new PublishSecurityCurveVersionRequest(definition.CurveId, June30,
            [new(June30, 1m), new(June30.AddYears(1), 0.96m)], "test-vendor", null, "jordan"));

        v1.Version.Should().Be(1);
        v2.Version.Should().Be(2);
        v2.CurveKey.Should().Be(key);
        v2.PublishedBy.Should().Be("jordan");
        v2.ContentSha256.Should().NotBe(v1.ContentSha256);
        v2.RecordedAt.Should().BeAfter(v1.RecordedAt);
        v1.SourcedAt.Should().Be(new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero));
        (await store.ListVersionsAsync(definition.CurveId)).Select(static v => v.Version).Should().Equal(1, 2);
        (await store.GetVersionAsync(definition.CurveId, 1)).Should().BeEquivalentTo(v1);
        (await store.GetVersionByIdAsync(definition.CurveId, v1.CurveVersionId)).Should().BeEquivalentTo(v1);
        (await store.GetVersionByIdAsync(Guid.NewGuid(), v1.CurveVersionId)).Should().BeNull("a version id resolves only under its own curve");
        (await store.GetVersionByIdAsync(definition.CurveId, Guid.NewGuid())).Should().BeNull();

        (await store.GetLatestVersionAsOfAsync(definition.CurveId, June30))!.Version.Should().Be(2);
        (await store.GetLatestVersionAsOfAsync(definition.CurveId, June30.AddMonths(3)))!.Version.Should().Be(2);
        (await store.GetLatestVersionAsOfAsync(definition.CurveId, June30, knownAt: v1.RecordedAt))!.Version.Should().Be(1, "v2 was not yet recorded");
        (await store.GetLatestVersionAsOfAsync(definition.CurveId, June30.AddDays(-1))).Should().BeNull();

        var eventsBefore = await CurveEventsAsync(governance, definition.CurveId);
        var republished = await store.PublishVersionAsync(new PublishSecurityCurveVersionRequest(definition.CurveId, June30,
            [new(June30.AddYears(1), 0.96m), new(June30, 1m)], "another-source", null, "morgan"));
        republished.Should().BeEquivalentTo(v2, "the same content for the same as-of is the retained version, whoever republishes it");
        (await store.ListVersionsAsync(definition.CurveId)).Should().HaveCount(2);
        (await CurveEventsAsync(governance, definition.CurveId)).Should().HaveCount(eventsBefore.Length, "an unchanged republish records nothing");

        var events = await CurveEventsAsync(governance, definition.CurveId);
        events.Select(static e => e.EventType).Should().Equal(PostgresSecurityCurveStore.DefinedEvent, PostgresSecurityCurveStore.VersionPublishedEvent, PostgresSecurityCurveStore.VersionPublishedEvent);
        events.Select(static e => e.Actor).Should().Equal("casey", "casey", "jordan");
        events.Should().OnlyContain(static e => e.SecurityId == null && e.Stream == SecurityGovernanceStreams.Curves);
        events[0].Payload.GetProperty("curveKey").GetString().Should().Be(key);
        events[0].Payload.GetProperty("dayCount").GetString().Should().Be("Actual360");
        events[2].Payload.GetProperty("version").GetInt64().Should().Be(2);
        events[2].Payload.GetProperty("contentSha256").GetString().Should().Be(v2.ContentSha256);
        events[2].Payload.GetProperty("pointCount").GetInt32().Should().Be(2);
    }

    [SecurityMasterDatabaseFact]
    public async Task Define_IsWriteOnce_ReturningTheStoredIdentityForIdenticalFacts_AndRefusingDifferentOnes()
    {
        var store = new PostgresSecurityCurveStore(_fixture.Options);
        var key = UniqueKey("EUR-ESTR");

        var first = await store.DefineAsync(new DefineSecurityCurveRequest(key, SecurityCurveKind.Forward, "EUR", "ESTR", "1D", "ACT/360", SecurityCurveInterpolation.Linear, "ecb", null, "casey"));
        var same = await store.DefineAsync(new DefineSecurityCurveRequest(key.ToLowerInvariant(), SecurityCurveKind.Forward, "eur", " estr ", "1d", "Actual360", SecurityCurveInterpolation.Linear, "ecb", "another description", "jordan"));

        same.Should().BeEquivalentTo(first, "the key is one identity whatever its case, and the stored facts come back");
        (await store.ListDefinitionsAsync(SecurityCurveKind.Forward, "eur", "estr")).Should().ContainSingle(d => d.CurveId == first.CurveId, "currency and index match ignoring case");

        var differentFacts = () => store.DefineAsync(new DefineSecurityCurveRequest(key, SecurityCurveKind.Forward, "EUR", "ESTR", "1D", "30/360", SecurityCurveInterpolation.Linear, "ecb", null, "casey"));
        await differentFacts.Should().ThrowAsync<InvalidOperationException>().WithMessage("*write-once*");

        // A 30E/360 curve re-defined with the day-count name the store returned is the same identity.
        var eurobondKey = UniqueKey("EUR-EURIBOR");
        var eurobond = await store.DefineAsync(new DefineSecurityCurveRequest(eurobondKey, SecurityCurveKind.Forward, "EUR", "EURIBOR", "6M", "30E/360", SecurityCurveInterpolation.Linear, "emmi", null, "casey"));
        eurobond.DayCount.Should().Be("ThirtyE360");
        (await store.DefineAsync(new DefineSecurityCurveRequest(eurobondKey, SecurityCurveKind.Forward, "EUR", "EURIBOR", "6M", eurobond.DayCount, SecurityCurveInterpolation.Linear, "emmi", null, "jordan")))
            .Should().BeEquivalentTo(eurobond, "the stored day-count name reads back as itself");

        var unknownCurve = () => store.PublishVersionAsync(new PublishSecurityCurveVersionRequest(Guid.NewGuid(), June30, [new(June30, 0.04m)], "ecb", null, "casey"));
        await unknownCurve.Should().ThrowAsync<ArgumentException>().WithMessage("*not defined*");
    }

    [SecurityMasterDatabaseFact]
    public async Task CurvesAndVersions_RefuseUpdateDeleteAndTruncate()
    {
        var store = new PostgresSecurityCurveStore(_fixture.Options);
        var definition = await store.DefineAsync(new DefineSecurityCurveRequest(UniqueKey("GBP-SONIA"), SecurityCurveKind.Forward, "GBP", "SONIA", null, "ACT/365", SecurityCurveInterpolation.Linear, "boe", null, "casey"));
        var version = await store.PublishVersionAsync(new PublishSecurityCurveVersionRequest(definition.CurveId, June30, [new(June30, 0.05m)], "boe", null, "casey"));
        // A curve without versions: its delete is refused by the guard, not by the versions' foreign key.
        var bare = await store.DefineAsync(new DefineSecurityCurveRequest(UniqueKey("GBP-BARE"), SecurityCurveKind.Discount, "GBP", null, null, "ACT/365", SecurityCurveInterpolation.Linear, "boe", null, "casey"));

        var versions = $"{_fixture.Options.Schema}.security_curve_versions";
        var curves = $"{_fixture.Options.Schema}.security_curves";
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var statement in new[]
                 {
                     $"update {versions} set published_by = 'tamper' where curve_version_id = '{version.CurveVersionId:D}';",
                     $"delete from {versions} where curve_version_id = '{version.CurveVersionId:D}';",
                     $"truncate {versions};",
                     $"update {curves} set currency = 'EUR' where curve_id = '{definition.CurveId:D}';",
                     $"update {curves} set description = 'tamper' where curve_id = '{bare.CurveId:D}';",
                     $"delete from {curves} where curve_id = '{bare.CurveId:D}';",
                     $"truncate {curves} cascade;"
                 })
        {
            await using var command = new NpgsqlCommand(statement, connection);
            var act = () => command.ExecuteNonQueryAsync();
            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514", statement);
        }

        (await store.GetVersionAsync(definition.CurveId, 1)).Should().BeEquivalentTo(version);
        (await store.GetDefinitionAsync(definition.CurveId)).Should().BeEquivalentTo(definition);
        (await store.GetDefinitionAsync(bare.CurveId)).Should().BeEquivalentTo(bare);
    }

    [SecurityMasterDatabaseFact]
    public async Task ThePinnedSwap_ValuesToTheGoldenNumber_ThroughTheRealStoresAndProvider()
    {
        var (service, query) = BuildSecurityMaster();
        var curveStore = new PostgresSecurityCurveStore(_fixture.Options);
        var cashFlowStore = new PostgresSecurityMasterCashFlowStore(_fixture.Options);
        var indexName = "SOFR-E2E-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var asOf = ValuationTestSecurities.AsOf;

        var discount = await curveStore.DefineAsync(new DefineSecurityCurveRequest(UniqueKey("USD-OIS"), SecurityCurveKind.Discount, "USD", null, null, "ACT/360", SecurityCurveInterpolation.Linear, "test", null, "casey"));
        await curveStore.PublishVersionAsync(new PublishSecurityCurveVersionRequest(discount.CurveId, ValuationTestSecurities.AsOfDate,
            ValuationTestSecurities.SwapDiscountPoints.Select(static p => new SecurityCurvePointDto(p.Date, p.Value)).ToList(), "test", null, "casey"));
        var forward = await curveStore.DefineAsync(new DefineSecurityCurveRequest(UniqueKey("USD-SOFR-3M"), SecurityCurveKind.Forward, "USD", indexName, "3M", "ACT/360", SecurityCurveInterpolation.Linear, "test", null, "casey"));
        var forwardVersion = await curveStore.PublishVersionAsync(new PublishSecurityCurveVersionRequest(forward.CurveId, ValuationTestSecurities.AsOfDate.AddMonths(-1),
            [new(ValuationTestSecurities.AsOfDate, 0.04m), new(ValuationTestSecurities.AsOfDate.AddYears(1), 0.04m)], "test", null, "casey"));

        var swapId = Guid.NewGuid();
        await service.CreateAsync(new CreateSecurityRequest(
            swapId,
            "Swap",
            JsonSerializer.SerializeToElement(new { displayName = "Golden swap", currency = "USD", countryOfRisk = "US", issuerName = "Big Bank" }),
            JsonSerializer.SerializeToElement(ValuationTestSecurities.SwapTerms(ValuationTestSecurities.AsOfDate, indexName)),
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "SWAP-" + swapId.ToString("N")[..8].ToUpperInvariant(), true, asOf.AddYears(-1), null, null)],
            asOf.AddYears(-1),
            "test",
            "codex",
            null,
            "create for the valuation seam"));
        await cashFlowStore.UpsertSourceAsync(new SecurityCashFlowSourceDto(swapId, StructuredCashFlowSourceKind.CalculatedBullet, DateTimeOffset.UtcNow, false, null, null));

        var cashFlows = new SecurityMasterCashFlowService(
            cashFlowStore, Array.Empty<IStructuredCashFlowProvider>(), query, NullLogger<SecurityMasterCashFlowService>.Instance,
            forwardCurves: new StoredSecurityForwardCurveProvider(curveStore, NullLogger<StoredSecurityForwardCurveProvider>.Instance));
        var valuation = new SecurityValuationService(
            query,
            [new DiscountedCashFlowValuationModel(cashFlows, curveStore, NullLogger<DiscountedCashFlowValuationModel>.Instance)],
            NullLogger<SecurityValuationService>.Instance);

        // The discount curve is pinned because other suites in this schema may define USD discount curves too.
        var result = await valuation.ValueAsync(new SecurityValuationRequest(swapId, asOf, Quantity: 1_000_000m, Curves: [new SecurityValuationCurveInput("Discount", discount.CurveId)]));

        result.Should().NotBeNull();
        result!.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.Model.Should().Be(SecurityValuationModelKind.DiscountedCashFlow, "the catalog declares the swap's default model");
        result.PresentValue.Should().Be(-162.50m);
        result.Price.Should().Be(-0.0001625m);
        result.PriceUnit.Should().Be(SecurityPriceUnit.CurrencyPerUnit);
        result.MarketValue.Should().Be(-162.50m);
        result.Observability.Should().Be(SecurityValuationObservability.ObservableInputs);
        result.ObservedAsOf.Should().Be(new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero), "the forward version dated 2025-12-01 is the oldest market input");
        result.Inputs.Single(static i => i.Kind == "DiscountCurveVersion").Reference.Should().Be(discount.CurveKey);
        result.Inputs.Single(static i => i.Kind == "ForwardCurve").Reference
            .Should().Be($"forwardCurve:{indexName}@2025-12-01 ({forward.CurveKey} v1 {forwardVersion.ContentSha256[..12]})");
    }

    private static string UniqueKey(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<SecurityGovernanceEvent[]> CurveEventsAsync(PostgresSecurityGovernanceEventLog governance, Guid curveId)
    {
        var all = new List<SecurityGovernanceEvent>();
        long after = 0;
        while (true)
        {
            var page = await governance.GetForStreamAsync(SecurityGovernanceStreams.Curves, after, 500);
            all.AddRange(page);
            if (page.Count < 500)
                break;
            after = page[^1].GlobalSequence;
        }

        return all.Where(e => e.Payload.GetProperty("curveId").GetGuid() == curveId).ToArray();
    }

    private (SecurityMasterService Service, SecurityMasterQueryService Query) BuildSecurityMaster()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var service = new SecurityMasterService(eventStore, snapshotStore, store, rebuilder, _fixture.Options, NullLogger<SecurityMasterService>.Instance);
        return (service, new SecurityMasterQueryService(eventStore, store, rebuilder));
    }
}
