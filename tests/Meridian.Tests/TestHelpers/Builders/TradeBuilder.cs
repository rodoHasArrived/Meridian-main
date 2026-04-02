using Bogus;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Tests.TestHelpers.Builders;

/// <summary>
/// Fluent builder for <see cref="Trade"/> test instances.
/// All required fields default to realistic values so tests only specify what they care about.
/// </summary>
/// <example>
/// <code>
/// var trade = new TradeBuilder().ForSymbol("AAPL").AtPrice(150.50m).Build();
/// var trades = new TradeBuilder().ForSymbol("MSFT").CreateMany(10);
/// </code>
/// </example>
public sealed class TradeBuilder
{
    // Instance-level Faker so parallel test execution does not race on a shared static.
    private readonly Faker _faker = new();

    private string _symbol = "TEST";
    private decimal? _price;
    private long? _size;
    private AggressorSide _aggressor = AggressorSide.Unknown;
    private long _sequenceNumber = 0;
    private string? _streamId;
    private string? _venue;
    private DateTimeOffset? _timestamp;
    private string[]? _rawConditions;
    private CanonicalTradeCondition[]? _canonicalConditions;

    /// <summary>Sets the ticker symbol.</summary>
    public TradeBuilder ForSymbol(string symbol)
    {
        _symbol = symbol;
        return this;
    }

    /// <summary>Sets the execution price.</summary>
    public TradeBuilder AtPrice(decimal price)
    {
        _price = price;
        return this;
    }

    /// <summary>Sets the traded size (number of shares).</summary>
    public TradeBuilder WithSize(long size)
    {
        _size = size;
        return this;
    }

    /// <summary>Sets which side initiated the trade.</summary>
    public TradeBuilder WithAggressor(AggressorSide side)
    {
        _aggressor = side;
        return this;
    }

    /// <summary>Sets the sequence number.</summary>
    public TradeBuilder WithSequenceNumber(long sequenceNumber)
    {
        _sequenceNumber = sequenceNumber;
        return this;
    }

    /// <summary>Sets the data stream identifier.</summary>
    public TradeBuilder WithStreamId(string streamId)
    {
        _streamId = streamId;
        return this;
    }

    /// <summary>Sets the trading venue or exchange code (e.g. "XNAS", "XNYS").</summary>
    public TradeBuilder WithVenue(string venue)
    {
        _venue = venue;
        return this;
    }

    /// <summary>Sets the event timestamp.</summary>
    public TradeBuilder WithTimestamp(DateTimeOffset timestamp)
    {
        _timestamp = timestamp;
        return this;
    }

    /// <summary>Sets raw provider-specific condition codes.</summary>
    public TradeBuilder WithRawConditions(params string[] conditions)
    {
        _rawConditions = conditions;
        return this;
    }

    /// <summary>Sets canonical trade conditions.</summary>
    public TradeBuilder WithCanonicalConditions(params CanonicalTradeCondition[] conditions)
    {
        _canonicalConditions = conditions;
        return this;
    }

    /// <summary>Builds a single <see cref="Trade"/> using the configured values.</summary>
    public Trade Build()
    {
        return new Trade(
            Timestamp: _timestamp ?? DateTimeOffset.UtcNow,
            Symbol: _symbol,
            Price: _price ?? Math.Round((decimal)_faker.Random.Double(1, 500), 2),
            Size: _size ?? _faker.Random.Long(1, 10_000),
            Aggressor: _aggressor,
            SequenceNumber: _sequenceNumber,
            StreamId: _streamId,
            Venue: _venue,
            RawConditions: _rawConditions)
        {
            CanonicalConditions = _canonicalConditions
        };
    }

    /// <summary>
    /// Builds a sequence of <paramref name="count"/> trades at incrementing sequence numbers.
    /// Each trade has a slightly randomized price around the starting price.
    /// </summary>
    public IReadOnlyList<Trade> CreateMany(int count)
    {
        var trades = new List<Trade>(count);
        var basePrice = _price ?? Math.Round((decimal)_faker.Random.Double(50, 300), 2);
        var ts = _timestamp ?? DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            var price = Math.Round(basePrice * (decimal)(1.0 + _faker.Random.Double(-0.005, 0.005)), 2);
            if (price <= 0)
                price = basePrice;

            trades.Add(new Trade(
                Timestamp: ts.AddMilliseconds(i * 100),
                Symbol: _symbol,
                Price: price,
                Size: _size ?? _faker.Random.Long(1, 10_000),
                Aggressor: _faker.PickRandom<AggressorSide>(),
                SequenceNumber: _sequenceNumber + i,
                StreamId: _streamId,
                Venue: _venue,
                RawConditions: _rawConditions)
            {
                CanonicalConditions = _canonicalConditions
            });
        }

        return trades;
    }
}
