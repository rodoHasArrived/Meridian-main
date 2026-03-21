#!/usr/bin/env bash
set -euo pipefail

# Bottleneck Benchmark Runner
# Runs targeted benchmarks to identify performance bottlenecks in the event pipeline.
#
# Usage:
#   ./benchmarks/run-bottleneck-benchmarks.sh              # Run all bottleneck benchmarks
#   ./benchmarks/run-bottleneck-benchmarks.sh --quick       # Run a fast subset (short runs)
#   ./benchmarks/run-bottleneck-benchmarks.sh --filter NAME # Run a specific benchmark class
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
            echo "Benchmark classes available:"
            echo "  Stage isolation:"
            echo "    EndToEndPipelineBenchmarks    - Channel → drain → serialize → write (staged)"
            echo ""
            echo "  Collector hot paths:"
            echo "    TradeCollectorBenchmarks       - TradeDataCollector throughput"
            echo "    DepthCollectorBenchmarks       - MarketDepthCollector throughput"
            echo ""
            echo "  Buffer & serialization:"
            echo "    EventBufferBenchmarks          - Swap-buffer drain strategies"
            echo "    EventBufferIngestionBenchmarks  - Single vs multi-producer contention"
            echo "    BatchSerializationBenchmarks   - Sequential vs parallel, string vs UTF-8"
            echo ""
            echo "  Object lifecycle:"
            echo "    MarketEventCreationBenchmarks  - Record creation and with-expression cost"
            echo "    DedupKeyBenchmarks             - SHA256 dedup key hashing"
            echo ""
            echo "  Existing benchmarks:"
            echo "    EventPipelineBenchmarks        - Channel throughput at various capacities"
            echo "    PublishLatencyBenchmarks        - Single-event publish latency"
            echo "    JsonSerializationBenchmarks    - Reflection vs source-generated JSON"
            echo "    JsonParsingBenchmarks           - Alpaca message parsing"
            echo "    IndicatorBenchmarks             - Technical indicator calculations"
            echo "    SingleIndicatorBenchmarks       - Per-indicator cost isolation"
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

echo "dotnet version: $(dotnet --version)"
echo "Results directory: $RESULTS_DIR"
echo ""

# Build first to avoid including build time in benchmarks
echo "--- Building benchmarks (Release) ---"
dotnet build "$PROJECT" -c Release --no-restore -v quiet 2>&1 || {
    echo "Build failed. Running restore first..."
    dotnet restore "$PROJECT"
    dotnet build "$PROJECT" -c Release -v quiet
}
echo ""

# Determine benchmark arguments
BDN_ARGS=("--artifacts" "$RESULTS_DIR/$TIMESTAMP")

if $QUICK; then
    # Short mode: fewer iterations for a quick overview
    BDN_ARGS+=("--job" "short")
    echo "Mode: QUICK (short runs, less precise)"
else
    echo "Mode: FULL (default iterations, high precision)"
fi

if [[ -n "$FILTER" ]]; then
    BDN_ARGS+=("--filter" "*${FILTER}*")
    echo "Filter: $FILTER"
fi

echo ""

# --- Phase 1: End-to-End Stage Isolation ---
# This is the most important benchmark — it shows where time goes.
if [[ -z "$FILTER" ]] || [[ "$FILTER" == *"EndToEnd"* ]]; then
    echo "=== Phase 1: End-to-End Pipeline Stage Isolation ==="
    echo "  Isolates channel / batch-drain / serialization / write costs"
    echo ""
    dotnet run --project "$PROJECT" -c Release --no-build -- \
        --filter "*EndToEndPipelineBenchmarks*" "${BDN_ARGS[@]}" 2>&1
    echo ""
fi

# --- Phase 2: Collector Hot Paths ---
if [[ -z "$FILTER" ]] || [[ "$FILTER" == *"Collector"* ]]; then
    echo "=== Phase 2: Collector Hot Paths ==="
    echo "  TradeDataCollector + MarketDepthCollector throughput"
    echo ""
    dotnet run --project "$PROJECT" -c Release --no-build -- \
        --filter "*CollectorBenchmarks*" "${BDN_ARGS[@]}" 2>&1
    echo ""
fi

# --- Phase 3: Buffer & Serialization ---
if [[ -z "$FILTER" ]] || [[ "$FILTER" == *"Buffer"* ]] || [[ "$FILTER" == *"Batch"* ]]; then
    echo "=== Phase 3: Buffer & Serialization ==="
    echo "  EventBuffer drain strategies + batch serialization"
    echo ""
    dotnet run --project "$PROJECT" -c Release --no-build -- \
        --filter "*EventBuffer*|*BatchSerialization*" "${BDN_ARGS[@]}" 2>&1
    echo ""
fi

# --- Phase 4: Object Lifecycle ---
if [[ -z "$FILTER" ]] || [[ "$FILTER" == *"Creation"* ]] || [[ "$FILTER" == *"Dedup"* ]]; then
    echo "=== Phase 4: Object Lifecycle ==="
    echo "  MarketEvent creation + dedup key hashing"
    echo ""
    dotnet run --project "$PROJECT" -c Release --no-build -- \
        --filter "*MarketEventCreation*|*DedupKey*" "${BDN_ARGS[@]}" 2>&1
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
