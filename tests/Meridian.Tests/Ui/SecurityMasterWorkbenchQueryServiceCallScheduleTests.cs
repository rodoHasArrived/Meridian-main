using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

/// <summary>
/// The workbench schedule read of declared call and put schedules (ADR-024 and ADR-025, increment 4b).
/// Rows stamped at the readable version carry the arrays, and the economic Call fill copies them into
/// the economic <c>call</c> module's lists. The fill takes <c>firstCallDate</c> only from a class's own
/// scalar call terms, and the workbench reads only <c>call.firstCallDate</c>, so a declared schedule
/// adds no "Call" lifecycle event and does not move the next lifecycle date. The economic documents
/// here come from the real adapter over a stored row, so the fill and the read are pinned together.
/// Increment 8 changes these tests on purpose, when the workbench starts reading declared schedules.
/// </summary>
public sealed class SecurityMasterWorkbenchQueryServiceCallScheduleTests
{
    private static readonly Guid SecurityId = Guid.Parse("4B0C2D1E-7A55-4F0B-9C3D-2E6A8F1B4C70");
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    // A version-2 StructuredCredit row stamped 4 by hand, declaring two calls and a put (no node writes
    // version 4 yet). It has no scalar call terms; its nonCallPeriodEnd never feeds the economic call
    // module either.
    private const string DeclaringStructuredCredit =
        """{"attachmentPoint":0.12,"callSchedule":[{"callDate":"2028-06-15","callPrice":100,"priceBasis":"PercentOfPar","priceType":"Fixed"},{"callDate":"2029-06-15","callPrice":100}],"collateralType":"CLO","couponOrIndex":"SOFR+250","currentFactor":0.8235,"detachmentPoint":0.20,"factorSchedule":"See trustee report 2026-07","factorScheduleEntries":[{"asOfDate":"2026-06-01","factor":0.8412},{"asOfDate":"2026-07-01","factor":0.8235}],"maturity":"2031-06-15","nonCallPeriodEnd":"2027-06-15","originalFace":10000000,"poolId":"MRDN-2026-1","putSchedule":[{"putDate":"2029-12-15","putPrice":100}],"reinvestmentPeriodEnd":"2028-06-15","schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":4,"seniority":"Mezzanine","structuredCreditKind":"Clo","tranche":"B","weightedAverageCoupon":7.85,"weightedAverageLoanAgeMonths":14,"weightedAverageMaturityMonths":58}""";

    // A fixed corporate bond stamped 3 by hand (no node writes version 3 yet), whose scalar callDate
    // (2028-01-01) sits between its two declared calls, so neither the earliest entry nor any entry
    // date can stand in for the scalar.
    private const string DeclaringBond =
        """{"callDate":"2028-01-01","callSchedule":[{"callDate":"2027-01-01","callPrice":102,"priceBasis":"PercentOfPar","priceType":"Fixed"},{"callDate":"2029-01-01","callPrice":100}],"capRate":null,"couponRate":4.25,"couponType":"Fixed","dayCount":"30/360","floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":true,"issueDate":"2024-01-01","issuerName":"ACME Corp","legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":1000,"paymentFrequency":"SemiAnnual","preRefundDate":null,"principalSchedule":[],"putSchedule":[{"putDate":"2028-07-01","putPrice":100}],"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":"Senior","spreadBps":null,"stepSchedule":[],"subclass":"Corporate"}""";

