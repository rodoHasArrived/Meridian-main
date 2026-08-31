using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Correcting an alias must not restate WHEN it was recorded. As-of rebuilds retain aliases whose
/// <see cref="SecurityAliasDto.CreatedAt"/> is at or before the cutoff, so advancing that stamp on an
/// edit would retroactively remove a corrected identifier from every view older than the correction —
/// an identifier recorded in January and corrected in June would vanish from the January view.
/// </summary>
public sealed class SecurityMasterAliasRecordedHistoryTests
{
    private static readonly DateTimeOffset RecordedInJanuary = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpsertAliasAsync_ReturnsPersistedCreationStamp_NotAFreshOne()
    {
        var store = Substitute.For<ISecurityMasterStore>();

        // The store retains the original creation facts on conflict and echoes them back.
        store.UpsertAliasAsync(Arg.Any<SecurityAliasDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<SecurityAliasDto>() with
            {
                CreatedAt = RecordedInJanuary,
                CreatedBy = "january.operator"
            });

        var service = CreateService(store);

        var corrected = await service.UpsertAliasAsync(new UpsertSecurityAliasRequest(
            AliasId: Guid.NewGuid(),
            SecurityId: Guid.NewGuid(),
            AliasKind: "Ticker",
            AliasValue: "MRDN",
            Provider: "Refinitiv",
            Scope: SecurityAliasScope.Operations,
            CreatedBy: "june.operator",
            ValidFrom: RecordedInJanuary,
            ValidTo: null,
            Reason: "ticker correction"));

        corrected.CreatedAt.Should().Be(
            RecordedInJanuary,
            "an edit must report when the alias was first recorded, not when it was corrected");
        corrected.CreatedBy.Should().Be("january.operator");
        corrected.AliasValue.Should().Be("MRDN", "the corrected value itself must still be returned");
    }

    [Fact]
    public async Task UpsertAliasAsync_FallsBackToProposedStamp_WhenStoreCannotReadRowBack()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.UpsertAliasAsync(Arg.Any<SecurityAliasDto>(), Arg.Any<CancellationToken>())
            .Returns((SecurityAliasDto?)null);

        var service = CreateService(store);

        var alias = await service.UpsertAliasAsync(new UpsertSecurityAliasRequest(
            AliasId: Guid.NewGuid(),
            SecurityId: Guid.NewGuid(),
            AliasKind: "Ticker",
            AliasValue: "MRDN",
            Provider: null,
            Scope: SecurityAliasScope.Operations,
            CreatedBy: "operator",
            ValidFrom: RecordedInJanuary,
            ValidTo: null,
            Reason: null));

        alias.Should().NotBeNull();
        alias.CreatedBy.Should().Be("operator");
    }

    private static SecurityMasterService CreateService(ISecurityMasterStore store)
        => new(
            Substitute.For<ISecurityMasterEventStore>(),
            Substitute.For<ISecurityMasterSnapshotStore>(),
            store,
            new SecurityMasterAggregateRebuilder(
                Substitute.For<ISecurityMasterEventStore>(),
                Substitute.For<ISecurityMasterSnapshotStore>()),
            new SecurityMasterOptions(),
            NullLogger<SecurityMasterService>.Instance);
}
