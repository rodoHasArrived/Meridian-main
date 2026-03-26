namespace Meridian.Benchmarks;

/// <summary>
/// Writes BenchmarkDotNet JSON results to a local history directory so developers
/// can compare benchmark runs offline without needing GitHub artifacts.
/// <para>
/// History is stored under <c>~/.meridian-benchmarks/</c> by default.
/// Override with the environment variable <c>MERIDIAN_BENCH_HISTORY_ROOT</c>.
/// </para>
/// <para>
/// This store is intentionally <b>not</b> connected to GitHub Releases or any remote
/// service. The CI 90-day artifact (see <c>benchmark.yml: Save baseline</c>) covers
/// cross-PR baseline comparison. This store is for local, iteration-speed developer use.
/// </para>
/// <para>
/// To compare a saved baseline against a new run, use:
/// <code>
/// python3 scripts/compare_benchmarks.py \
///   --baseline ~/.meridian-benchmarks/&lt;timestamp&gt;_&lt;class&gt;.json \
///   --current ./BenchmarkDotNet.Artifacts/current/results
/// </code>
/// </para>
/// </summary>
public sealed class BenchmarkResultStore
{
    /// <summary>
    /// Default history root directory.
    /// Overridable via the <c>MERIDIAN_BENCH_HISTORY_ROOT</c> environment variable.
    /// </summary>
    public static string DefaultHistoryRoot =>
        Environment.GetEnvironmentVariable("MERIDIAN_BENCH_HISTORY_ROOT")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".meridian-benchmarks");

    private readonly string _historyRoot;

    /// <param name="historyRoot">
    /// Directory to write history into.
    /// Defaults to <see cref="DefaultHistoryRoot"/> when <c>null</c>.
    /// </param>
    public BenchmarkResultStore(string? historyRoot = null)
    {
        _historyRoot = historyRoot ?? DefaultHistoryRoot;
    }

    /// <summary>
    /// Copies the BDN JSON result file at <paramref name="bdnJsonResultPath"/> into the
    /// history directory, named <c>{yyyyMMdd_HHmmss}_{benchmarkClass}.json</c>.
    /// Does nothing if <paramref name="bdnJsonResultPath"/> does not exist.
    /// </summary>
    /// <param name="benchmarkClass">
    /// Logical name for the benchmark class (used as the file name suffix and lookup key).
    /// </param>
    /// <param name="bdnJsonResultPath">
    /// Absolute path to the BDN-generated JSON result file.
    /// </param>
    public void Save(string benchmarkClass, string bdnJsonResultPath)
    {
        if (!File.Exists(bdnJsonResultPath))
            return;

        try
        {
            Directory.CreateDirectory(_historyRoot);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{timestamp}_{SanitizeClassName(benchmarkClass)}.json";
            var destination = Path.Combine(_historyRoot, fileName);
            File.Copy(bdnJsonResultPath, destination, overwrite: true);
        }
        catch (Exception ex)
        {
            // Storage failures are non-fatal — benchmarks should not stop running
            // because history persistence failed.
            Console.WriteLine($"[BenchmarkResultStore] Warning: could not save history for {benchmarkClass}: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the absolute path of the most recently saved result for
    /// <paramref name="benchmarkClass"/>, or <c>null</c> if no history entry exists.
    /// </summary>
    public string? GetLatestBaseline(string benchmarkClass)
    {
        if (!Directory.Exists(_historyRoot))
            return null;

        var sanitized = SanitizeClassName(benchmarkClass);
        return Directory
            .GetFiles(_historyRoot, $"*_{sanitized}.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }

    private static string SanitizeClassName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
}
