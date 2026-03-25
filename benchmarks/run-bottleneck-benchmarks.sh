#!/usr/bin/env bash
set -euo pipefail

# Bottleneck Benchmark Runner
# Runs targeted benchmarks to identify performance bottlenecks in the event pipeline.
#
# Usage:
#   ./benchmarks/run-bottleneck-benchmarks.sh              # Run all bottleneck benchmarks
#   ./benchmarks/run-bottleneck-benchmarks.sh --quick       # Run a fast subset (short runs)
#   ./benchmarks/run-bottleneck-benchmarks.sh --filter NAME # Run benchmarks matching NAME
#
# Results are written to benchmarks/results/

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj"
RESULTS_DIR="$REPO_ROOT/benchmarks/results"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

# Parse arguments
QUICK=false
FILTER=""
while [[ $# -gt 0 ]]; do
    case $1 in
        --quick) QUICK=true; shift ;;
        --filter) FILTER="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 [--quick] [--filter NAME]"
            echo ""
            echo "  --quick          Fewer iterations for a fast overview (less precise)"
            echo "  --filter NAME    Run only benchmarks whose class or method name contains NAME"
            echo ""
            echo "Benchmark classes available:"
            echo ""
            echo "  Stage isolation (EndToEndPipelineBenchmarks.cs):"
            echo "    EndToEndPipelineBenchmarks    - Channel → drain → serialize → write (staged)"
            echo "    DedupKeyBenchmarks            - SHA256 dedup key hashing"
            echo "    MarketEventCreationBenchmarks - Record creation and with-expression cost"
            echo ""
            echo "  Collector hot paths (CollectorBenchmarks.cs):"
            echo "    TradeCollectorBenchmarks      - TradeDataCollector throughput"
            echo "    DepthCollectorBenchmarks      - MarketDepthCollector throughput"
            echo ""
            echo "  Buffer & serialization (StorageSinkBenchmarks.cs):"
            echo "    EventBufferBenchmarks         - Swap-buffer drain strategies"
            echo "    EventBufferIngestionBenchmarks - Single vs multi-producer contention"
            echo "    BatchSerializationBenchmarks  - Sequential vs parallel, string vs UTF-8"
            echo ""
            echo "  WAL & checksum (WalChecksumBenchmarks.cs):"
            echo "    WalChecksumBenchmarks         - Legacy string-concat vs IncrementalHash + stackalloc"
            echo ""
            echo "  Channel throughput & latency (EventPipelineBenchmarks.cs):"
            echo "    EventPipelineBenchmarks       - Channel throughput at various capacities"
            echo "    PublishLatencyBenchmarks      - Single-event publish latency"
            echo ""
            echo "  Serialization (JsonSerializationBenchmarks.cs):"
            echo "    JsonSerializationBenchmarks   - Reflection vs source-generated JSON"
            echo "    JsonParsingBenchmarks         - Alpaca message parsing"
            echo ""
            echo "  Indicators (IndicatorBenchmarks.cs):"
            echo "    IndicatorBenchmarks           - Technical indicator calculations"
            echo "    SingleIndicatorBenchmarks     - Per-indicator cost isolation"
            exit 0
            ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

mkdir -p "$RESULTS_DIR"

echo "============================================"
echo " Meridian - Bottleneck Benchmarks"
echo " $(date)"
echo "============================================"
echo ""

# Verify dotnet is available
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: dotnet SDK not found. Install .NET 9.0 SDK first."
    echo "  https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
fi

# Verify the project file exists
if [[ ! -f "$PROJECT" ]]; then
    echo "ERROR: Benchmark project not found: $PROJECT"
    exit 1
fi

echo "dotnet version: $(dotnet --version)"
echo "Results directory: $RESULTS_DIR"
echo ""

