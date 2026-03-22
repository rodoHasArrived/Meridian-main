namespace Meridian.Infrastructure.CppTrader.Options;

/// <summary>
/// Runtime options for the external CppTrader integration layer.
/// </summary>
public sealed record CppTraderOptions
{
    public const string SectionName = "CppTrader";

    public bool Enabled { get; init; }

    public string? AdapterExecutablePath { get; init; }

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public int MaxConcurrentSessions { get; init; } = 4;

    public int PerSessionQueueCapacity { get; init; } = 1_024;

    public string NativeLogLevel { get; init; } = "Information";

    public CppTraderFeatureOptions Features { get; init; } = new();

    public IReadOnlyDictionary<string, CppTraderSymbolSpecification> Symbols { get; init; } =
        new Dictionary<string, CppTraderSymbolSpecification>(StringComparer.OrdinalIgnoreCase);
}

public sealed record CppTraderFeatureOptions
{
    public bool ExecutionEnabled { get; init; } = true;

    public bool ReplayEnabled { get; init; }

    public bool ItchIngestionEnabled { get; init; }
}

public sealed record CppTraderSymbolSpecification
{
    public required string Symbol { get; init; }

    public required int SymbolId { get; init; }

    public decimal TickSize { get; init; } = 0.01m;

    public decimal QuantityIncrement { get; init; } = 1m;

    public int PriceScale { get; init; } = 2;

    public decimal LotSize { get; init; } = 1m;

    public string? Venue { get; init; }

    public string SessionTimeZone { get; init; } = "UTC";
}
