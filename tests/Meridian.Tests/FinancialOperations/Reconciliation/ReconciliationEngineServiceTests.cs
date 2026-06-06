using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Reconciliation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.FinancialOperations.Reconciliation;

public sealed class ReconciliationEngineServiceTests
{
    [Fact]
    public async Task RunAsync_BuildsPortfolioLedgerMatchesAndBreaks()
    {
        var service = new ReconciliationEngineService(
            new EmptySecurityMasterQueryService(),
            NullLogger<ReconciliationEngineService>.Instance);
        var request = new EngineReconciliationRequest(
            PortfolioId: "portfolio-1",
            AsOf: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            PortfolioPositions:
            [
                new PortfolioPositionInput("AAPL", 100m)
            ],
            LedgerBalances: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = 100m,
                ["TSLA"] = 50m
            },
            SecurityIdentifiers: []);

        var result = await service.RunAsync(request);

        result.PortfolioId.Should().Be("portfolio-1");
        result.TotalChecks.Should().Be(2);
        result.MatchCount.Should().Be(1);
        result.BreakCount.Should().Be(1);
        result.Matches.Should().Contain(match => match.CheckId.Length > 0);
        result.Breaks.Should().ContainSingle();
    }

    private sealed class EmptySecurityMasterQueryService : ISecurityMasterQueryService
    {
        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>(Array.Empty<SecuritySummaryDto>());

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }
}
