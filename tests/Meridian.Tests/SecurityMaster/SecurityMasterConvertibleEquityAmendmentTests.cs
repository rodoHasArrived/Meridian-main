using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterConvertibleEquityAmendmentTests
{
    [Fact]
    public async Task AmendConvertibleEquityTermsAsync_UpdatesConversionTerms_AndPreservesPreferredTerms()
    {
        var securityId = Guid.NewGuid();
        var underlyingSecurityId = Guid.NewGuid();
        var newUnderlyingId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "ConvertiblePreferred", underlyingSecurityId));

        var service = CreateService(eventStore, snapshotStore, store);
        var request = new AmendConvertibleEquityTermsRequest(
            ExpectedVersion: 7,
            UnderlyingSecurityId: newUnderlyingId,
            ConversionRatio: 3.0m,
            ConversionPrice: 45.00m,
            ConversionStartDate: new DateOnly(2028, 1, 1),
            ConversionEndDate: new DateOnly(2032, 12, 31),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "conversion term update");

        var detail = await service.AmendConvertibleEquityTermsAsync(securityId, request);

        detail.SecurityId.Should().Be(securityId);
        detail.Version.Should().Be(8);
        detail.AssetSpecificTerms.GetProperty("classification").GetString().Should().Be("ConvertiblePreferred");
        detail.AssetSpecificTerms.GetProperty("convertibleTerms").GetProperty("underlyingSecurityId").GetGuid().Should().Be(newUnderlyingId);
        detail.AssetSpecificTerms.GetProperty("convertibleTerms").GetProperty("conversionRatio").GetDecimal().Should().Be(3.0m);
        detail.AssetSpecificTerms.GetProperty("convertibleTerms").GetProperty("conversionPrice").GetDecimal().Should().Be(45.00m);
        // Preferred terms must be preserved
        detail.AssetSpecificTerms.GetProperty("preferredTerms").GetProperty("dividendRate").GetDecimal().Should().Be(5.75m);

        await eventStore.Received(1).AppendAsync(
            securityId,
            7,
            Arg.Is<IReadOnlyList<SecurityMasterEventEnvelope>>(events =>
                events.Count == 1 &&
                EventPayloadPreservesPreferredTerms(events[0].Payload)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendConvertibleEquityTermsAsync_Convertible_ClassificationNotPreserved_UpdatesRatio()
    {
        var securityId = Guid.NewGuid();
        var underlyingSecurityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "Convertible", underlyingSecurityId));

        var service = CreateService(eventStore, snapshotStore, store);
        var request = new AmendConvertibleEquityTermsRequest(
            ExpectedVersion: 3,
            UnderlyingSecurityId: underlyingSecurityId,
            ConversionRatio: 4.5m,
            ConversionPrice: null,
            ConversionStartDate: null,
            ConversionEndDate: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "ratio change");

        var detail = await service.AmendConvertibleEquityTermsAsync(securityId, request);

        detail.AssetSpecificTerms.GetProperty("convertibleTerms").GetProperty("conversionRatio").GetDecimal().Should().Be(4.5m);
        detail.AssetSpecificTerms.GetProperty("classification").GetString().Should().Be("Convertible");
    }

    [Fact]
    public async Task AmendConvertibleEquityTermsAsync_NonEquityAssetClass_ThrowsInvalidOperationException()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateNonEquityProjection(securityId));

        var service = CreateService(eventStore, snapshotStore, store);
        var request = new AmendConvertibleEquityTermsRequest(
            ExpectedVersion: 1,
            UnderlyingSecurityId: Guid.NewGuid(),
            ConversionRatio: 2.0m,
            ConversionPrice: null,
            ConversionStartDate: null,
            ConversionEndDate: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "bad amend");

        var act = () => service.AmendConvertibleEquityTermsAsync(securityId, request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not an equity*");
    }

    [Fact]
    public async Task AmendConvertibleEquityTermsAsync_PreferredOnlyEquity_ThrowsInvalidOperationException()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "Preferred", Guid.Empty));

        var service = CreateService(eventStore, snapshotStore, store);
        var request = new AmendConvertibleEquityTermsRequest(
            ExpectedVersion: 1,
            UnderlyingSecurityId: Guid.NewGuid(),
            ConversionRatio: 2.0m,
            ConversionPrice: null,
            ConversionStartDate: null,
            ConversionEndDate: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "bad amend");

        var act = () => service.AmendConvertibleEquityTermsAsync(securityId, request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not currently have convertible-equity terms*");
    }

    [Fact]
    public async Task AmendConvertibleEquityTermsAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var securityId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<SecurityProjectionRecord?>(cts.Token));

        var service = CreateService(eventStore, snapshotStore, store);
        var request = new AmendConvertibleEquityTermsRequest(
            ExpectedVersion: 1,
            UnderlyingSecurityId: Guid.NewGuid(),
            ConversionRatio: 2.0m,
            ConversionPrice: null,
            ConversionStartDate: null,
            ConversionEndDate: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "cancellation");

        var act = () => service.AmendConvertibleEquityTermsAsync(securityId, request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static SecurityMasterService CreateService(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterSnapshotStore snapshotStore,
        ISecurityMasterStore store)
    {
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        return new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);
    }

    private static SecurityProjectionRecord CreateProjection(Guid securityId, string classification, Guid underlyingSecurityId)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Meridian Convertible",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "MCVT",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Meridian Convertible",
                currency = "USD",
                exchange = "XNYS",
                lotSize = 100,
                tickSize = 0.01m
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "A",
                classification,
                preferredTerms = classification is "Preferred" or "ConvertiblePreferred"
                    ? (object)new
                    {
                        dividendRate = 5.75m,
                        dividendType = "Fixed",
                        redemptionPrice = 25.00m,
                        callableDate = new DateOnly(2030, 1, 15),
                        liquidationPreference = new { kind = "Pari" }
                    }
                    : null,
                convertibleTerms = classification is "Convertible" or "ConvertiblePreferred"
                    ? (object)new
                    {
                        underlyingSecurityId,
                        conversionRatio = 2.5m,
                        conversionPrice = 50.00m,
                        conversionStartDate = new DateOnly(2027, 1, 15),
                        conversionEndDate = new DateOnly(2031, 12, 31)
                    }
                    : null
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "codex",
                asOf = DateTimeOffset.UtcNow
            }),
            Version: classification is "Convertible" ? 3 : 7,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo: null,
            Identifiers: new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MCVT", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>());

    private static SecurityProjectionRecord CreateNonEquityProjection(Guid securityId)
        => new(
            SecurityId: securityId,
            AssetClass: "FixedIncome",
            Status: SecurityStatusDto.Active,
            DisplayName: "Some Bond",
            Currency: "USD",
            PrimaryIdentifierKind: "ISIN",
            PrimaryIdentifierValue: "US1234567890",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Some Bond", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { schemaVersion = 1 }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test", updatedBy = "codex", asOf = DateTimeOffset.UtcNow }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers: Array.Empty<SecurityIdentifierDto>(),
            Aliases: Array.Empty<SecurityAliasDto>());

    private static bool EventPayloadPreservesPreferredTerms(JsonElement payload)
    {
        return payload.TryGetProperty("legacyAssetSpecificTerms", out var terms) &&
               terms.TryGetProperty("preferredTerms", out var preferred) &&
               preferred.TryGetProperty("dividendRate", out var rate) &&
               rate.GetDecimal() == 5.75m;
    }
}
