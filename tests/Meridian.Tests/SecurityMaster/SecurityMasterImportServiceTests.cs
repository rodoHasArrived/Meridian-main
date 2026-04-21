using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterImportServiceTests
{
    [Fact]
    public async Task ImportAsync_WhenRecordsAreCreated_TriggersAutomaticConflictRecordingPerSecurity()
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        conflictService.GetOpenConflictsAsync(Arg.Any<CancellationToken>())
            .Returns(
                Array.Empty<SecurityMasterConflict>(),
                new[]
                {
                    new SecurityMasterConflict(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "IdentifierAmbiguity",
                        "Identifiers.Isin",
                        "polygon",
                        Guid.NewGuid().ToString(),
                        "alpaca",
                        Guid.NewGuid().ToString(),
                        DateTimeOffset.UtcNow,
                        "Open")
                });

        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var securityMasterService = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            new SecurityMasterOptions
            {
                SnapshotIntervalVersions = 50,
                ResolveInactiveByDefault = true
            },
            NullLogger<SecurityMasterService>.Instance,
            conflictService);

        var importService = new SecurityMasterImportService(
            securityMasterService,
            new SecurityMasterCsvParser(),
            NullLogger<SecurityMasterImportService>.Instance,
            conflictService);

        var firstSecurityId = Guid.NewGuid();
        var secondSecurityId = Guid.NewGuid();
        var fileContent = JsonSerializer.Serialize(new[]
        {
            CreateRequest(firstSecurityId, "IMPA", "US0000000001", "polygon"),
            CreateRequest(secondSecurityId, "IMPB", "US0000000001", "alpaca")
        });

        var result = await importService.ImportAsync(fileContent, ".json", ct: CancellationToken.None);

        result.Imported.Should().Be(2);
        result.ConflictsDetected.Should().Be(1);

        await conflictService.Received(1).RecordConflictsForProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(projection => projection.SecurityId == firstSecurityId),
            Arg.Any<CancellationToken>());
        await conflictService.Received(1).RecordConflictsForProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(projection => projection.SecurityId == secondSecurityId),
            Arg.Any<CancellationToken>());
    }

    private static CreateSecurityRequest CreateRequest(Guid securityId, string ticker, string isin, string provider)
        => new(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = $"Imported {ticker}",
                currency = "USD"
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            new[]
            {
                new SecurityIdentifierDto(
                    Kind: SecurityIdentifierKind.Ticker,
                    Value: ticker,
                    IsPrimary: true,
                    ValidFrom: DateTimeOffset.UtcNow.AddDays(-1),
                    ValidTo: null,
                    Provider: provider),
                new SecurityIdentifierDto(
                    Kind: SecurityIdentifierKind.Isin,
                    Value: isin,
                    IsPrimary: false,
                    ValidFrom: DateTimeOffset.UtcNow.AddDays(-1),
                    ValidTo: null,
                    Provider: provider)
            },
            DateTimeOffset.UtcNow,
            "import-test",
            "codex",
            null,
            "bulk import");
}
