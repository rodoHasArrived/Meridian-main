using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Unit tests for <see cref="SecurityMasterConflictService"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterConflictServiceTests
{
    private static SecurityProjectionRecord MakeProjection(
        Guid securityId,
        string identifierKind,
        string identifierValue,
        string? provider = null)
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
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = provider ?? "default" }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers: new[] { identifier },
            Aliases: Array.Empty<SecurityAliasDto>());
    }

    [Fact]
    public async Task GetOpenConflictsAsync_WhenNoSecurities_ReturnsEmpty()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_WhenSameIdentifierFromTwoProviders_ReturnsConflict()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        // Both securities claim the same ISIN from different providers
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Isin", "US0378331005", provider: "alpaca"),
                MakeProjection(securityB, "Isin", "US0378331005", provider: "polygon")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().HaveCount(1);
        var conflict = conflicts[0];
        conflict.ConflictKind.Should().Be("IdentifierAmbiguity");
        conflict.FieldPath.Should().Contain("Isin");
        conflict.Status.Should().Be("Open");
        conflict.ProviderA.Should().NotBeNullOrWhiteSpace();
        conflict.ProviderB.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_WhenSameIdentifierSameSecurityDifferentProviders_NoConflict()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();

        // Same ISIN, same SecurityId from two providers — NOT a conflict
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityId, "Isin", "US0378331005", provider: "alpaca")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().BeEmpty("same identifier on the same security is not a conflict");
    }

    [Fact]
    public async Task GetOpenConflictsAsync_ConflictIdIsStable_SameConflictDetectedTwice()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var projections = new[]
        {
            MakeProjection(securityA, "Isin", "US1234567890", provider: "provA"),
            MakeProjection(securityB, "Isin", "US1234567890", provider: "provB")
        };

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(projections);

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var first = await service.GetOpenConflictsAsync(CancellationToken.None);
        var second = await service.GetOpenConflictsAsync(CancellationToken.None);

        first.Should().HaveCount(1);
        second.Should().HaveCount(1);
        first[0].ConflictId.Should().Be(second[0].ConflictId,
            "the same conflict pair must yield the same deterministic ID");
    }

    [Fact]
    public async Task ResolveAsync_WhenConflictExists_UpdatesStatus()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Cusip", "037833100", provider: "provA"),
                MakeProjection(securityB, "Cusip", "037833100", provider: "provB")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        // Seed the conflict
        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);
        conflicts.Should().HaveCount(1);

        var conflictId = conflicts[0].ConflictId;
        var request = new ResolveConflictRequest(conflictId, "AcceptA", "operator@meridian.test");

        var updated = await service.ResolveAsync(request, CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be("Resolved");
    }

    [Fact]
    public async Task ResolveAsync_WhenConflictDismissed_SetsStatusDismissed()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Figi", "BBG000B9XRY4", provider: "openfigi"),
                MakeProjection(securityB, "Figi", "BBG000B9XRY4", provider: "polygon")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflictId = conflicts[0].ConflictId;

        var updated = await service.ResolveAsync(
            new ResolveConflictRequest(conflictId, "Dismiss", "qa@meridian.test"),
            CancellationToken.None);

        updated!.Status.Should().Be("Dismissed");
    }

    [Fact]
    public async Task ResolveAsync_WhenConflictNotFound_ReturnsNull()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var result = await service.ResolveAsync(
            new ResolveConflictRequest(Guid.NewGuid(), "AcceptA", "test"),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetConflictAsync_WhenNotFound_ReturnsNull()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var result = await service.GetConflictAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_OnceResolved_ExcludesFromOpenList()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Ticker", "AAPL", provider: "alpaca"),
                MakeProjection(securityB, "Ticker", "AAPL", provider: "polygon")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var initial = await service.GetOpenConflictsAsync(CancellationToken.None);
        initial.Should().HaveCount(1);

        await service.ResolveAsync(
            new ResolveConflictRequest(initial[0].ConflictId, "AcceptA", "test"),
            CancellationToken.None);

        var afterResolve = await service.GetOpenConflictsAsync(CancellationToken.None);
        afterResolve.Should().BeEmpty("resolved conflicts must not appear in the open list");
    }
}
