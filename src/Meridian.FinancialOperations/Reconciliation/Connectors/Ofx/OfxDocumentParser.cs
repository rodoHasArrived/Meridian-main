using System.Text;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.Ofx;

/// <summary>
/// Tolerant OFX parser covering both OFX 1.x (SGML, unclosed leaf tags) and OFX 2.x (XML).
/// Aggregates of interest — bank transactions, ledger balances, investment buys/sells, and
/// investment positions — are flattened into per-entry tag dictionaries ("pseudo-columns")
/// so the same declarative mapping profiles used for CSV apply to OFX statements.
/// </summary>
public static class OfxDocumentParser
{
    /// <summary>Pseudo-column carrying the aggregate name (STMTTRN, INVBUY, POSSTOCK, ...).</summary>
    public const string AggregateColumn = "AGGREGATE";

    private static readonly HashSet<string> WrapperAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "STMTTRN",
        "LEDGERBAL",
        "BUYSTOCK", "SELLSTOCK", "BUYMF", "SELLMF", "BUYDEBT", "SELLDEBT", "BUYOPT", "SELLOPT", "BUYOTHER", "SELLOTHER",
        "INCOME", "REINVEST",
        "POSSTOCK", "POSMF", "POSDEBT", "POSOPT", "POSOTHER"
    };

    /// <summary>
    /// INVBUY/INVSELL are wrapped by BUYSTOCK/SELLSTOCK/... in spec-compliant files (where
    /// they are detail of the wrapper entry), but some exporters emit them bare — then each
    /// is its own entry.
    /// </summary>
    private static readonly HashSet<string> DetailAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "INVBUY", "INVSELL"
    };

    /// <summary>Tags that carry OFX timestamps and are normalized to their date component.</summary>
    private static readonly HashSet<string> DateTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "DTPOSTED", "DTTRADE", "DTSETTLE", "DTASOF", "DTAVAIL", "DTUSER", "DTPRICEASOF"
    };

    public static bool LooksLikeOfx(string content)
    {
        var head = content.Length > 512 ? content[..512] : content;
        return head.Contains("OFXHEADER", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<OFX>", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<OFX ", StringComparison.OrdinalIgnoreCase);
    }

    public static OfxDocument Parse(string content) =>
        Parse(content, int.MaxValue, int.MaxValue, out _);

    /// <summary>
    /// Parses an OFX document, refusing one that exceeds the ingress bounds instead of building it first.
    /// </summary>
    /// <remarks>
    /// Two bounds, because the parse has two allocation phases and a check after the second cannot undo
    /// the first. <paramref name="maxDepth"/> caps aggregate nesting, which also caps the recursion in
    /// <see cref="CollectEntries"/>: without it a deeply nested document overflows the stack, which no
    /// caller can catch. <paramref name="maxEntries"/> stops entry discovery at the bound. Aggregate
    /// nodes carry a separate, deliberately loose allocation cap derived from
    /// <paramref name="maxEntries"/>: entries are built from aggregates, so the node tree is materialized
    /// before any entry exists, and bounding only entries would let the tree grow unbounded first.
    /// </remarks>
    public static OfxDocument Parse(string content, int maxEntries, int maxDepth, out OfxParseBound bound)
    {
        bound = OfxParseBound.None;
        // Deliberately loose, and an allocation bound rather than an acceptance one: a spec-compliant
        // investment entry is a wrapper plus up to three nested detail aggregates (BUYSTOCK > INVBUY >
        // SECID/INVTRAN), so 8x the entry bound leaves roughly double the headroom a real statement
        // needs. Acceptance is decided by maxEntries; this only stops the node tree growing without
        // limit before any entry exists to count.
        var maxNodes = maxEntries > (int.MaxValue - 64) / 8 ? int.MaxValue : (maxEntries * 8) + 64;
        var nodes = 0;
        var root = new OfxNode("OFX-ROOT", null);
        var stack = new Stack<OfxNode>();
        stack.Push(root);

        var body = SkipSgmlHeader(content);
        var index = 0;
        while (index < body.Length)
        {
            var open = body.IndexOf('<', index);
            if (open < 0)
            {
                break;
            }

            var close = body.IndexOf('>', open + 1);
            if (close < 0)
            {
                break;
            }

            var rawTag = body[(open + 1)..close].Trim();
            index = close + 1;
            if (rawTag.Length == 0 || rawTag[0] is '?' or '!' || rawTag.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (rawTag[0] == '/')
            {
                var closingName = NormalizeTagName(rawTag[1..]);
                // Tolerant close: pop to the matching open aggregate if present; otherwise
                // it closed an SGML leaf and there is nothing on the stack to unwind.
                if (stack.Any(node => string.Equals(node.Name, closingName, StringComparison.OrdinalIgnoreCase)))
                {
                    while (stack.Count > 1 && !string.Equals(stack.Peek().Name, closingName, StringComparison.OrdinalIgnoreCase))
                    {
                        stack.Pop();
                    }

                    if (stack.Count > 1)
                    {
                        stack.Pop();
                    }
                }

                continue;
            }

            var name = NormalizeTagName(rawTag);
            var valueEnd = body.IndexOf('<', index);
            var value = (valueEnd < 0 ? body[index..] : body[index..valueEnd]).Trim();
            if (value.Length > 0)
            {
                stack.Peek().Leaves[name] = DecodeEntities(value);
            }
            else
            {
                if (stack.Count > maxDepth)
                {
                    bound = OfxParseBound.NestingTooDeep;
                    break;
                }

                if (++nodes > maxNodes)
                {
                    bound = OfxParseBound.TooManyEntries;
                    break;
                }

                var aggregate = new OfxNode(name, stack.Peek());
                stack.Peek().Children.Add(aggregate);
                stack.Push(aggregate);
            }
        }

        var entries = new List<IReadOnlyDictionary<string, string>>();
        var accountId = FindFirstLeaf(root, "ACCTID");
        if (bound == OfxParseBound.None && !CollectEntries(root, accountId, entries, maxEntries))
        {
            bound = OfxParseBound.TooManyEntries;
        }

        return new OfxDocument(accountId, entries);
    }

    private static bool IsEntryNode(OfxNode node)
    {
        if (WrapperAggregates.Contains(node.Name))
        {
            return true;
        }

        // Detail aggregates count as entries only when emitted bare (not inside a wrapper).
        return DetailAggregates.Contains(node.Name)
            && (node.Parent is null || !WrapperAggregates.Contains(node.Parent.Name));
    }

    // Returns false when the entry bound stopped the walk. One entry past the bound is collected so the
    // caller can tell "exactly at the bound" from "over it" by count alone.
    private static bool CollectEntries(
        OfxNode node,
        string? accountId,
        List<IReadOnlyDictionary<string, string>> entries,
        int maxEntries)
    {
        if (IsEntryNode(node))
        {
            var entry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AggregateColumn] = node.Name.ToUpperInvariant()
            };
            FlattenLeaves(node, entry);
            NormalizeEntry(entry, accountId);
            entries.Add(entry);
            if (entries.Count > maxEntries)
            {
                return false;
            }
        }

        // Keep walking children even below an entry aggregate so a malformed SGML file
        // with an unclosed entry (the next entry parses as a nested child) still yields
        // every entry instead of silently merging them.
        foreach (var child in node.Children)
        {
            if (!CollectEntries(child, accountId, entries, maxEntries))
            {
                return false;
            }
        }

        return true;
    }

    private static void FlattenLeaves(OfxNode node, IDictionary<string, string> entry)
    {
        foreach (var (tag, value) in node.Leaves)
        {
            entry.TryAdd(tag, value);
        }

        foreach (var child in node.Children)
        {
            // Children that are entries in their own right keep their tags to themselves.
            if (!IsEntryNode(child))
            {
                FlattenLeaves(child, entry);
            }
        }
    }

    /// <summary>
    /// Backfills the uniform pseudo-columns so one mapping profile covers every aggregate
    /// shape: TRNTYPE falls back to the aggregate name, DTPOSTED to trade/as-of dates,
    /// TRNAMT to totals/balances/market values, and dates lose their time component.
    /// </summary>
    private static void NormalizeEntry(Dictionary<string, string> entry, string? accountId)
    {
        foreach (var tag in DateTags)
        {
            if (entry.TryGetValue(tag, out var raw))
            {
                entry[tag] = NormalizeOfxDate(raw);
            }
        }

        if (!entry.ContainsKey("TRNTYPE"))
        {
            entry["TRNTYPE"] = entry[AggregateColumn];
        }

        if (!entry.ContainsKey("DTPOSTED"))
        {
            if (entry.TryGetValue("DTTRADE", out var tradeDate))
            {
                entry["DTPOSTED"] = tradeDate;
            }
            else if (entry.TryGetValue("DTASOF", out var asOfDate))
            {
                entry["DTPOSTED"] = asOfDate;
            }
            else if (entry.TryGetValue("DTPRICEASOF", out var priceAsOfDate))
            {
                entry["DTPOSTED"] = priceAsOfDate;
            }
        }

        if (!entry.ContainsKey("TRNAMT"))
        {
            if (entry.TryGetValue("TOTAL", out var total))
            {
                entry["TRNAMT"] = total;
            }
            else if (entry.TryGetValue("BALAMT", out var balance))
            {
                entry["TRNAMT"] = balance;
            }
            else if (entry.TryGetValue("MKTVAL", out var marketValue))
            {
                entry["TRNAMT"] = marketValue;
            }
        }

        if (!entry.ContainsKey("TICKER") && entry.TryGetValue("UNIQUEID", out var uniqueId))
        {
            entry["TICKER"] = uniqueId;
        }

        if (!entry.ContainsKey("ACCTID") && !string.IsNullOrWhiteSpace(accountId))
        {
            entry["ACCTID"] = accountId;
        }
    }

    /// <summary>OFX timestamps ("20260601120000.000[-5:EST]") reduce to their yyyyMMdd date.</summary>
    private static string NormalizeOfxDate(string value)
    {
        var trimmed = value.Trim();
        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        return digits >= 8 ? trimmed[..8] : trimmed;
    }

    private static string? FindFirstLeaf(OfxNode node, string tagName)
    {
        if (node.Leaves.TryGetValue(tagName, out var value))
        {
            return value;
        }

        foreach (var child in node.Children)
        {
            if (FindFirstLeaf(child, tagName) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string SkipSgmlHeader(string content)
    {
        var start = content.IndexOf('<');
        return start <= 0 ? content : content[start..];
    }

    private static string NormalizeTagName(string rawTag)
    {
        // XML tags may carry attributes; OFX tag names never contain spaces.
        var space = rawTag.IndexOf(' ');
        return (space < 0 ? rawTag : rawTag[..space]).Trim().ToUpperInvariant();
    }

    private static string DecodeEntities(string value)
    {
        if (!value.Contains('&', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value);
        builder.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'");
        return builder.ToString();
    }

    private sealed class OfxNode(string name, OfxNode? parent)
    {
        public string Name { get; } = name;
        public OfxNode? Parent { get; } = parent;
        public Dictionary<string, string> Leaves { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<OfxNode> Children { get; } = [];
    }
}

/// <summary>A parsed OFX file flattened into uniform per-entry tag dictionaries.</summary>
public sealed record OfxDocument(
    string? AccountId,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Entries);

/// <summary>Which ingress bound, if any, stopped an OFX parse before the document was complete.</summary>
public enum OfxParseBound
{
    /// <summary>The document was parsed in full.</summary>
    None = 0,

    /// <summary>Entry discovery or the aggregate allocation cap stopped the parse.</summary>
    TooManyEntries = 1,

    /// <summary>Aggregate nesting exceeded the depth bound.</summary>
    NestingTooDeep = 2,
}
