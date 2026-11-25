using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Review A2 (owner decisions 2026-11-25: per-source ranking, hold the whole amendment). A lower-ranked
/// source changing a value a higher-ranked source supplied is held: nothing is applied, a conflict is
/// open for the field, and the request is retained for governed replay.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterSourceAdmissionPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterSourceAdmissionPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task LowerRankedSource_ChangingAHigherRankedValue_IsHeldAndNothingIsApplied()
    {
        var (service, store, held, conflicts) = Compose(["Bloomberg", "Polygon"]);
        var securityId = await CreateEquityAsync(service, "Bloomberg", countryOfRisk: "US");

        var amend = () => service.AmendTermsAsync(CountryAmendment(securityId, 1, "Polygon", "GB"));

        var failure = await amend.Should().ThrowAsync<SecurityMasterAmendmentHeldException>();
        failure.Which.DominatedFieldPaths.Should().Contain("CommonTerms.countryOfRisk");

        var projection = await store.GetProjectionAsync(securityId);
        projection!.Version.Should().Be(1, "a held amendment applies nothing");
        SecurityTermReader.ReadString(projection.CommonTerms, "countryOfRisk").Should().Be("US");

        var heldRows = await held.ListAsync(securityId, SecurityMasterHeldAmendmentStatus.Held);
        heldRows.Should().ContainSingle();
        heldRows[0].SourceSystem.Should().Be("Polygon");
        SecurityMasterHeldAmendmentJson.Deserialize(heldRows[0].RequestJson)!.SecurityId.Should().Be(securityId);

        var open = await conflicts.GetOpenConflictsAsync(CancellationToken.None);
        open.Should().Contain(conflict => conflict.SecurityId == securityId
            && conflict.FieldPath == "CommonTerms.countryOfRisk"
            && conflict.ProviderA == "Bloomberg" && conflict.ProviderB == "Polygon");
    }

    [SecurityMasterDatabaseFact]
    public async Task HigherRankedSource_OverwritesALowerRankedValue_AsBefore()
    {
        var (service, store, held, _) = Compose(["Bloomberg", "Polygon"]);
        var securityId = await CreateEquityAsync(service, "Polygon", countryOfRisk: "US");

        await service.AmendTermsAsync(CountryAmendment(securityId, 1, "Bloomberg", "GB"));

        var projection = await store.GetProjectionAsync(securityId);
        SecurityTermReader.ReadString(projection!.CommonTerms, "countryOfRisk").Should().Be("GB");
        (await held.ListAsync(securityId, null)).Should().BeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task UnrankedSource_KeepsApplying()
    {
        var (service, store, _, _) = Compose(["Bloomberg"]);
        var securityId = await CreateEquityAsync(service, "Bloomberg", countryOfRisk: "US");

        await service.AmendTermsAsync(CountryAmendment(securityId, 1, "SomeNewFeed", "GB"));

        var projection = await store.GetProjectionAsync(securityId);
        SecurityTermReader.ReadString(projection!.CommonTerms, "countryOfRisk").Should().Be("GB",
            "an unranked source keeps applying (owner decision)");
    }

    private (SecurityMasterService Service, PostgresSecurityMasterStore Store, ISecurityMasterHeldAmendmentStore Held, PostgresSecurityMasterConflictService Conflicts)
        Compose(IReadOnlyList<string> precedence)
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var conflicts = new PostgresSecurityMasterConflictService(store, _fixture.Options, NullLogger<PostgresSecurityMasterConflictService>.Instance);
        var held = new PostgresSecurityMasterHeldAmendmentStore(_fixture.Options);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance,
            conflictService: conflicts,
            fieldProvenance: new PostgresSecurityFieldProvenanceStore(_fixture.Options),
            sourceAdmissionPolicy: new SourcePrecedenceAdmissionPolicy(new SecurityMasterWorkbenchOptions { SourcePrecedence = [.. precedence] }),
            heldAmendments: held);
        return (service, store, held, conflicts);
    }

    private static async Task<Guid> CreateEquityAsync(SecurityMasterService service, string source, string countryOfRisk)
    {
        var securityId = Guid.NewGuid();
        var ticker = $"AD{securityId.ToString("N")[..6].ToUpperInvariant()}";
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            CommonTerms(countryOfRisk),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, effectiveFrom)],
            effectiveFrom,
            source,
            "feed",
            null,
            "create"));
        return securityId;
    }

    private static AmendSecurityTermsRequest CountryAmendment(Guid securityId, long expectedVersion, string source, string countryOfRisk)
        => new(
            securityId,
            expectedVersion,
            CommonTerms(countryOfRisk),
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            source,
            "feed",
            null,
            "vendor refresh");

    private static JsonElement CommonTerms(string countryOfRisk)
        => JsonSerializer.SerializeToElement(new
        {
            displayName = "Admission Test Co",
            currency = "USD",
            countryOfRisk,
            issuerName = "Admission Test Co",
            exchange = "XNAS",
            lotSize = 1,
            tickSize = 0.01m
        });
}
