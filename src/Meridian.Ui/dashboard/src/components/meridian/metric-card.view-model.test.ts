import { describe, expect, it } from "vitest";
import { buildMetricCardViewModel } from "@/components/meridian/metric-card.view-model";

describe("metric card view model", () => {
  it("derives semantic tone, stable ids, and accessible summary copy", () => {
    const model = buildMetricCardViewModel({
      id: "net-pnl",
      label: "NET P&L",
      value: "+12,430.17",
      delta: "▲ +0.42% · 1D",
      tone: "success"
    });

    expect(model).toMatchObject({
      id: "metric-net-pnl",
      label: "NET P&L",
      value: "+12,430.17",
      delta: "▲ +0.42% · 1D",
      toneClass: "text-success",
      deltaAriaLabel: "Change up +0.42% · 1D",
      labelId: "metric-net-pnl-label",
      valueId: "metric-net-pnl-value",
      deltaId: "metric-net-pnl-delta"
    });
    expect(model.ariaLabel).toBe("NET P&L metric. +12,430.17. Change up +0.42% · 1D. Status positive");
  });

  it("keeps empty delta values out of the accessible description", () => {
    const model = buildMetricCardViewModel({
      id: "provider-count",
      label: "Providers",
      value: "4/5",
      delta: " ",
      tone: "warning"
    });

    expect(model.delta).toBeNull();
    expect(model.deltaAriaLabel).toBeNull();
    expect(model.deltaId).toBeNull();
    expect(model.ariaLabel).toBe("Providers metric. 4/5. Status attention");
  });
});

describe("buildMetricCardViewModel trust rules", () => {
  it("drops a bare change with no baseline and shows it once a baseline is stated", () => {
    const bare = buildMetricCardViewModel({ id: "runs", label: "Runs", value: "24", delta: "+8%", tone: "default" });
    const labelled = buildMetricCardViewModel({ id: "runs", label: "Runs", value: "24", delta: "+8%", baseline: "vs prior session", tone: "default" });

    expect(bare).toMatchObject({ delta: null, deltaAriaLabel: null, deltaId: null, ariaLabel: "Runs metric. 24. Status neutral" });
    expect(labelled).toMatchObject({ delta: "+8% vs prior session", deltaAriaLabel: "Change +8% vs prior session" });
  });

  it("uses the one P&L spelling and carries freshness and provenance into the accessible name", () => {
    const vm = buildMetricCardViewModel({
      id: "pnl",
      label: "Unrealized PnL",
      value: "+$90",
      delta: "",
      tone: "success",
      freshness: "stale",
      provenance: { source: "Portfolio workspace", asOf: "2026-06-30T18:05:00Z" }
    });

    expect(vm).toMatchObject({
      label: "Unrealized P&L",
      freshnessLabel: "Stale",
      provenanceText: "Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC",
      ariaLabel: "Unrealized P&L metric. +$90. Status positive. Stale: as of Jun 30, 2026, 18:05 UTC. Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC"
    });
  });
});