    [Fact]
    public async Task GetTrustSnapshotAsync_StructuredCreditDeclaringCallAndPutSchedules_AddsNoCallEventAndKeepsTheMaturityAsNextLifecycleDate()
    {
        var queryService = new StoredRowSecurityMasterQueryService(Projection("StructuredCredit", DeclaringStructuredCredit));
        var economic = (await queryService.GetEconomicDefinitionByIdAsync(SecurityId))!;

        // The fill: a call module with the declared lists, callable, but no first call date or price.
        var call = economic.EconomicTerms.GetProperty("call");
        call.ValueKind.Should().Be(JsonValueKind.Object, "a StructuredCredit that declares a schedule gains a call module");
        call.GetProperty("isCallable").GetBoolean().Should().BeTrue();
        call.GetProperty("firstCallDate").ValueKind.Should().Be(JsonValueKind.Null, "StructuredCredit has no scalar call terms");
        call.GetProperty("callPrice").ValueKind.Should().Be(JsonValueKind.Null);
        call.GetProperty("callSchedule").EnumerateArray().Select(static entry => entry.GetProperty("callDt").GetString())
            .Should().Equal("2028-06-15", "2029-06-15");
        call.GetProperty("isPuttable").GetBoolean().Should().BeTrue();
        call.GetProperty("putSchedule").EnumerateArray().Select(static entry => entry.GetProperty("putDt").GetString())
            .Should().Equal("2029-12-15");
        economic.EconomicTerms.TryGetProperty("structuredProduct", out var structuredProduct).Should().BeTrue();
        structuredProduct.ValueKind.Should().Be(JsonValueKind.Object);

        var snapshot = await CreateService(queryService).GetTrustSnapshotAsync(SecurityId, fundProfileId: null);

        snapshot.Should().NotBeNull();
        snapshot!.ScheduleSummary.Should().NotBeNull();
        snapshot.ScheduleSummary!.SupportsCashflowSchedule.Should().BeTrue("structuredProduct already supports it");
        snapshot.ScheduleSummary.NextLifecycleDate.Should().Be(
            new DateOnly(2031, 6, 15), "with no corporate action, the maturity stays the next lifecycle date");
        snapshot.ScheduleBook.Should().NotBeNull();
        snapshot.ScheduleBook!.NextLifecycleDate.Should().Be(new DateOnly(2031, 6, 15));
        snapshot.ScheduleBook.Events.Should().NotContain(
            item => item.EventType == "Call", "the workbench reads only call.firstCallDate, which a declared schedule leaves null");
        snapshot.ScheduleBook.Events.Should().ContainSingle(item => item.EventType == "Maturity")
            .Which.EffectiveDate.Should().Be(new DateOnly(2031, 6, 15));
    }

    [Fact]
    public async Task GetTrustSnapshotAsync_BondDeclaringCallSchedulesWithAScalarCallDate_KeepsExactlyOneCallEventAtTheScalarDate()
    {
        var queryService = new StoredRowSecurityMasterQueryService(Projection("Bond", DeclaringBond));
        var economic = (await queryService.GetEconomicDefinitionByIdAsync(SecurityId))!;

        // The fill: firstCallDate stays the scalar callDate, and the lists carry the declared entries.
        var call = economic.EconomicTerms.GetProperty("call");
        call.GetProperty("firstCallDate").GetString().Should().Be("2028-01-01");
        call.GetProperty("callSchedule").EnumerateArray().Select(static entry => entry.GetProperty("callDt").GetString())
            .Should().Equal("2027-01-01", "2029-01-01");
        call.GetProperty("isPuttable").GetBoolean().Should().BeTrue();

        var snapshot = await CreateService(queryService).GetTrustSnapshotAsync(SecurityId, fundProfileId: null);

        snapshot.Should().NotBeNull();
        snapshot!.ScheduleSummary.Should().NotBeNull();
        snapshot.ScheduleSummary!.SupportsCashflowSchedule.Should().BeTrue();
        snapshot.ScheduleSummary.NextLifecycleDate.Should().Be(new DateOnly(2028, 1, 1), "the scalar call date precedes the maturity");
        snapshot.ScheduleBook.Should().NotBeNull();
        snapshot.ScheduleBook!.Events.Where(static item => item.EventType == "Call")
            .Should().ContainSingle("only the scalar first call date becomes a lifecycle event")
            .Which.EffectiveDate.Should().Be(new DateOnly(2028, 1, 1));
        snapshot.ScheduleBook.Events.Should().ContainSingle(item => item.EventType == "Maturity")
            .Which.EffectiveDate.Should().Be(new DateOnly(2030, 1, 1));
    }

