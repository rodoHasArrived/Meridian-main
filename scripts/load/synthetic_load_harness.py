#!/usr/bin/env python3
"""Simple synthetic load harness for Meridian endpoints.

Simulates baseline and surge load and prints latency/error summaries.
"""
from __future__ import annotations
import argparse
import concurrent.futures
import statistics
import time
import urllib.request


def hit(url: str, timeout: float) -> tuple[bool, float]:
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(url, timeout=timeout) as response:
            ok = 200 <= response.status < 500
        return ok, (time.perf_counter() - start) * 1000
    except Exception:
        return False, (time.perf_counter() - start) * 1000


def run_phase(url: str, requests: int, concurrency: int, timeout: float) -> None:
    latencies = []
    failures = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as pool:
        futures = [pool.submit(hit, url, timeout) for _ in range(requests)]
        for fut in concurrent.futures.as_completed(futures):
            ok, latency_ms = fut.result()
            latencies.append(latency_ms)
            if not ok:
                failures += 1

    p95 = statistics.quantiles(latencies, n=20)[18] if len(latencies) >= 20 else max(latencies)
    print(f"requests={requests} concurrency={concurrency} failures={failures} "
          f"avg_ms={statistics.mean(latencies):.2f} p95_ms={p95:.2f} max_ms={max(latencies):.2f}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://localhost:8080/healthz")
    parser.add_argument("--baseline-requests", type=int, default=200)
    parser.add_argument("--baseline-concurrency", type=int, default=10)
    parser.add_argument("--surge-multiplier", type=int, default=10)
    parser.add_argument("--timeout", type=float, default=2.0)
    args = parser.parse_args()

    print("phase=baseline")
    run_phase(args.url, args.baseline_requests, args.baseline_concurrency, args.timeout)
    print("phase=surge")
    run_phase(
        args.url,
        args.baseline_requests * args.surge_multiplier,
        args.baseline_concurrency * args.surge_multiplier,
        args.timeout,
    )


if __name__ == "__main__":
    main()
