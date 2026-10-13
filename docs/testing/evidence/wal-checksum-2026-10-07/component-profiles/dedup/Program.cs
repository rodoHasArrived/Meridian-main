using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

// Full source snapshots differ only in class name, hex formatter, and the scratch-only
// reused SHA provider. Existing compiled dependencies avoid rebuilding the repo.
internal static class Program
{
    static int _sink;
    static async Task Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "dedup_profile_" + Guid.NewGuid().ToString("N"));
        var baseline = new BaselineDedupLedger(Path.Combine(root, "baseline"));
        var hex = new HexDedupLedger(Path.Combine(root, "hex"));
        var reuse = new ReuseDedupLedger(Path.Combine(root, "reuse"));
        try
        {
            Validate(baseline, hex, reuse);
            var timestamp = new DateTimeOffset(2024, 1, 15, 14, 30, 0, TimeSpan.Zero);
            MarketEvent[] fixtures = Enumerable.Range(0, 256).Select(i =>
                MarketEvent.Trade(timestamp.AddTicks(i), "AAPL", new Trade(timestamp.AddTicks(i), "AAPL",
                    174.53m + i / 100m, 100 + i, AggressorSide.Buy, 1234567 + i, "ALPACA", "XNAS"), "ALPACA")).ToArray();
            baseline.SeedCacheEntry(fixtures[0]); hex.SeedCacheEntry(fixtures[0]); reuse.SeedCacheEntry(fixtures[0]);
            Func<MarketEvent, string>[] functions = [baseline.ComputeKeyForBenchmark, hex.ComputeKeyForBenchmark, reuse.ComputeKeyForBenchmark];
            string[] labels = ["baseline", "span_hex", "span_hex_reused_SHA_scratch_only"];
            Console.WriteLine("pattern,implementation,repetition,iterations,mean_ns,allocated_bytes_per_event,sink");
            foreach (bool varied in new[] { false, true })
            {
                foreach (var function in functions) Run(function, fixtures, varied, 100_000);
                for (int rep = 1; rep <= 7; ++rep)
                {
                    // Rotate order to limit systematic drift between implementations.
                    for (int offset = 0; offset < functions.Length; ++offset)
                    {
                        int index = (rep + offset) % functions.Length;
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        long start = Stopwatch.GetTimestamp();
                        Run(functions[index], fixtures, varied, 100_000);
                        long elapsed = Stopwatch.GetTimestamp() - start;
                        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                        double nanos = elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / 100_000;
                        Console.WriteLine(FormattableString.Invariant($"{(varied ? "varied256" : "fixed")},{labels[index]},{rep},100000,{nanos:F3},{allocated / 100_000.0:F6},{_sink}"));
                    }
                }
            }
        }
        finally
        {
            await baseline.DisposeAsync(); await hex.DisposeAsync(); await reuse.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }
    static void Run(Func<MarketEvent, string> function, MarketEvent[] fixtures, bool varied, int count)
    {
        int sink = _sink;
        for (int i = 0; i < count; ++i)
        {
            string key = function(fixtures[varied ? i & 255 : 0]);
            sink = unchecked(sink + key[^1]);
        }
        _sink = sink;
    }
    static void Validate(BaselineDedupLedger baseline, HexDedupLedger hex, ReuseDedupLedger reuse)
    {
        var previous = CultureInfo.CurrentCulture;
        int checks = 0;
        try
        {
            foreach (string culture in new[] { "en-US", "fr-FR", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var timestamp = new DateTimeOffset(2024, 1, 3, 14, 30, 0, TimeSpan.FromHours(2));
                foreach (string venue in new[] { "XNAS", "東京🙂", string.Concat(Enumerable.Repeat("市場", 128)) })
                for (int i = 0; i < 32; ++i)
                {
                    var trade = new Trade(timestamp.AddTicks(i), "AAPL", 100.25m + i / 100m, 107 + i,
                        i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell, i, "TEST", venue);
                    var tradeEvent = MarketEvent.Trade(trade.Timestamp, "AAPL", trade, "TEST");
                    string tradeIdentity = FormattableString.Invariant($"{trade.Timestamp.Ticks}|{trade.Price}|{trade.Size}|{trade.Aggressor}|{venue}");
                    Assert(tradeEvent, Legacy("TEST:AAPL:Trade:", tradeIdentity));
                    var quote = new BboQuotePayload(timestamp.AddTicks(i), "AAPL", 100.25m + i / 100m, 133 + i,
                        100.50m + i / 100m, 150 + i, null, null, i, "TEST", venue);
                    var quoteEvent = MarketEvent.BboQuote(quote.Timestamp, "AAPL", quote, "TEST");
                    string quoteIdentity = FormattableString.Invariant($"{quote.Timestamp.Ticks}|{quote.BidPrice}|{quote.AskPrice}|{quote.BidSize}|{quote.AskSize}");
                    Assert(quoteEvent, Legacy("TEST:AAPL:BboQuote:", quoteIdentity));
                }
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Console.Error.WriteLine($"Independent invariant UTF8 SHA256/truncation oracle passed {checks} event comparisons across3cultures/Unicode/poolvenues for all3implementations.");
        void Assert(MarketEvent evt, string expected)
        {
            if (baseline.ComputeKeyForBenchmark(evt) != expected || hex.ComputeKeyForBenchmark(evt) != expected || reuse.ComputeKeyForBenchmark(evt) != expected)
                throw new Exception("Identity compatibility failure");
            ++checks;
        }
    }
    static string Legacy(string prefix, string identity) =>
        prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
}
