import { screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { renderWithRouter } from "@/test/render";
import { buildBreakAgingRows, buildProviderFreshnessRows, CloseTaskProgressChart, ReconciliationAgingChart } from "./workstation-evidence-charts";
import type { ProviderConnectionRow, ReconciliationBreakQueueItem } from "@/types";

const now = Date.parse("2026-11-11T12:00:00Z");

describe("operational chart evidence", () => {
  it("does not turn missing or future provider timestamps into zero age", () => {
    const providers = [
      { providerId: "current", displayName: "Current", lastSuccessfulAt: "2026-11-11T10:00:00Z" },
      { providerId: "missing", displayName: "Missing", lastSuccessfulAt: null },
      { providerId: "future", displayName: "Future", lastSuccessfulAt: "2026-11-12T10:00:00Z" }
    ] as ProviderConnectionRow[];
    expect(buildProviderFreshnessRows(providers, now)).toEqual({
      rows: [{ id: "current", label: "Current", value: 2, valueLabel: "2h" }], unavailable: 2
    });
  });

  it("uses retained occurrence age with explicit buckets and excludes untracked timestamps", () => {
    const breaks = [0, 4, 24, 72].map((hours) => ({
      lineage: { occurrenceFirstObservedAt: new Date(now - hours * 3_600_000).toISOString() }
    })) as ReconciliationBreakQueueItem[];
    breaks.push({ detectedAt: "2026-11-01T12:00:00Z", lineage: null } as ReconciliationBreakQueueItem);
    const model = buildBreakAgingRows(breaks, now);
    expect(model.rows.map((row) => row.value)).toEqual([1, 1, 1, 1]);
    expect(model.tracked).toBe(4);
    expect(model.unavailable).toBe(1);
  });

  it("provides the same aging counts in an accessible table alternative", async () => {
    const breaks = [{ breakId: "break-1", lineage: { occurrenceFirstObservedAt: "2026-11-11T10:00:00Z" } }] as ReconciliationBreakQueueItem[];
    const { container } = renderWithRouter(<ReconciliationAgingChart breaks={breaks} nowMs={now} />);
    expect(screen.getByRole("img", { name: "Break occurrence age" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Break occurrence age data", hidden: true })).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("does not fabricate burndown history or a successful empty close plan", () => {
    renderWithRouter(<CloseTaskProgressChart tasks={[]} />);
    expect(screen.getByText("No close plan tasks are loaded.")).toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screen.getByText(/Historical task snapshots are unavailable/)).toBeInTheDocument();
  });
});
