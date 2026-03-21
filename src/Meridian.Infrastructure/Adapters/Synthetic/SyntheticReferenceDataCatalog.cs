using Meridian.Application.Config;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.Adapters.Synthetic;

internal static class SyntheticReferenceDataCatalog
{
    private static readonly IReadOnlyDictionary<string, SyntheticSymbolProfile> Profiles =
        new Dictionary<string, SyntheticSymbolProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new(
                Symbol: "AAPL",
                Name: "Apple Inc.",
                Exchange: "XNAS",
                AssetType: "Stock",
                Sector: "Technology",
                Industry: "Consumer Electronics",
                Country: "US",
                Currency: "USD",
                BasePrice: 187.35m,
                AverageDailyVolume: 58_000_000,
                Beta: 1.18m,
                ReferenceData: new SymbolDetails(
                    Symbol: "AAPL",
                    Name: "Apple Inc.",
                    Description: "Synthetic reference profile for a mega-cap hardware and services issuer.",
                    Exchange: "XNAS",
                    AssetType: "Stock",
                    Sector: "Technology",
                    Industry: "Consumer Electronics",
                    Country: "US",
                    Currency: "USD",
                    MarketCap: 2_900_000_000_000m,
                    AverageVolume: 58_000_000,
                    Week52High: 205.22m,
                    Week52Low: 164.08m,
                    LastPrice: 187.35m,
                    WebUrl: "https://www.apple.com",
                    IpoDate: new DateOnly(1980, 12, 12),
                    PaysDividend: true,
                    DividendYield: 0.54m,
                    PeRatio: 30.4m,
                    SharesOutstanding: 15_400_000_000,
                    Figi: "BBG000B9XRY4",
                    CompositeFigi: "BBG000B9XRY4",
                    Isin: "US0378331005",
                    Cusip: "037833100",
                    Source: "synthetic",
                    LastUpdated: DateTimeOffset.UtcNow),
                Dividends: new[]
                {
                    new DividendInfo("AAPL", new DateOnly(2024, 2, 9), new DateOnly(2024, 2, 15), new DateOnly(2024, 2, 12), 0.24m, Source: "synthetic"),
                    new DividendInfo("AAPL", new DateOnly(2024, 5, 10), new DateOnly(2024, 5, 16), new DateOnly(2024, 5, 13), 0.25m, Source: "synthetic"),
                    new DividendInfo("AAPL", new DateOnly(2024, 8, 9), new DateOnly(2024, 8, 15), new DateOnly(2024, 8, 12), 0.25m, Source: "synthetic"),
                    new DividendInfo("AAPL", new DateOnly(2024, 11, 8), new DateOnly(2024, 11, 14), new DateOnly(2024, 11, 11), 0.25m, Source: "synthetic")
                },
                Splits: Array.Empty<SplitInfo>()),
            ["MSFT"] = new(
                Symbol: "MSFT",
                Name: "Microsoft Corporation",
                Exchange: "XNAS",
                AssetType: "Stock",
                Sector: "Technology",
                Industry: "Software Infrastructure",
                Country: "US",
                Currency: "USD",
                BasePrice: 417.80m,
                AverageDailyVolume: 26_500_000,
                Beta: 1.04m,
                ReferenceData: new SymbolDetails(
                    Symbol: "MSFT",
                    Name: "Microsoft Corporation",
                    Description: "Synthetic reference profile for a cloud and enterprise software issuer.",
                    Exchange: "XNAS",
                    AssetType: "Stock",
                    Sector: "Technology",
                    Industry: "Software Infrastructure",
                    Country: "US",
                    Currency: "USD",
                    MarketCap: 3_100_000_000_000m,
                    AverageVolume: 26_500_000,
                    Week52High: 430.82m,
                    Week52Low: 332.40m,
                    LastPrice: 417.80m,
                    WebUrl: "https://www.microsoft.com",
                    IpoDate: new DateOnly(1986, 3, 13),
                    PaysDividend: true,
                    DividendYield: 0.72m,
                    PeRatio: 35.1m,
                    SharesOutstanding: 7_440_000_000,
                    Figi: "BBG000BPH459",
                    CompositeFigi: "BBG000BPH459",
                    Isin: "US5949181045",
                    Cusip: "594918104",
                    Source: "synthetic",
                    LastUpdated: DateTimeOffset.UtcNow),
                Dividends: new[]
                {
                    new DividendInfo("MSFT", new DateOnly(2024, 2, 14), new DateOnly(2024, 3, 14), new DateOnly(2024, 2, 15), 0.75m, Source: "synthetic"),
                    new DividendInfo("MSFT", new DateOnly(2024, 5, 15), new DateOnly(2024, 6, 13), new DateOnly(2024, 5, 16), 0.75m, Source: "synthetic"),
                    new DividendInfo("MSFT", new DateOnly(2024, 8, 15), new DateOnly(2024, 9, 12), new DateOnly(2024, 8, 16), 0.75m, Source: "synthetic"),
                    new DividendInfo("MSFT", new DateOnly(2024, 11, 21), new DateOnly(2024, 12, 12), new DateOnly(2024, 11, 22), 0.83m, Source: "synthetic")
                },
                Splits: Array.Empty<SplitInfo>()),
            ["SPY"] = new(
                Symbol: "SPY",
                Name: "SPDR S&P 500 ETF Trust",
                Exchange: "ARCX",
                AssetType: "ETF",
                Sector: "Multi-Sector",
                Industry: "Large Blend ETF",
                Country: "US",
                Currency: "USD",
                BasePrice: 512.42m,
                AverageDailyVolume: 92_000_000,
                Beta: 1.00m,
                ReferenceData: new SymbolDetails(
                    Symbol: "SPY",
                    Name: "SPDR S&P 500 ETF Trust",
                    Description: "Synthetic reference profile for a broad-market index ETF.",
                    Exchange: "ARCX",
                    AssetType: "ETF",
                    Sector: "Multi-Sector",
                    Industry: "Large Blend ETF",
                    Country: "US",
                    Currency: "USD",
                    MarketCap: 490_000_000_000m,
                    AverageVolume: 92_000_000,
                    Week52High: 525.61m,
                    Week52Low: 410.12m,
                    LastPrice: 512.42m,
                    WebUrl: "https://www.ssga.com",
                    IpoDate: new DateOnly(1993, 1, 22),
                    PaysDividend: true,
                    DividendYield: 1.32m,
                    PeRatio: null,
                    SharesOutstanding: 960_000_000,
                    Figi: "BBG000BDTBL9",
                    CompositeFigi: "BBG000BDTBL9",
                    Isin: "US78462F1030",
                    Cusip: "78462F103",
                    Source: "synthetic",
                    LastUpdated: DateTimeOffset.UtcNow),
                Dividends: new[]
                {
                    new DividendInfo("SPY", new DateOnly(2024, 3, 15), new DateOnly(2024, 4, 30), new DateOnly(2024, 3, 18), 1.58m, Source: "synthetic"),
                    new DividendInfo("SPY", new DateOnly(2024, 6, 21), new DateOnly(2024, 7, 31), new DateOnly(2024, 6, 24), 1.76m, Source: "synthetic"),
                    new DividendInfo("SPY", new DateOnly(2024, 9, 20), new DateOnly(2024, 10, 31), new DateOnly(2024, 9, 23), 1.84m, Source: "synthetic"),
                    new DividendInfo("SPY", new DateOnly(2024, 12, 20), new DateOnly(2025, 1, 31), new DateOnly(2024, 12, 23), 1.97m, Source: "synthetic")
                },
                Splits: Array.Empty<SplitInfo>()),
            ["NVDA"] = new(
                Symbol: "NVDA",
                Name: "NVIDIA Corporation",
                Exchange: "XNAS",
                AssetType: "Stock",
                Sector: "Technology",
                Industry: "Semiconductors",
                Country: "US",
                Currency: "USD",
                BasePrice: 118.42m,
                AverageDailyVolume: 310_000_000,
                Beta: 1.72m,
                ReferenceData: new SymbolDetails(
                    Symbol: "NVDA",
                    Name: "NVIDIA Corporation",
                    Description: "Synthetic reference profile for a high-beta semiconductor issuer.",
                    Exchange: "XNAS",
                    AssetType: "Stock",
                    Sector: "Technology",
                    Industry: "Semiconductors",
                    Country: "US",
                    Currency: "USD",
                    MarketCap: 2_850_000_000_000m,
                    AverageVolume: 310_000_000,
                    Week52High: 140.76m,
                    Week52Low: 47.32m,
                    LastPrice: 118.42m,
                    WebUrl: "https://www.nvidia.com",
                    IpoDate: new DateOnly(1999, 1, 22),
                    PaysDividend: true,
                    DividendYield: 0.03m,
                    PeRatio: 63.8m,
                    SharesOutstanding: 24_600_000_000,
                    Figi: "BBG000BBJQV0",
                    CompositeFigi: "BBG000BBJQV0",
                    Isin: "US67066G1040",
                    Cusip: "67066G104",
                    Source: "synthetic",
                    LastUpdated: DateTimeOffset.UtcNow),
                Dividends: new[]
                {
                    new DividendInfo("NVDA", new DateOnly(2024, 3, 5), new DateOnly(2024, 3, 27), new DateOnly(2024, 3, 6), 0.004m, Source: "synthetic"),
                    new DividendInfo("NVDA", new DateOnly(2024, 6, 11), new DateOnly(2024, 6, 28), new DateOnly(2024, 6, 12), 0.01m, Source: "synthetic"),
                    new DividendInfo("NVDA", new DateOnly(2024, 9, 12), new DateOnly(2024, 10, 3), new DateOnly(2024, 9, 13), 0.01m, Source: "synthetic"),
                    new DividendInfo("NVDA", new DateOnly(2024, 12, 5), new DateOnly(2024, 12, 27), new DateOnly(2024, 12, 6), 0.01m, Source: "synthetic")
                },
                Splits: new[]
                {
                    new SplitInfo("NVDA", new DateOnly(2024, 6, 10), 1m, 10m, Source: "synthetic")
                }),
            ["XOM"] = new(
                Symbol: "XOM",
                Name: "Exxon Mobil Corporation",
                Exchange: "XNYS",
                AssetType: "Stock",
                Sector: "Energy",
                Industry: "Integrated Oil & Gas",
                Country: "US",
                Currency: "USD",
                BasePrice: 108.17m,
                AverageDailyVolume: 19_500_000,
                Beta: 0.86m,
                ReferenceData: new SymbolDetails(
                    Symbol: "XOM",
                    Name: "Exxon Mobil Corporation",
                    Description: "Synthetic reference profile for a large-cap dividend-paying energy issuer.",
                    Exchange: "XNYS",
                    AssetType: "Stock",
                    Sector: "Energy",
                    Industry: "Integrated Oil & Gas",
                    Country: "US",
                    Currency: "USD",
                    MarketCap: 462_000_000_000m,
                    AverageVolume: 19_500_000,
                    Week52High: 123.75m,
                    Week52Low: 95.77m,
                    LastPrice: 108.17m,
                    WebUrl: "https://corporate.exxonmobil.com",
                    IpoDate: new DateOnly(1920, 3, 1),
                    PaysDividend: true,
                    DividendYield: 3.29m,
                    PeRatio: 14.2m,
                    SharesOutstanding: 4_150_000_000,
                    Figi: "BBG000GZQ728",
                    CompositeFigi: "BBG000GZQ728",
                    Isin: "US30231G1022",
                    Cusip: "30231G102",
                    Source: "synthetic",
                    LastUpdated: DateTimeOffset.UtcNow),
                Dividends: new[]
                {
                    new DividendInfo("XOM", new DateOnly(2024, 2, 14), new DateOnly(2024, 3, 11), new DateOnly(2024, 2, 15), 0.95m, Source: "synthetic"),
                    new DividendInfo("XOM", new DateOnly(2024, 5, 14), new DateOnly(2024, 6, 10), new DateOnly(2024, 5, 15), 0.95m, Source: "synthetic"),
                    new DividendInfo("XOM", new DateOnly(2024, 8, 15), new DateOnly(2024, 9, 10), new DateOnly(2024, 8, 16), 0.99m, Source: "synthetic"),
                    new DividendInfo("XOM", new DateOnly(2024, 11, 14), new DateOnly(2024, 12, 10), new DateOnly(2024, 11, 15), 0.99m, Source: "synthetic")
                },
                Splits: Array.Empty<SplitInfo>())
        };

    public static IReadOnlyList<SyntheticSymbolProfile> GetUniverse(SyntheticMarketDataConfig? config)
    {
        if (config?.UniverseSymbols is not { Length: > 0 })
            return Profiles.Values.OrderBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).ToArray();

        return config.UniverseSymbols
            .Select(GetProfile)
            .Where(p => p is not null)
            .Cast<SyntheticSymbolProfile>()
            .ToArray();
    }

    public static SyntheticSymbolProfile GetProfileOrDefault(string symbol)
        => GetProfile(symbol) ?? Profiles["SPY"] with { Symbol = symbol.ToUpperInvariant(), Name = $"Synthetic {symbol.ToUpperInvariant()}" };

    public static SyntheticSymbolProfile? GetProfile(string symbol)
        => Profiles.TryGetValue(symbol, out var profile) ? profile : null;

    public static IReadOnlyList<SymbolSearchResult> Search(string query, int limit)
    {
        query ??= string.Empty;
        var normalized = query.Trim();
        return Profiles.Values
            .Select(profile => new
            {
                Profile = profile,
                Score = GetMatchScore(profile, normalized)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Profile.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => new SymbolSearchResult(
                x.Profile.Symbol,
                x.Profile.Name,
                x.Profile.Exchange,
                x.Profile.AssetType,
                x.Profile.Country,
                x.Profile.Currency,
                Source: "synthetic",
                MatchScore: x.Score,
                Figi: x.Profile.ReferenceData.Figi,
                CompositeFigi: x.Profile.ReferenceData.CompositeFigi))
            .ToArray();
    }

    private static int GetMatchScore(SyntheticSymbolProfile profile, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 10;

        if (profile.Symbol.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 100;

        if (profile.Symbol.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 92;

        if (profile.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 80;

        if (profile.Sector.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 45;

        return 0;
    }
}

internal sealed record SyntheticSymbolProfile(
    string Symbol,
    string Name,
    string Exchange,
    string AssetType,
    string Sector,
    string Industry,
    string Country,
    string Currency,
    decimal BasePrice,
    long AverageDailyVolume,
    decimal Beta,
    SymbolDetails ReferenceData,
    IReadOnlyList<DividendInfo> Dividends,
    IReadOnlyList<SplitInfo> Splits);
