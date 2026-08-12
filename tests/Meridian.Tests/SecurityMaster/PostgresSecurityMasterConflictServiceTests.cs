using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The durable, Postgres-backed conflict store must detect the same golden-record conflicts as the
/// in-memory store and — the reason it exists — retain each resolution and its chosen winner across
/// process instances, so the audit guarantee survives restarts and horizontal scale-out.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgresSecurityMasterConflictServiceTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public PostgresSecurityMasterConflictServiceTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresSecurityMasterConflictService NewService(ISecurityMasterStore store)
        => new(store, _fixture.Options, NullLogger<PostgresSecurityMasterConflictService>.Instance);

    private static ISecurityMasterStore StoreReturning(params SecurityProjectionRecord[] projections)
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(projections);
        return store;
    }

    private static SecurityProjectionRecord MakeProjection(
        Guid securityId, string identifierKind, string identifierValue, string provider)
    {
        var identifier = new SecurityIdentifierDto(
            Enum.Parse<SecurityIdentifierKind>(identifierKind, ignoreCase: true),
            identifierValue,
            IsPrimary: true,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(-30),
            Provider: provider);

        return new SecurityProjectionRecord(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: $"Test Security {securityId:N}",
            Currency: "USD",
            PrimaryIdentifierKind: identifierKind,
            PrimaryIdentifierValue: identifierValue,
            CommonTerms: JsonSerializer.SerializeToElement(new { currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = provider }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers: new[] { identifier },
            Aliases: Array.Empty<SecurityAliasDto>());
    }

    [SecurityMasterDatabaseFact]
    public async Task GetOpenConflictsAsync_DetectsAndPersistsConflict()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();
        var store = StoreReturning(
            MakeProjection(securityA, "Isin", "US0378331005", "alpaca"),
            MakeProjection(securityB, "Isin", "US0378331005", "polygon"));

        var conflicts = await NewService(store).GetOpenConflictsAsync(CancellationToken.None);

        var conflict = conflicts.Should().ContainSingle(c => c.SecurityId == securityA).Subject;
        conflict.ConflictKind.Should().Be("IdentifierAmbiguity");
        conflict.FieldPath.Should().Contain("Isin");
        conflict.Status.Should().Be("Open");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_PersistsWinnerAndResolverAcrossInstances()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();
        var store = StoreReturning(
            MakeProjection(securityA, "Cusip", "037833100", "provA"),
            MakeProjection(securityB, "Cusip", "037833100", "provB"));

        // Instance A detects and resolves.
        var serviceA = NewService(store);
        var open = await serviceA.GetOpenConflictsAsync(CancellationToken.None);
        var conflictId = open.Single(c => c.SecurityId == securityA).ConflictId;

        var resolved = await serviceA.ResolveAsync(
            new ResolveConflictRequest(conflictId, "Resolve", "operator@meridian.test", "Edgar is golden.", ChosenWinnerSource: "Edgar"),
            CancellationToken.None);
        resolved.Should().NotBeNull();
        resolved!.Status.Should().Be("Resolved");

        // A fresh instance reading the same database observes the durable resolution and its winner.
        var serviceB = NewService(store);
        var reloaded = await serviceB.GetConflictAsync(conflictId, CancellationToken.None);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be("Resolved");
        reloaded.ResolvedWinnerSource.Should().Be("Edgar");
        reloaded.ResolvedBy.Should().Be("operator@meridian.test");
        reloaded.ResolvedReason.Should().Be("Edgar is golden.");
        reloaded.ResolvedAt.Should().NotBeNull();

        // The resolved conflict is excluded from the open list and cannot be re-resolved.
        var openAfter = await serviceB.GetOpenConflictsAsync(CancellationToken.None);
        openAfter.Should().NotContain(c => c.ConflictId == conflictId);

        var second = await serviceB.ResolveAsync(
            new ResolveConflictRequest(conflictId, "AcceptA", "operator.b@meridian.test"),
            CancellationToken.None);
        second.Should().BeNull("a conflict that is no longer Open cannot be re-resolved");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_FieldConflict_WritesWinnerFieldProvenanceWithTheClose()
    {
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US88160R1014", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US88160R1014", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };

        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);

        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflict = open.Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);
        conflict.FieldPath.Should().Be("CommonTerms.currency");

        var resolved = await service.ResolveAsync(
            new ResolveConflictRequest(conflict.ConflictId, "AcceptB", "operator@meridian.test", "provB confirmed."),
            CancellationToken.None);
        resolved.Should().NotBeNull();
        resolved!.ResolvedWinnerSource.Should().Be("provB");

        // The winning attribution is durable field lineage, committed with the conflict close.
        var provenanceStore = new PostgresSecurityFieldProvenanceStore(_fixture.Options);
        var lineage = await provenanceStore.GetAsync(securityId, CancellationToken.None);
        var row = lineage.Should().ContainSingle().Subject;
        row.FieldPath.Should().Be("CommonTerms.currency");
        row.SourceSystem.Should().Be("provB");
        row.Origin.Should().Be(SecurityFieldProvenanceOrigins.ConflictResolution);
        row.OriginReference.Should().Be(conflict.ConflictId.ToString("D"));
        row.UpdatedBy.Should().Be("operator@meridian.test");
        row.AsOf.Should().NotBeNull("the attribution's as-of is the resolution time");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_FieldConflict_OriginalWinner_ShouldRemainOpenUntilValueIsRestored()
    {
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US5949181045", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US5949181045", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };

        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        var act = () => service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId,
                "Resolve",
                "operator@meridian.test",
                "Original source selected for a later governed restore.",
                ChosenWinnerSource: "provA"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Apply the selected value through a governed amendment*");
        var canonical = await canonicalStore.GetProjectionAsync(securityId, CancellationToken.None);
        canonical.Should().NotBeNull();
        canonical!.Currency.Should().Be("EUR");
        var retained = await service.GetConflictAsync(conflict.ConflictId, CancellationToken.None);
        retained.Should().NotBeNull();
        retained!.Status.Should().Be("Open");
        retained.ResolvedWinnerSource.Should().BeNull();
        var lineage = await new PostgresSecurityFieldProvenanceStore(_fixture.Options)
            .GetAsync(securityId, CancellationToken.None);
        lineage.Should().BeEmpty(
            "provA cannot be asserted until its selected value is restored");
    }

    [SecurityMasterDatabaseFact]
    public async Task FieldProvenanceUpsert_StaleRecord_ShouldNotReplaceNewerAttribution()
    {
        var securityId = Guid.NewGuid();
        var store = new PostgresSecurityFieldProvenanceStore(_fixture.Options);
        var newerRecordedAt = new DateTimeOffset(2026, 6, 1, 12, 2, 0, TimeSpan.Zero);
        var newer = new SecurityFieldProvenanceRecord(
            securityId,
            "EconomicDefinition.Coupon",
            "new-provider",
            AsOf: newerRecordedAt,
            UpdatedBy: "new-operator",
            Confidence: 0.99m,
            Origin: SecurityFieldProvenanceOrigins.OperatorFieldEdit,
            OriginReference: "revision-new",
            RecordedAt: newerRecordedAt);
        await store.UpsertAsync(newer, CancellationToken.None);

        await store.UpsertAsync(newer with
        {
            SourceSystem = "stale-provider",
            UpdatedBy = "stale-operator",
            OriginReference = "revision-stale",
            RecordedAt = newerRecordedAt.AddMinutes(-1)
        }, CancellationToken.None);

        var retained = (await store.GetAsync(securityId, CancellationToken.None))
            .Should().ContainSingle().Subject;
        retained.SourceSystem.Should().Be("new-provider");
        retained.UpdatedBy.Should().Be("new-operator");
        retained.OriginReference.Should().Be("revision-new");
        retained.RecordedAt.Should().Be(newerRecordedAt);
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_FieldConflict_StaleChallengerWinner_ShouldRemainOpen()
    {
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US02079K3059", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US02079K3059", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };
        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        await canonicalStore.UpsertProjectionAsync(incoming with
        {
            Currency = "GBP",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provC" }),
            Version = 2
        }, CancellationToken.None);

        var act = () => service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId,
                "Resolve",
                "operator@meridian.test",
                "provB no longer matches the golden record.",
                ChosenWinnerSource: "provB"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var retained = await service.GetConflictAsync(conflict.ConflictId, CancellationToken.None);
        retained!.Status.Should().Be("Open");
        var lineage = await new PostgresSecurityFieldProvenanceStore(_fixture.Options)
            .GetAsync(securityId, CancellationToken.None);
        lineage.Should().BeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_DismissalOrIdentifierConflict_WritesNoFieldProvenance()
    {
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US4642872000", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US4642872000", "provB") with
        {
            Currency = "GBP",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };

        var service = NewService(StoreReturning());
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflict = open.Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        // A dismissal records no winner, so it must assert no field attribution.
        var dismissed = await service.ResolveAsync(
            new ResolveConflictRequest(conflict.ConflictId, "Dismiss", "operator@meridian.test", "Values equivalent."),
            CancellationToken.None);
        dismissed.Should().NotBeNull();
        dismissed!.Status.Should().Be("Dismissed");

        var provenanceStore = new PostgresSecurityFieldProvenanceStore(_fixture.Options);
        var lineage = await provenanceStore.GetAsync(securityId, CancellationToken.None);
        lineage.Should().BeEmpty("dismissals and identifier-ownership resolutions carry no field-source winner");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_WhenConflictNotFound_ReturnsNull()
    {
        var result = await NewService(StoreReturning()).ResolveAsync(
            new ResolveConflictRequest(Guid.NewGuid(), "AcceptA", "test"),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordConflictsForProjectionAsync_PersistsIngestTimeConflict()
    {
        var existingId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var existing = MakeProjection(existingId, "Figi", "BBG000B9XRY4", "openfigi");
        var incoming = MakeProjection(newId, "Figi", "BBG000B9XRY4", "polygon");

        // Universe already holds the existing security; the incoming projection collides on its FIGI.
        var store = StoreReturning(existing, incoming);
        var service = NewService(store);

        await service.RecordConflictsForProjectionAsync(incoming, CancellationToken.None);

        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        open.Should().Contain(c => c.SecurityId == newId && c.FieldPath.Contains("Figi"));
    }
}
