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
