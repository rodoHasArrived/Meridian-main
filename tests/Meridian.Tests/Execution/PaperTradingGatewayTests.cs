using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Adapters;
using Meridian.Execution.Exceptions;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingGatewayTests
{
    [Fact]
    public async Task ValidateOrderAsync_RejectsStopLimitWithoutPrices()
    {
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.StopLimit,
            Quantity = 10,
            StopPrice = 401m
        };

        var result = await gateway.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("limit price");
    }

    [Fact]
    public async Task SubmitAsync_UsesValidationAndThrowsForUnsupportedRequests()
    {
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.StopMarket,
            Quantity = 10
        };

        Func<Task> submit = async () => await gateway.SubmitAsync(request);

        await submit.Should().ThrowAsync<UnsupportedOrderRequestException>();
    }

    [Fact]
    public async Task SubmitAsync_AcceptsStopLimitOrders_WhenFullySpecified()
    {
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.StopLimit,
            Quantity = 10,
            LimitPrice = 402m,
            StopPrice = 401m,
            TimeInForce = TimeInForce.GoodTilCancelled
        };

        var acknowledgement = await gateway.SubmitAsync(request);

        acknowledgement.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Accepted);
        gateway.Capabilities.SupportedOrderTypes.Should().Contain(Meridian.Execution.Sdk.OrderType.StopLimit);
        gateway.Capabilities.SupportedTimeInForce.Should().Contain(TimeInForce.GoodTilCancelled);
    }

    [Fact]
    public async Task ValidateOrderAsync_AcceptsMarketOnCloseWithoutExtraPrices()
    {
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.MarketOnClose,
            Quantity = 10
        };

        var result = await gateway.ValidateOrderAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateOrderAsync_RejectsLimitOnOpenWithoutLimitPrice()
    {
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.LimitOnOpen,
            Quantity = 10
        };

        var result = await gateway.ValidateOrderAsync(request);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("limit price");
    }

    // ── Lot-size validation tests ─────────────────────────────────────────

    [Fact]
    public async Task ValidateOrderAsync_WithSecurityMaster_NoLotSize_AcceptsAnyQuantity()
    {
        // Security found but has no lot size → validation passes for any qty
        var sm = new StubSecurityMaster(securityId: Guid.NewGuid(), lotSize: null);
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance, sm);

        var result = await gateway.ValidateOrderAsync(MarketBuy("XYZ", 7));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateOrderAsync_WithSecurityMaster_ValidLot_Accepts()
    {
        var sm = new StubSecurityMaster(securityId: Guid.NewGuid(), lotSize: 100m);
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance, sm);

        var result = await gateway.ValidateOrderAsync(MarketBuy("XYZ", 300));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateOrderAsync_WithSecurityMaster_SubLotQuantity_Rejects()
    {
        var sm = new StubSecurityMaster(securityId: Guid.NewGuid(), lotSize: 100m);
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance, sm);

        var result = await gateway.ValidateOrderAsync(MarketBuy("XYZ", 150));

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("lot-size");
        result.Reason.Should().Contain("100");
    }

    [Fact]
    public async Task ValidateOrderAsync_WithSecurityMaster_SellOrderSubLot_Rejects()
    {
        // Sell orders (negative qty) should also be validated by absolute value.
        var sm = new StubSecurityMaster(securityId: Guid.NewGuid(), lotSize: 100m);
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance, sm);

        var result = await gateway.ValidateOrderAsync(new OrderRequest
        {
            Symbol = "XYZ",
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = -50,
            TimeInForce = TimeInForce.Day
        });

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("lot-size");
    }

    [Fact]
    public async Task ValidateOrderAsync_WithSecurityMaster_SecurityNotFound_Accepts()
    {
        // When the symbol is not in Security Master, validation is non-blocking.
        var sm = new StubSecurityMaster(securityId: null, lotSize: null);
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance, sm);

        var result = await gateway.ValidateOrderAsync(MarketBuy("UNKNOWN", 7));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateOrderAsync_WithoutSecurityMaster_AcceptsAnyQuantity()
    {
        // Without Security Master injection, no lot-size constraint is applied.
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);

        var result = await gateway.ValidateOrderAsync(MarketBuy("XYZ", 7));

        result.IsValid.Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static OrderRequest MarketBuy(string symbol, decimal qty) => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = qty,
        TimeInForce = TimeInForce.Day
    };

    /// <summary>
    /// Stub Security Master that returns a fixed security ID (or null) and optional lot size.
    /// </summary>
    private sealed class StubSecurityMaster : ISecurityMasterQueryService
    {
        private readonly Guid? _securityId;
        private readonly decimal? _lotSize;

        public StubSecurityMaster(Guid? securityId, decimal? lotSize)
        {
            _securityId = securityId;
            _lotSize = lotSize;
        }

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind kind, string value, string? provider, CancellationToken ct = default)
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

        public Task<TradingParametersDto?> GetTradingParametersAsync(
            Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            var dto = new TradingParametersDto(
                SecurityId: securityId,
                LotSize: _lotSize,
                TickSize: null,
                ContractMultiplier: null,
                MarginRequirementPct: null,
                TradingHoursUtc: null,
                CircuitBreakerThresholdPct: null,
                AsOf: asOf);
            return Task.FromResult<TradingParametersDto?>(dto);
        }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);
        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }
}
