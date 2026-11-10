import { describe, expect, it } from "vitest";
import {
  buildFigureDifferenceNote,
  buildMetricTrust,
  describeMetricProvenance,
  formatMetricAsOf,
  metricFreshnessFromTrustTone,
  parseDisplayedNumber,
  resolveMetricDelta,
  sharedMetricProvenance,
  withMetricContext,
  worstMetricFreshness
} from "@/lib/metric-trust";
import type { MetricSnapshot } from "@/types";

function metric(overrides: Partial<MetricSnapshot> = {}): MetricSnapshot {
  return { id: "runs", label: "Runs", value: "24", delta: "", tone: "default", ...overrides };
}

describe("resolveMetricDelta", () => {
  it.each(["+8%", "0%", "+2", "-3", "0", "▲ 3.1 bps", "+$1,200", "−4.5%"])("drops the bare change %s when no baseline is stated", (delta) => {
    expect(resolveMetricDelta(delta)).toBeNull();
  });

  it("drops a direction flag that measures nothing, even with a baseline", () => {
    expect(resolveMetricDelta("+session")).toBeNull();
    expect(resolveMetricDelta("-session", "vs prior session")).toBeNull();
  });

  it("shows a bare change together with what it is measured against", () => {
    expect(resolveMetricDelta("+8%", "vs prior session")).toEqual({ kind: "change", text: "+8% vs prior session" });
    expect(resolveMetricDelta("+2", " since yesterday ")).toEqual({ kind: "change", text: "+2 since yesterday" });
  });

  it("keeps a change that already names its comparison", () => {
    expect(resolveMetricDelta("3 new since your last visit")).toEqual({ kind: "change", text: "3 new since your last visit" });
    expect(resolveMetricDelta("+1.84% today")).toEqual({ kind: "change", text: "+1.84% today" });
  });

  it("keeps descriptive captions and ignores empty text", () => {
    expect(resolveMetricDelta("From open positions")).toEqual({ kind: "caption", text: "From open positions" });
    expect(resolveMetricDelta("  ")).toBeNull();
    expect(resolveMetricDelta(null)).toBeNull();
  });
});

describe("metric provenance", () => {
  it("formats an ISO as-of time in UTC with the year and passes other text through", () => {
    expect(formatMetricAsOf("2026-06-30T18:05:00Z")).toBe("Jun 30, 2026, 18:05 UTC");
    expect(formatMetricAsOf("187d ago")).toBe("187d ago");
    expect(formatMetricAsOf("  ")).toBeNull();
    expect(formatMetricAsOf(null)).toBeNull();
  });

  it("states source, as-of and scope in one line", () => {
    expect(describeMetricProvenance({ source: "Portfolio workspace", asOf: "2026-06-30T18:05:00Z", scope: "All accounts" }))
      .toBe("Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC · Scope: All accounts");
    expect(describeMetricProvenance({ source: "Trading workspace" })).toBe("Source: Trading workspace");
    expect(describeMetricProvenance({ source: " " })).toBeNull();
    expect(describeMetricProvenance(null)).toBeNull();
  });

  it("stamps page context onto metrics without overriding what a metric already states", () => {
    const own = { source: "Broker statement" };
    const [stamped, kept] = withMetricContext(
      [metric(), metric({ id: "cash", provenance: own, freshness: "fresh", baseline: "vs prior close" })],
      { provenance: { source: "Portfolio workspace" }, freshness: "stale", baseline: "vs prior session" }
    );

    expect(stamped).toMatchObject({ provenance: { source: "Portfolio workspace" }, freshness: "stale", baseline: "vs prior session" });
    expect(kept).toMatchObject({ provenance: own, freshness: "fresh", baseline: "vs prior close" });
  });

  it("reports the shared provenance of a strip only when every metric agrees", () => {
    const provenance = { source: "Portfolio workspace", asOf: "2026-06-30T18:05:00Z" };
    expect(sharedMetricProvenance([metric({ provenance }), metric({ provenance: { ...provenance } })])).toEqual(provenance);
    expect(sharedMetricProvenance([metric({ provenance }), metric({ provenance: { source: "Trading workspace" } })])).toBeNull();
    expect(sharedMetricProvenance([metric({ provenance }), metric()])).toBeNull();
    expect(sharedMetricProvenance([])).toBeNull();
  });
});