    private static SecurityProjectionRecord Projection(string assetClass, string assetSpecificTerms)
        => new(
            SecurityId: SecurityId,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: $"Declaring {assetClass}",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: $"DECL-{assetClass}-1",
            CommonTerms: Json($$"""{"displayName":"Declaring {{assetClass}}","currency":"USD"}"""),
            AssetSpecificTerms: Json(assetSpecificTerms),
            Provenance: Json("""{"sourceSystem":"golden-edm","sourceRecordId":"edm-declaring","asOf":"2026-01-02T00:00:00+00:00","updatedBy":"steward"}"""),
            Version: 3,
            EffectiveFrom: EffectiveFrom,
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"DECL-{assetClass}-1", true, EffectiveFrom)],
            Aliases: []);

    private static SecurityMasterWorkbenchQueryService CreateService(StoredRowSecurityMasterQueryService queryService) =>
        new(
            queryService,
            new EmptySecurityValidationService(),
            new EmptySecurityMasterConflictService(),
            new EmptySecurityMasterIngestStatusService(),
            Substitute.For<IStrategyRepository>(),
            new PortfolioReadService(),
            new LedgerReadService(),
            new ReportGenerationService(queryService));

    /// <summary>
    /// Serves one stored row the way a deployed node reads it: the detail is the projection's, and the
    /// economic definition is the adapter's read of the same terms.
    /// </summary>
    private sealed class StoredRowSecurityMasterQueryService(SecurityProjectionRecord projection)
        : ContractSecurityMasterQueryService
    {
        public Task<SecurityDetailDto?> GetByIdAsync(Guid requestedSecurityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(requestedSecurityId == projection.SecurityId ? SecurityMasterMapping.ToDetail(projection) : null);
        }

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid requestedSecurityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => GetByIdAsync(requestedSecurityId, ct);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null)
            => GetByIdAsync(projection.SecurityId, ct);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);
        }

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);
        }

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid requestedSecurityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<SecurityEconomicDefinitionRecord?>(requestedSecurityId == projection.SecurityId
                ? SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection)
                : null);
        }

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid requestedSecurityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<TradingParametersDto?>(requestedSecurityId == projection.SecurityId
                ? new TradingParametersDto(projection.SecurityId, 1m, 0.01m, 1m, 0.25m, "14:30-21:00", 0.10m, asOf)
                : null);
        }

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid requestedSecurityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);
        }

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }

    private sealed class EmptySecurityValidationService : ISecurityValidationService
    {
        public Task<SecurityValidationReportDto> ValidateSecurityAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult(new SecurityValidationReportDto(securityId, "Security", DateTimeOffset.UtcNow, false, 0, 0, []));

        public Task<SecurityValidationReportDto> ValidateUniverseAsync(CancellationToken ct = default) =>
            Task.FromResult(new SecurityValidationReportDto(null, "Universe", DateTimeOffset.UtcNow, false, 0, 0, []));

        public SecurityValidationReportDto ValidateRecord(
            SecurityProjectionRecord record,
            IReadOnlyList<SecurityProjectionRecord> universe,
            DateTimeOffset? evaluatedAtUtc = null) =>
            new(record.SecurityId, "Security", evaluatedAtUtc ?? DateTimeOffset.UtcNow, false, 0, 0, []);
    }

    private sealed class EmptySecurityMasterConflictService : ISecurityMasterConflictService
    {
        public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SecurityMasterConflict>>([]);

        public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct) =>
            Task.FromResult<SecurityMasterConflict?>(null);

        public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct) =>
            Task.FromResult<SecurityMasterConflict?>(null);

        public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct) =>
            Task.CompletedTask;

        public Task RecordFieldConflictsAsync(SecurityProjectionRecord previous, SecurityProjectionRecord incoming, CancellationToken ct) =>
            Task.CompletedTask;

        public Task ReconcileOpenFieldConflictsAsync(SecurityProjectionRecord persisted, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class EmptySecurityMasterIngestStatusService : ISecurityMasterIngestStatusService
    {
        public SecurityMasterIngestStatusSnapshot GetSnapshot() => new(null, null);
    }

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
