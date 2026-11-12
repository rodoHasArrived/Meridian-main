using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Meridian.Contracts.Options;

/// <summary>Canonical matching and format validation only. Never computes option margin or permission.</summary>
public static partial class OptionAssessmentValidation
{
    public const int MaxLegs = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [GeneratedRegex(@"^-?\d+(?:\.\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalLiteral();

    public static string ExactDecimal(string? value, string name, bool positive = false)
    {
        if (value is null || !DecimalLiteral().IsMatch(value.Trim())
            || !decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) || positive && amount <= 0m)
            throw new ArgumentException($"{name} must be an exact{(positive ? " positive" : "")} decimal string.");
        var canonical = amount.ToString("0.############################", CultureInfo.InvariantCulture);
        var literal = value.Trim(); var negative = literal.StartsWith('-');
        if (negative) literal = literal[1..];
        var parts = literal.Split('.'); var whole = parts[0].TrimStart('0');
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        whole = whole.Length == 0 ? "0" : whole;
        var exact = (negative && (whole != "0" || fraction.Length > 0) ? "-" : string.Empty)
            + whole + (fraction.Length > 0 ? "." + fraction : string.Empty);
        if (canonical != exact) throw new ArgumentException($"{name} exceeds exact decimal precision; rounding is not allowed.");
        return canonical;
    }

    public static BrokerOptionAssessment Normalize(BrokerOptionAssessment value, string? legsCsv = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.SchemaVersion != "1") throw new ArgumentException("Assessment schemaVersion must be 1.");
        if (value.FundAccountId == Guid.Empty) throw new ArgumentException("An owned fund account is required.");
        if (value.AssessedAt == default || value.AssessedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Broker assessment time must be an explicit UTC timestamp.");
        var basket = value.Basket ?? throw new ArgumentException("Basket is required.");
        var legs = legsCsv is null ? basket.Legs : ParseLegsCsv(legsCsv);
        if (legs is null || legs.Count is < 1 or > MaxLegs)
            throw new ArgumentException($"A basket must contain 1–{MaxLegs} explicit option or equity legs.");
        var normalizedLegs = legs.Select(NormalizeLeg).OrderBy(leg => leg.LegId, StringComparer.Ordinal).ToArray();
        if (normalizedLegs.Select(leg => leg.LegId).Distinct(StringComparer.Ordinal).Count() != legs.Count)
            throw new ArgumentException("Leg IDs must be unique.");
        var orderType = Choice(basket.OrderType, "orderType", "Limit", "Market");
        if (orderType == "Limit" && basket.LimitPrice is null) throw new ArgumentException("Limit baskets require a limit price.");
        var price = basket.LimitPrice is null ? null : ExactDecimal(basket.LimitPrice, "limitPrice");
        if (price is not null && decimal.Parse(price, CultureInfo.InvariantCulture) < 0m)
            throw new ArgumentException("Use a nonnegative limit price and an explicit debit/credit price effect.");
        if (orderType == "Market" && price is not null) throw new ArgumentException("Market baskets cannot have a limit price.");
        var warnings = value.Warnings?.Select(warning => Required(warning, "warning", 2000)).ToArray() ?? [];
        if (warnings.Length > 100) throw new ArgumentException("A maximum of 100 broker warnings may be imported.");
        var hasMargin = new[] { value.InitialMarginBefore, value.InitialMarginChange, value.InitialMarginAfter,
            value.MaintenanceMarginBefore, value.MaintenanceMarginChange, value.MaintenanceMarginAfter, value.ExcessLiquidityAfter }
            .Any(amount => amount is not null);
        var marginCurrency = value.MarginCurrency is not null || hasMargin ? Currency(value.MarginCurrency) : null;
        var cashCurrency = value.CashImpactCurrency is not null || value.CashImpact is not null ? Currency(value.CashImpactCurrency) : null;
        return value with
        {
            ProviderId = Required(value.ProviderId, "providerId", 128),
            ExternalAccountId = Required(value.ExternalAccountId, "externalAccountId", 256),
            BrokerReference = Required(value.BrokerReference, "brokerReference", 512),
            Outcome = Choice(value.Outcome, "outcome", "Approved", "Conditional", "Rejected", "NotReported"),
            RawBrokerStatus = Required(value.RawBrokerStatus, "rawBrokerStatus", 2000),
            Basket = basket with
            {
                Quantity = ExactDecimal(basket.Quantity, "quantity", true), OrderType = orderType,
                PriceEffect = Choice(basket.PriceEffect, "priceEffect", "Debit", "Credit", "Even"),
                LimitPrice = price, Currency = Currency(basket.Currency), Legs = normalizedLegs
            },
            InitialMarginBefore = Amount(value.InitialMarginBefore), InitialMarginChange = Amount(value.InitialMarginChange),
            InitialMarginAfter = Amount(value.InitialMarginAfter), MaintenanceMarginBefore = Amount(value.MaintenanceMarginBefore),
            MaintenanceMarginChange = Amount(value.MaintenanceMarginChange), MaintenanceMarginAfter = Amount(value.MaintenanceMarginAfter),
            ExcessLiquidityAfter = Amount(value.ExcessLiquidityAfter), CashImpact = Amount(value.CashImpact),
            PermissionEvidence = Optional(value.PermissionEvidence), RestrictionEvidence = Optional(value.RestrictionEvidence),
            Warnings = warnings, MarginCurrency = marginCurrency, CashImpactCurrency = cashCurrency
        };
    }

    public static string BasketFingerprint(BrokerOptionAssessment assessment) => Digest(JsonSerializer.SerializeToUtf8Bytes(
        new { assessment.FundAccountId, assessment.ProviderId, assessment.ExternalAccountId, assessment.Basket }, Json));

    public static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static byte[] CanonicalBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    private static OptionAssessmentLeg NormalizeLeg(OptionAssessmentLeg value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var kind = Choice(value.InstrumentType, "instrumentType", "Option", "Equity");
        var result = value with
        {
            LegId = Required(value.LegId, "legId", 128), InstrumentType = kind,
            Symbol = Required(value.Symbol, "symbol", 256), Currency = Currency(value.Currency),
            Side = Choice(value.Side, "side", "Buy", "Sell"),
            PositionIntent = Choice(value.PositionIntent, "positionIntent", "Open", "Close"),
            Ratio = ExactDecimal(value.Ratio, "ratio", true),
            Exchange = Optional(value.Exchange), Deliverable = Optional(value.Deliverable)
        };
        if (kind == "Option")
        {
            if (!DateOnly.TryParseExact(value.Expiration, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
                throw new ArgumentException("Option legs require an exact expiration date (yyyy-MM-dd).");
            result = result with
            {
                UnderlyingSymbol = Required(value.UnderlyingSymbol, "underlyingSymbol", 256),
                Right = Choice(value.Right, "right", "Call", "Put"), Expiration = expiry.ToString("yyyy-MM-dd"),
                Strike = ExactDecimal(value.Strike, "strike", true), Multiplier = ExactDecimal(value.Multiplier, "multiplier", true),
                Deliverable = Required(value.Deliverable, "deliverable", 2000)
            };
        }
        else if (value.Right is not null || value.Expiration is not null || value.Strike is not null || value.Multiplier is not null)
            throw new ArgumentException("Equity legs cannot carry option-specific identity fields.");
        return result;
    }

    private static string? Amount(string? value) => value is null ? null : ExactDecimal(value, "reported amount");
    private static string Currency(string? value)
    {
        var text = Required(value, "currency", 3).ToUpperInvariant();
        if (text.Length != 3 || text.Any(c => c is < 'A' or > 'Z')) throw new ArgumentException("Currency requires a three-letter code.");
        return text;
    }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : Required(value, "text", 2000);
    private static string Required(string? value, string name, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new ArgumentException($"{name} is required and must be at most {maximum} characters.");
        return value.Trim();
    }
    private static string Choice(string? value, string name, params string[] choices) =>
        choices.FirstOrDefault(choice => string.Equals(choice, value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"{name} must be one of {string.Join(", ", choices)}.");

    public static IReadOnlyList<OptionAssessmentLeg> ParseLegsCsv(string csv)
    {
        if (csv.Length > 256_000) throw new ArgumentException("Leg CSV exceeds 256 KB.");
        var rows = ReadCsv(csv);
        var headers = new[] { "legId", "instrumentType", "symbol", "side", "positionIntent", "ratio", "currency",
            "underlyingSymbol", "right", "expiration", "strike", "multiplier", "exchange", "deliverable" };
        if (rows.Count == 0 || !rows[0].SequenceEqual(headers, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"CSV header must be exactly: {string.Join(',', headers)}");
        return rows.Skip(1).Select((row, i) =>
        {
            if (row.Length != headers.Length) throw new ArgumentException($"CSV row {i + 2} requires {headers.Length} fields.");
            string? Cell(int n) => string.IsNullOrWhiteSpace(row[n]) ? null : row[n].Trim();
            return new OptionAssessmentLeg(row[0], row[1], row[2], row[3], row[4], row[5], row[6],
                Cell(7), Cell(8), Cell(9), Cell(10), Cell(11), Cell(12), Cell(13));
        }).ToArray();
    }

    private static List<string[]> ReadCsv(string csv)
    {
        var rows = new List<string[]>(); var cells = new List<string>(); var cell = new StringBuilder();
        var quoted = false; var closedQuote = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') { quoted = false; closedQuote = true; }
                else cell.Append(c);
                continue;
            }
            if (c == '"')
            {
                if (cell.Length > 0 || closedQuote) throw new ArgumentException("Malformed CSV quote.");
                quoted = true; continue;
            }
            if (c is ',' or '\r' or '\n')
            {
                cells.Add(cell.ToString()); cell.Clear(); closedQuote = false;
                if (c != ',')
                {
                    if (cells.Count != 1 || cells[0].Length > 0) rows.Add(cells.ToArray());
                    cells.Clear(); if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                }
            }
            else
            {
                if (closedQuote) throw new ArgumentException("Unexpected text after a quoted CSV field.");
                cell.Append(c);
            }
        }
        if (quoted) throw new ArgumentException("Unclosed CSV quote.");
        if (cell.Length > 0 || cells.Count > 0 || closedQuote) { cells.Add(cell.ToString()); rows.Add(cells.ToArray()); }
        return rows;
    }
}
