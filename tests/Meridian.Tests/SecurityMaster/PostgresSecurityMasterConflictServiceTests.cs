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
