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

        var report = await gateway.SubmitOrderAsync(MarketBuy("XYZ", 200));

        report.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FilledQuantity.Should().Be(200m);
    }

    private static OrderRequest MarketBuy(string symbol, decimal qty) => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = qty,
        TimeInForce = TimeInForce.Day
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

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind kind, string value, string? provider, CancellationToken ct = default)
        {
            if (_securityId is null)
                return Task.FromResult<SecurityDetailDto?>(null);

            var detail = new SecurityDetailDto(
                SecurityId: _securityId.Value,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: value,
                Currency: "USD",
                CommonTerms: System.Text.Json.JsonDocument.Parse("{}").RootElement,
                AssetSpecificTerms: System.Text.Json.JsonDocument.Parse("{}").RootElement,
                Identifiers: Array.Empty<SecurityIdentifierDto>(),
                Aliases: Array.Empty<SecurityAliasDto>(),
                Version: 1L,
                EffectiveFrom: DateTimeOffset.UtcNow.AddYears(-1),
                EffectiveTo: null);
            return Task.FromResult<SecurityDetailDto?>(detail);
        }

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            var dto = new TradingParametersDto(
                SecurityId: securityId,
                LotSize: _lotSize,
                TickSize: null,
                PriceBandLower: null,
                PriceBandUpper: null,
                EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-1),
                EffectiveTo: null);
            return Task.FromResult<TradingParametersDto?>(dto);
        }

        public Task<IReadOnlyList<SecurityCorporateActionDto>> ListCorporateActionsAsync(Guid securityId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityCorporateActionDto>>(Array.Empty<SecurityCorporateActionDto>());

        public Task<IReadOnlyList<SecurityPriceBandDto>> ListPriceBandsAsync(Guid securityId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityPriceBandDto>>(Array.Empty<SecurityPriceBandDto>());

        public Task<IReadOnlyList<SecurityStatusTransitionDto>> ListStatusHistoryAsync(Guid securityId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityStatusTransitionDto>>(Array.Empty<SecurityStatusTransitionDto>());
    }
}