describe("metric freshness", () => {
  it("describes a stale figure with its as-of time", () => {
    const trust = buildMetricTrust(metric({
      delta: "+8%",
      freshness: "stale",
      provenance: { source: "Portfolio workspace", asOf: "2026-06-30T18:05:00Z" }
    }));

    expect(trust).toMatchObject({
      delta: null,
      degraded: true,
      freshnessLabel: "Stale",
      freshnessDetail: "Stale: as of Jun 30, 2026, 18:05 UTC.",
      title: "Stale: as of Jun 30, 2026, 18:05 UTC. Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC"
    });
  });

  it("treats a metric with no freshness claim as fresh and unverified data as degraded", () => {
    expect(buildMetricTrust(metric())).toMatchObject({ degraded: false, freshnessLabel: null, title: null });
    expect(buildMetricTrust(metric({ freshness: "unverified" }))).toMatchObject({
      degraded: true,
      freshnessLabel: "Unverified",
      freshnessDetail: "Unverified: no current evidence backs this figure."
    });
    expect(buildMetricTrust(metric({ freshness: "stale" })).freshnessDetail).toBe("Stale: the source is past its freshness budget.");
  });

  it("ranks stale above unverified above fresh across a strip", () => {
    expect(worstMetricFreshness([metric(), metric({ freshness: "unverified" }), metric({ freshness: "stale" })])).toBe("stale");
    expect(worstMetricFreshness([metric(), metric({ freshness: "unverified" })])).toBe("unverified");
    expect(worstMetricFreshness([metric()])).toBe("fresh");
  });

  it("maps data-confidence tones onto figure freshness", () => {
    expect(metricFreshnessFromTrustTone("ready")).toBe("fresh");
    expect(metricFreshnessFromTrustTone("review")).toBe("stale");
    expect(metricFreshnessFromTrustTone("blocked")).toBe("stale");
    expect(metricFreshnessFromTrustTone("unknown")).toBe("unverified");
  });
});

describe("parseDisplayedNumber", () => {
  it("reads signed, grouped and parenthesized amounts", () => {
    expect(parseDisplayedNumber("+$1,239")).toBe(1239);
    expect(parseDisplayedNumber("-$1,234.50")).toBe(-1234.5);
    expect(parseDisplayedNumber("−$90")).toBe(-90);
    expect(parseDisplayedNumber("(1,234.00)")).toBe(-1234);
    expect(parseDisplayedNumber("2")).toBe(2);
    expect(parseDisplayedNumber("EUR 12.5")).toBe(12.5);
    expect(parseDisplayedNumber("1,234.56 USD")).toBe(1234.56);
  });

  it("is null for text that is not a single amount", () => {
    expect(parseDisplayedNumber("—")).toBeNull();
    expect(parseDisplayedNumber("")).toBeNull();
    expect(parseDisplayedNumber("1/2")).toBeNull();
    expect(parseDisplayedNumber("Observe")).toBeNull();
    expect(parseDisplayedNumber(null)).toBeNull();
  });
});

describe("buildFigureDifferenceNote", () => {
  const header = {
    name: "Header strip",
    provenance: { source: "Portfolio workspace", asOf: "2026-06-30T18:05:00Z", scope: "All accounts" },
    figures: [
      { key: "exposure", label: "Total exposure", value: "$18,900" },
      { key: "unrealized-pnl", label: "Unrealized P&L", value: "+$90" },
      { key: "positions", label: "Open positions", value: "1" },
      { key: "cash", label: "Cash", value: "—" }
    ]
  };
  const explorer = {
    name: "Portfolio Explorer",
    provenance: { source: "Retained run records", asOf: "Jan 2, 2026, 14:00 UTC", scope: "Run 7" },
    figures: [
      { key: "positions", label: "Positions", value: "2" },
      { key: "exposure", label: "Market value", value: "$25,587" },
      { key: "unrealized-pnl", label: "Unrealized P&L", value: "+$1,239" }
    ]
  };

  it("names each differing figure and the source behind each panel", () => {
    expect(buildFigureDifferenceNote(header, explorer)).toEqual({
      title: "Why these differ",
      summary: "Header strip and Portfolio Explorer read different records, so their figures are not expected to match.",
      unexplained: false,
      rows: [
        { key: "exposure", label: "Total exposure", first: "$18,900", second: "$25,587" },
        { key: "unrealized-pnl", label: "Unrealized P&L", first: "+$90", second: "+$1,239" },
        { key: "positions", label: "Open positions", first: "1", second: "2" }
      ],
      sources: [
        { name: "Header strip", text: "Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC · Scope: All accounts" },
        { name: "Portfolio Explorer", text: "Source: Retained run records · As of Jan 2, 2026, 14:00 UTC · Scope: Run 7" }
      ]
    });
  });

  it("returns nothing when the shared figures agree at the coarser displayed precision", () => {
    expect(buildFigureDifferenceNote(header, {
      ...explorer,
      figures: [
        { key: "positions", label: "Positions", value: "1" },
        { key: "exposure", label: "Market value", value: "$18,900.25" },
        { key: "unrealized-pnl", label: "Unrealized P&L", value: "+$90.00" }
      ]
    })).toBeNull();
  });

  it("flags a difference between panels that claim the same source as unexplained", () => {
    const note = buildFigureDifferenceNote(header, { ...explorer, provenance: header.provenance });

    expect(note?.unexplained).toBe(true);
    expect(note?.summary).toBe("Header strip and Portfolio Explorer report the same source but different figures. Treat both as unverified until the difference is traced.");
  });
});