# Build first to avoid including build time in benchmarks
echo "--- Building benchmarks (Release) ---"
dotnet build "$PROJECT" -c Release --no-restore -v quiet || {
    echo "Build failed. Running restore first..."
    dotnet restore "$PROJECT"
    dotnet build "$PROJECT" -c Release -v quiet
}
echo ""

# Base BenchmarkDotNet arguments.
# Note: --filter is intentionally not added here; each phase below supplies its own
# pattern so that BenchmarkDotNet never receives two conflicting --filter arguments.
BDN_ARGS=("--artifacts" "$RESULTS_DIR/$TIMESTAMP")

if $QUICK; then
    # Short mode: fewer iterations for a quick overview
    BDN_ARGS+=("--job" "short")
    echo "Mode: QUICK (short runs, less precise)"
else
    echo "Mode: FULL (default iterations, high precision)"
fi

if [[ -n "$FILTER" ]]; then
    echo "Filter: $FILTER"
fi

echo ""

# run_phase PATTERN — invoke BenchmarkDotNet with a single filter pattern
run_phase() {
    local pattern="$1"
    dotnet run --project "$PROJECT" -c Release --no-build -- \
        --filter "$pattern" "${BDN_ARGS[@]}" || return $?
}

# When the user supplies --filter, run a single targeted pass rather than iterating
# through every phase.  Running phases individually would either duplicate the filter
# (causing BenchmarkDotNet to receive two --filter arguments) or silently skip all
# phases when the filter value doesn't match any phase-name check.
if [[ -n "$FILTER" ]]; then
    echo "=== Running benchmarks matching: $FILTER ==="
    echo ""
    run_phase "*${FILTER}*"
    echo ""
else
    # --- Phase 1: End-to-End Stage Isolation ---
    # This is the most important benchmark — it shows where time goes.
    echo "=== Phase 1: End-to-End Pipeline Stage Isolation ==="
    echo "  Isolates channel / batch-drain / serialization / write costs"
    echo ""
    run_phase "*EndToEndPipelineBenchmarks*"
    echo ""

    # --- Phase 2: Collector Hot Paths ---
    echo "=== Phase 2: Collector Hot Paths ==="
    echo "  TradeDataCollector + MarketDepthCollector throughput"
    echo ""
    run_phase "*CollectorBenchmarks*"
    echo ""

    # --- Phase 3: Buffer & Serialization ---
    echo "=== Phase 3: Buffer & Serialization ==="
    echo "  EventBuffer drain strategies + batch serialization"
    echo ""
    run_phase "*EventBuffer*|*BatchSerialization*"
    echo ""

    # --- Phase 4: Object Lifecycle ---
    echo "=== Phase 4: Object Lifecycle ==="
    echo "  MarketEvent creation + dedup key hashing"
    echo ""
    run_phase "*MarketEventCreation*|*DedupKey*"
    echo ""

    # --- Phase 5: WAL & Checksum Strategies ---
    echo "=== Phase 5: WAL & Checksum Strategies ==="
    echo "  Legacy string-concat vs IncrementalHash + stackalloc (see BOTTLENECK_REPORT #2)"
    echo ""
    run_phase "*WalChecksum*"
    echo ""
fi

echo "============================================"
echo " Benchmarks complete!"
echo " Results: $RESULTS_DIR/$TIMESTAMP"
echo "============================================"
echo ""
echo "Key files to review:"
echo "  - *-report-github.md   (summary tables)"
echo "  - *-report.csv         (raw data for analysis)"
echo ""
echo "Quick interpretation guide:"
echo "  - Compare Mean columns across EndToEndPipeline stages to find the dominant cost"
echo "  - Check Allocated column for GC pressure (record with-expressions, serialization)"
echo "  - BatchSerialization: if Parallel is faster at BatchSize=5000, lower the threshold"
echo "  - TradeCollector: if SingleSymbol is much slower, per-symbol lock is the bottleneck"
echo "  - WalChecksum: IncrementalHash path should be significantly faster than LegacyStringConcat"
