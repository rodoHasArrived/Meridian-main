using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Alias rows feed recorded-as-of reconstruction. Until the schema retains append-only alias
/// revisions, a material edit must fail closed instead of rewriting what an older view reports.
/// </summary>
public sealed class SecurityMasterAliasRecordedHistoryTests
{
    private static readonly DateTimeOffset RecordedInJanuary = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpsertAliasAsync_IdempotentReplay_ReturnsPersistedCreationStamp()
    {
        var store = Substitute.For<ISecurityMasterStore>();

        // The store retains the original creation facts on an idempotent replay and echoes them back.
        store.UpsertAliasAsync(Arg.Any<SecurityAliasDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<SecurityAliasDto>() with
            {
                CreatedAt = RecordedInJanuary,
                CreatedBy = "january.operator"
            });

        var service = CreateService(store);

        var replayed = await service.UpsertAliasAsync(new UpsertSecurityAliasRequest(
            AliasId: Guid.NewGuid(),
            SecurityId: Guid.NewGuid(),
            AliasKind: "Ticker",
            AliasValue: "MRDN",
            Provider: "Refinitiv",
            Scope: SecurityAliasScope.Operations,
            CreatedBy: "june.operator",
            ValidFrom: RecordedInJanuary,
            ValidTo: null,
            Reason: "original registration"));

        replayed.CreatedAt.Should().Be(
            RecordedInJanuary,
            "an idempotent replay must report when the alias was first recorded");
        replayed.CreatedBy.Should().Be("january.operator");
        replayed.AliasValue.Should().Be("MRDN");
    }

    [Fact]
    public async Task UpsertAliasAsync_FailsClosed_WhenStoreCannotConfirmPersistence()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.UpsertAliasAsync(Arg.Any<SecurityAliasDto>(), Arg.Any<CancellationToken>())
            .Returns((SecurityAliasDto?)null);

        var service = CreateService(store);

        var act = () => service.UpsertAliasAsync(new UpsertSecurityAliasRequest(
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

        await act.Should().ThrowAsync<SecurityAliasHistoryConflictException>()
            .WithMessage("*append-only alias revisions*");
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
