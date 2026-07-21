using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class PaperExecutionGatewayLotSizeTests
{
    [Fact]
    public async Task SubmitOrderAsync_WithSecurityMaster_SubLotQuantity_Throws()
    {
        var sm = new StubSecurityMaster(securityId: Guid.NewGuid(), lotSize: 100m);
        var gateway = new Meridian.Execution.PaperTradingGateway(NullLogger<Meridian.Execution.PaperTradingGateway>.Instance, sm);

        var act = async () => await gateway.SubmitOrderAsync(MarketBuy("XYZ", 150));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*lot-size*");
    }

    [Fact]
    public async Task SubmitOrderAsync_WithSecurityMaster_ValidLot_Accepts()
    {
        var sm = new StubSecurityMaster(securityId: Guid.NewGuid(), lotSize: 100m);
        var gateway = new Meridian.Execution.PaperTradingGateway(NullLogger<Meridian.Execution.PaperTradingGateway>.Instance, sm);

        var report = await gateway.SubmitOrderAsync(MarketBuy("XYZ", 200, simulatedPrice: 50m));

        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FilledQuantity.Should().Be(200m);
    }

    private static OrderRequest MarketBuy(string symbol, decimal qty, decimal? simulatedPrice = null) => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = qty,
        TimeInForce = TimeInForce.Day,
        LimitPrice = simulatedPrice
    };

    private sealed class StubSecurityMaster : ISecurityMasterQueryService
    {
        private readonly Guid? _securityId;
        private readonly decimal? _lotSize;

        public StubSecurityMaster(Guid? securityId, decimal? lotSize)
        {
            _securityId = securityId;
            _lotSize = lotSize;
        }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(_securityId == securityId ? CreateDetail(securityId, "XYZ") : null);

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => GetByIdAsync(securityId, ct);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind kind, string value, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null)
        {
            if (_securityId is null)
                return Task.FromResult<SecurityDetailDto?>(null);

            return Task.FromResult<SecurityDetailDto?>(CreateDetail(_securityId.Value, value));
        }

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            var dto = new TradingParametersDto(
                SecurityId: securityId,
                LotSize: _lotSize,
                TickSize: null,
                ContractMultiplier: null,
                MarginRequirementPct: null,
                TradingHoursUtc: null,
                CircuitBreakerThresholdPct: null,
                AsOf: DateTimeOffset.UtcNow);
            return Task.FromResult<TradingParametersDto?>(dto);
        }

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>(Array.Empty<SecuritySummaryDto>());

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);

        private static SecurityDetailDto CreateDetail(Guid securityId, string displayName) =>
            new(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: displayName,
                Currency: "USD",
                CommonTerms: System.Text.Json.JsonDocument.Parse("{}").RootElement,
                AssetSpecificTerms: System.Text.Json.JsonDocument.Parse("{}").RootElement,
                Identifiers: Array.Empty<SecurityIdentifierDto>(),
                Aliases: Array.Empty<SecurityAliasDto>(),
                Version: 1L,
                EffectiveFrom: DateTimeOffset.UtcNow.AddYears(-1),
                EffectiveTo: null);
    }
}
