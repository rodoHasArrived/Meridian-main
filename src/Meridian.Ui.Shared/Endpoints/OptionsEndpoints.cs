using System.Text.Json;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering equity options API endpoints.
/// Provides access to option chains, quotes, greeks, trades, and open interest.
/// </summary>
public static class OptionsEndpoints
{
    /// <summary>
    /// Maps all options / derivatives API endpoints.
    /// </summary>
    public static void MapOptionsEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Options");

        // GET /api/options/expirations/{underlyingSymbol} — available expirations
        group.MapGet(UiApiRoutes.OptionsExpirations, async (string underlyingSymbol, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(underlyingSymbol))
                return ValidationError("underlyingSymbol", "Underlying symbol is required.", jsonOptions);

            var service = ctx.RequestServices.GetService<OptionsChainService>();
            if (service is null)
                return ServiceUnavailableError("OptionsChainService", jsonOptions);

            var expirations = await service.GetExpirationsAsync(underlyingSymbol, ct);

            return Results.Json(new OptionsExpirationsResponse(
                UnderlyingSymbol: underlyingSymbol,
                Expirations: expirations,
                Count: expirations.Count,
                Timestamp: DateTimeOffset.UtcNow), jsonOptions);
        })
        .WithName("GetOptionsExpirations")
        .Produces(200)
        .Produces(400)
        .Produces(503);

        // GET /api/options/strikes/{underlyingSymbol}/{expiration} — available strikes
        group.MapGet(UiApiRoutes.OptionsStrikes, async (string underlyingSymbol, string expiration, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(underlyingSymbol))
                return ValidationError("underlyingSymbol", "Underlying symbol is required.", jsonOptions);

            var service = ctx.RequestServices.GetService<OptionsChainService>();
            if (service is null)
                return ServiceUnavailableError("OptionsChainService", jsonOptions);

            if (!DateOnly.TryParse(expiration, out var expirationDate))
                return ValidationError("expiration", "Invalid expiration date format. Use yyyy-MM-dd.", jsonOptions);

            var strikes = await service.GetStrikesAsync(underlyingSymbol, expirationDate, ct);

            return Results.Json(new OptionsStrikesResponse(
                UnderlyingSymbol: underlyingSymbol,
                Expiration: expirationDate,
                Strikes: strikes,
                Count: strikes.Count,
                Timestamp: DateTimeOffset.UtcNow), jsonOptions);
        })
        .WithName("GetOptionsStrikes")
        .Produces(200)
        .Produces(400)
        .Produces(503);

        // GET /api/options/chains/{underlyingSymbol} — option chain snapshot
        group.MapGet(UiApiRoutes.OptionsChains, async (string underlyingSymbol, string? expiration, int? strikeRange, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(underlyingSymbol))
                return ValidationError("underlyingSymbol", "Underlying symbol is required.", jsonOptions);

            var service = ctx.RequestServices.GetService<OptionsChainService>();
            if (service is null)
                return ServiceUnavailableError("OptionsChainService", jsonOptions);

            // If expiration is specified, fetch that specific chain
            if (!string.IsNullOrWhiteSpace(expiration))
            {
                if (!DateOnly.TryParse(expiration, out var expirationDate))
                    return ValidationError("expiration", "Invalid expiration date format. Use yyyy-MM-dd.", jsonOptions);

                // Try cache first, then fetch from provider
                var chain = service.GetCachedChain(underlyingSymbol, expirationDate)
                    ?? await service.FetchChainSnapshotAsync(underlyingSymbol, expirationDate, strikeRange, ct);

                if (chain is null)
                {
                    return Results.Json(
                        ErrorResponse.NotFound("OptionChain", $"{underlyingSymbol}/{expiration}"),
                        jsonOptions,
                        statusCode: StatusCodes.Status404NotFound);
                }

                return Results.Json(MapChainToResponse(chain), jsonOptions);
            }

            // Otherwise return all cached chains for this underlying
            var chains = service.GetCachedChainsForUnderlying(underlyingSymbol);
            if (chains.Count == 0)
            {
                return Results.Json(
                    ErrorResponse.NotFound("OptionChain", underlyingSymbol),
                    jsonOptions,
                    statusCode: StatusCodes.Status404NotFound);
            }

            var responses = chains.Select(MapChainToResponse).ToList();
            return Results.Json(responses, jsonOptions);
        })
        .WithName("GetOptionsChains")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .Produces(503);

        // GET /api/options/quotes/{underlyingSymbol} — all option quotes for an underlying
        group.MapGet(UiApiRoutes.OptionsQuotesByUnderlying, (string underlyingSymbol, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(underlyingSymbol))
                return ValidationError("underlyingSymbol", "Underlying symbol is required.", jsonOptions);

            var collector = ctx.RequestServices.GetService<OptionDataCollector>();
            if (collector is null)
                return ServiceUnavailableError("OptionDataCollector", jsonOptions);

            var quotes = collector.GetQuotesForUnderlying(underlyingSymbol);
            var dtos = quotes.Select(MapQuoteToDto).ToList();

            return Results.Json(dtos, jsonOptions);
        })
        .WithName("GetOptionQuotesByUnderlying")
        .Produces(200)
        .Produces(400)
        .Produces(503);

        // GET /api/options/summary — option data summary
        group.MapGet(UiApiRoutes.OptionsSummary, (HttpContext ctx) =>
        {
            var service = ctx.RequestServices.GetService<OptionsChainService>();
            if (service is null)
                return ServiceUnavailableError("OptionsChainService", jsonOptions);

            var summary = service.GetSummary();

            return Results.Json(new OptionsSummaryResponse(
                TrackedContracts: summary.TrackedContracts,
                TrackedChains: summary.TrackedChains,
                TrackedUnderlyings: summary.TrackedUnderlyings,
                ContractsWithGreeks: summary.ContractsWithGreeks,
                ContractsWithOpenInterest: summary.ContractsWithOpenInterest,
                ProviderAvailable: service.IsProviderAvailable,
                Timestamp: DateTimeOffset.UtcNow), jsonOptions);
        })
        .WithName("GetOptionsSummary")
        .Produces(200)
        .Produces(503);

        // GET /api/options/underlyings — tracked underlyings
        group.MapGet(UiApiRoutes.OptionsTrackedUnderlyings, (HttpContext ctx) =>
        {
            var collector = ctx.RequestServices.GetService<OptionDataCollector>();
            if (collector is null)
                return ServiceUnavailableError("OptionDataCollector", jsonOptions);

            var underlyings = collector.GetTrackedUnderlyings();

            return Results.Json(new
            {
                underlyings,
                count = underlyings.Count,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetTrackedUnderlyings")
        .Produces(200)
        .Produces(503);

        // POST /api/options/refresh — trigger a chain data refresh
        group.MapPost(UiApiRoutes.OptionsRefresh, async (OptionsRefreshRequest? request, HttpContext ctx, CancellationToken ct) =>
        {
            var service = ctx.RequestServices.GetService<OptionsChainService>();
            if (service is null)
                return ServiceUnavailableError("OptionsChainService", jsonOptions);

            if (!service.IsProviderAvailable)
            {
                return Results.Json(
                    ErrorResponse.ServiceUnavailable("OptionsChainProvider", "No options chain provider configured"),
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (request?.UnderlyingSymbol is not null && request?.Expiration is not null)
            {
                if (string.IsNullOrWhiteSpace(request.UnderlyingSymbol))
                    return ValidationError("underlyingSymbol", "Underlying symbol cannot be empty.", jsonOptions);

                if (!DateOnly.TryParse(request.Expiration, out var expDate))
                    return ValidationError("expiration", "Invalid expiration date format. Use yyyy-MM-dd.", jsonOptions);

                var chain = await service.FetchChainSnapshotAsync(
                    request.UnderlyingSymbol, expDate, request.StrikeRange, ct);

                return Results.Json(chain, jsonOptions);
            }

            return Results.Json(
                ErrorResponse.Validation("Specify underlyingSymbol and expiration to refresh a specific chain"),
                jsonOptions,
                statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("RefreshOptionsData")
        .Produces(200)
        .Produces(400)
        .Produces(503);
    }

    private static IResult ServiceUnavailableError(string serviceName, JsonSerializerOptions jsonOptions)
    {
        return Results.Json(
            ErrorResponse.ServiceUnavailable(serviceName),
            jsonOptions,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult ValidationError(string field, string message, JsonSerializerOptions jsonOptions)
    {
        return Results.Json(
            ErrorResponse.Validation(message, new[] { new FieldError(field, message) }),
            jsonOptions,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static OptionsChainResponse MapChainToResponse(OptionChainSnapshot chain)
    {
        return new OptionsChainResponse(
            UnderlyingSymbol: chain.UnderlyingSymbol,
            UnderlyingPrice: chain.UnderlyingPrice,
            Expiration: chain.Expiration,
            DaysToExpiration: chain.DaysToExpiration,
            InstrumentType: chain.InstrumentType.ToString(),
            AtTheMoneyStrike: chain.AtTheMoneyStrike,
            PutCallVolumeRatio: chain.PutCallVolumeRatio,
            PutCallOpenInterestRatio: chain.PutCallOpenInterestRatio,
            Calls: chain.Calls.Select(MapQuoteToDto).ToList(),
            Puts: chain.Puts.Select(MapQuoteToDto).ToList(),
            TotalContracts: chain.TotalContracts,
            Timestamp: chain.Timestamp);
    }

    private static OptionQuoteDto MapQuoteToDto(OptionQuote q)
    {
        return new OptionQuoteDto(
            Symbol: q.Symbol,
            UnderlyingSymbol: q.Contract.UnderlyingSymbol,
            Strike: q.Contract.Strike,
            Right: q.Contract.Right.ToString(),
            Expiration: q.Contract.Expiration,
            Style: q.Contract.Style.ToString(),
            BidPrice: q.BidPrice,
            BidSize: q.BidSize,
            AskPrice: q.AskPrice,
            AskSize: q.AskSize,
            LastPrice: q.LastPrice,
            MidPrice: q.MidPrice,
            Spread: q.Spread,
            UnderlyingPrice: q.UnderlyingPrice,
            ImpliedVolatility: q.ImpliedVolatility,
            Delta: q.Delta,
            Gamma: q.Gamma,
            Theta: q.Theta,
            Vega: q.Vega,
            OpenInterest: q.OpenInterest,
            Volume: q.Volume,
            IsInTheMoney: q.IsInTheMoney,
            Moneyness: q.Moneyness,
            NotionalValue: q.NotionalValue,
            SequenceNumber: q.SequenceNumber,
            Source: q.Source,
            Timestamp: q.Timestamp);
    }
}
