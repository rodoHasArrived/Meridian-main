using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Reporting;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

/// <summary>
/// Builds a <see cref="SecurityMasterWorkbenchQueryService"/> over one stored row for the schedule-book
/// tests: the detail is the projection's and the economic definition is the adapter's read of the same
/// terms, as on a deployed node.
/// </summary>
internal static class SecurityMasterWorkbenchScheduleBookHarness
{
    internal static SecurityMasterWorkbenchQueryService CreateService(StoredRowSecurityMasterQueryService queryService) =>
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
    internal sealed class StoredRowSecurityMasterQueryService(
        SecurityProjectionRecord projection, IReadOnlyList<CorporateActionDto>? corporateActions = null)
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
            return Task.FromResult(requestedSecurityId == projection.SecurityId ? corporateActions ?? [] : []);
        }

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }

    internal sealed class EmptySecurityValidationService : ISecurityValidationService
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

    internal sealed class EmptySecurityMasterConflictService : ISecurityMasterConflictService
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

    internal sealed class EmptySecurityMasterIngestStatusService : ISecurityMasterIngestStatusService
    {
        public SecurityMasterIngestStatusSnapshot GetSnapshot() => new(null, null);
    }
}
