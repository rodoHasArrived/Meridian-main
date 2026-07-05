import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { EquityCurve } from "@/components/charts/EquityCurve";
import {
  ChartSyncProvider,
  nearestTimestampIndex,
  useChartCrosshairSync,
  useChartSync
} from "@/components/charts/chart-sync";

describe("nearestTimestampIndex", () => {
  it("returns null when there is no target or no timestamps", () => {
    expect(nearestTimestampIndex([0, 1, 2], null)).toBeNull();
    expect(nearestTimestampIndex([], 5)).toBeNull();
  });

  it("returns the index of the closest timestamp, preferring the earlier one on ties", () => {
    expect(nearestTimestampIndex([0, 100, 200, 300], 190)).toBe(2);
    expect(nearestTimestampIndex([50, 150, 250], 200)).toBe(1); // 150 and 250 both 50 away -> first
  });

  it("clamps to the ends and hits exact matches", () => {
    expect(nearestTimestampIndex([10, 20, 30], 5)).toBe(0); // before first
    expect(nearestTimestampIndex([10, 20, 30], 100)).toBe(2); // after last
    expect(nearestTimestampIndex([10, 20, 30], 20)).toBe(1); // exact
    expect(nearestTimestampIndex([10, 20, 30, 40, 50], 34)).toBe(2); // closer to 30 than 40
  });
});

function BoundEquityCurve({ timestamps, points }: { timestamps: number[]; points: number[] }) {
  const sync = useChartCrosshairSync(timestamps);
  return (
    <EquityCurve
      series={[{ label: "Equity", color: "var(--chart-equity)", points }]}
      crosshairIndex={sync.crosshairIndex}
      onCrosshairChange={sync.onCrosshairChange}
      onPointActivate={sync.onPointActivate}
      showLegend={false}
    />
  );
}

function SelectedReadout() {
  const { selectedTimestamp } = useChartSync();
  return <div data-testid="selected">{selectedTimestamp ?? "none"}</div>;
}

describe("chart crosshair synchronization", () => {
  afterEach(() => cleanup());

  it("mirrors a crosshair across linked charts with different x-domains", async () => {
    const user = userEvent.setup();
    render(
      <ChartSyncProvider>
        <BoundEquityCurve timestamps={[0, 100, 200, 300]} points={[1, 2, 3, 4]} />
        <BoundEquityCurve timestamps={[50, 150, 250]} points={[9, 8, 7]} />
      </ChartSyncProvider>
    );

    const [chartA, chartB] = screen.getAllByRole("slider");
    chartA.focus();
    await user.keyboard("{ArrowRight}{ArrowRight}{ArrowRight}"); // move chart A to index 2 (ts 200)

    // Chart A sits on its own index 2; chart B resolves the shared ts 200 to its nearest point (150 -> index 1).
    expect(chartA).toHaveAttribute("aria-valuenow", "2");
    expect(chartB).toHaveAttribute("aria-valuenow", "1");
  });

  it("activating a point publishes the selected timestamp for the evidence drill", async () => {
    const user = userEvent.setup();
    render(
      <ChartSyncProvider>
        <BoundEquityCurve timestamps={[0, 100, 200, 300]} points={[1, 2, 3, 4]} />
        <SelectedReadout />
      </ChartSyncProvider>
    );

    expect(screen.getByTestId("selected")).toHaveTextContent("none");

    const slider = screen.getByRole("slider");
    slider.focus();
    await user.keyboard("{ArrowRight}{Enter}"); // crosshair to index 0 (ts 0), then activate

    expect(screen.getByTestId("selected")).toHaveTextContent("0");
  });

  it("activates the point under a pointer click", () => {
    render(
      <ChartSyncProvider>
        <BoundEquityCurve timestamps={[0, 100, 200, 300]} points={[1, 2, 3, 4]} />
        <SelectedReadout />
      </ChartSyncProvider>
    );

    const slider = screen.getByRole("slider");
    slider.getBoundingClientRect = () =>
      ({ left: 0, width: 300, top: 0, height: 100, right: 300, bottom: 100, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect;

    fireEvent.click(slider, { clientX: 300 }); // right edge -> last index (3) -> ts 300

    expect(screen.getByTestId("selected")).toHaveTextContent("300");
  });
});
