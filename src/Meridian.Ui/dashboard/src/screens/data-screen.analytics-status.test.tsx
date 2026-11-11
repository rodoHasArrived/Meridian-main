import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  buildDataAnalyticsDegradedViewModel,
  buildDataAnalyticsAvailability,
  DataAnalyticsDegradedRegion,
  type DataAnalyticsPanelStatus
} from "@/screens/data-screen.analytics-status";
import { renderWithRouter } from "@/test/render";

function panel(overrides: Partial<DataAnalyticsPanelStatus> = {}): DataAnalyticsPanelStatus {
  return {
    id: "data-quality",
    label: "Data quality",
    error: null,
    loading: false,
    refresh: vi.fn().mockResolvedValue(undefined),
    ...overrides
  };
}

describe("diagnostic availability", () => {
  it("reports one failed read even when alarm consolidation is unnecessary", () => {
    const availability = buildDataAnalyticsAvailability([panel({ error: "unavailable" }), panel({ id: "coverage", label: "Coverage" })]);
    expect(availability.label).toBe("1 diagnostic unavailable");
    expect(availability.tone).toBe("warning");
    expect(availability.detail).toContain("Data quality");
  });

  it("keeps checking explicit and never promotes loading reads to available", () => {
    expect(buildDataAnalyticsAvailability([panel({ loading: true })])).toMatchObject({ label: "Checking diagnostics…", tone: "outline" });
    expect(buildDataAnalyticsAvailability([panel({ loading: true, error: "old failure" })])).toMatchObject({ label: "1 diagnostic unavailable · checking", tone: "warning" });
  });

  it("states read availability without claiming healthy financial data", () => {
    const availability = buildDataAnalyticsAvailability([panel(), panel({ id: "coverage", label: "Coverage" })]);
    expect(availability.label).toBe("2/2 diagnostics available");
    expect(availability.detail).toContain("Review data quality separately");
  });
});

describe("buildDataAnalyticsDegradedViewModel", () => {
  it("returns null when no analytics panel is failing", () => {
    expect(buildDataAnalyticsDegradedViewModel([panel(), panel({ id: "coverage-gaps", label: "Coverage" })])).toBeNull();
  });

  it("returns null for a single failing panel so it keeps its inline state", () => {
    expect(
      buildDataAnalyticsDegradedViewModel([
        panel({ error: "boom" }),
        panel({ id: "coverage-gaps", label: "Coverage" })
      ])
    ).toBeNull();
  });

  it("consolidates two or more failing panels into one degraded state", () => {
    const vm = buildDataAnalyticsDegradedViewModel([
      panel({ error: "boom" }),
      panel({ id: "capability-matrix", label: "Provider capability matrix", error: "down" }),
      panel({ id: "coverage-gaps", label: "Coverage" })
    ]);

    expect(vm).not.toBeNull();
    expect(vm?.affected.map((entry) => entry.id)).toEqual(["data-quality", "capability-matrix"]);
    expect(vm?.affectedIds.has("coverage-gaps")).toBe(false);
    expect(vm?.affectsLabel).toBe("Data quality · Provider capability matrix");
  });

  it("retries every affected panel and reports refreshing while any is loading", async () => {
    const first = panel({ error: "boom" });
    const second = panel({ id: "capability-matrix", label: "Capability matrix", error: "down", loading: true });
    const healthy = panel({ id: "coverage-gaps", label: "Coverage" });
    const vm = buildDataAnalyticsDegradedViewModel([first, second, healthy]);

    expect(vm?.refreshing).toBe(true);
    await vm?.retryAll();
    expect(first.refresh).toHaveBeenCalledTimes(1);
    expect(second.refresh).toHaveBeenCalledTimes(1);
    expect(healthy.refresh).not.toHaveBeenCalled();
  });
});

describe("DataAnalyticsDegradedRegion", () => {
  it("renders one degraded state with affected panels and a retry-all action", async () => {
    const user = userEvent.setup();
    const first = panel({ error: "boom" });
    const second = panel({ id: "capability-matrix", label: "Provider capability matrix", error: "down" });
    const vm = buildDataAnalyticsDegradedViewModel([first, second]);

    renderWithRouter(<DataAnalyticsDegradedRegion vm={vm!} />);

    expect(screen.getByText("Data analytics services degraded")).toBeInTheDocument();
    expect(screen.getByText("Data quality · Provider capability matrix")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open workstation diagnostics in Settings" })).toHaveAttribute(
      "href",
      "/settings/diagnostics"
    );

    await user.click(screen.getByRole("button", { name: "Retry 2 unavailable analytics panels" }));
    expect(first.refresh).toHaveBeenCalledTimes(1);
    expect(second.refresh).toHaveBeenCalledTimes(1);
  });
});
