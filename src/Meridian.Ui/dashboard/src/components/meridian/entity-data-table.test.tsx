import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EntityDataTable } from "@/components/meridian/entity-data-table";
import type { ResearchRunRecord } from "@/types";

const rows: ResearchRunRecord[] = [
  {
    id: "run-1",
    strategyName: "Mean Reversion FX",
    engine: "Meridian Native",
    mode: "paper",
    status: "Running",
    dataset: "FX Majors",
    window: "90d",
    pnl: "+4.2%",
    sharpe: "1.41",
    lastUpdated: "2m ago",
    notes: "Primary paper candidate."
  },
  {
    id: "run-2",
    strategyName: "Index Momentum",
    engine: "Lean",
    mode: "live",
    status: "Needs Review",
    dataset: "US Equities",
    window: "180d",
    pnl: "-0.8%",
    sharpe: "0.62",
    lastUpdated: "10m ago",
    notes: "Check slippage."
  }
];

describe("EntityDataTable", () => {
  it("filters rows by free text", async () => {
    const user = userEvent.setup();
    render(<EntityDataTable rows={rows} onSelectRun={() => undefined} />);

    await user.type(screen.getByLabelText("Filter runs"), "Momentum");

    expect(screen.getByText("Index Momentum")).toBeInTheDocument();
    expect(screen.queryByText("Mean Reversion FX")).not.toBeInTheDocument();
  });

  it("opens row detail callback", async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<EntityDataTable rows={rows} onSelectRun={onSelect} />);

    await user.click(screen.getAllByRole("button", { name: /open/i })[0]);

    expect(onSelect).toHaveBeenCalledWith(rows[0]);
  });

  it("renders checkboxes when onToggleSelection is provided", () => {
    render(
      <EntityDataTable
        rows={rows}
        onSelectRun={() => undefined}
        selectedRunIds={new Set()}
        onToggleSelection={() => undefined}
      />
    );

    const checkboxes = screen.getAllByRole("checkbox");
    expect(checkboxes).toHaveLength(rows.length);
  });

  it("marks the correct checkbox as checked for selected run", () => {
    render(
      <EntityDataTable
        rows={rows}
        onSelectRun={() => undefined}
        selectedRunIds={new Set(["run-1"])}
        onToggleSelection={() => undefined}
      />
    );

    const checkboxes = screen.getAllByRole("checkbox") as HTMLInputElement[];
    // run-1 rows come from sorting — find by aria-label
    const checkedBoxes = checkboxes.filter((cb) => cb.checked);
    expect(checkedBoxes).toHaveLength(1);
  });

  it("calls onToggleSelection with run id when checkbox is clicked", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn();
    render(
      <EntityDataTable
        rows={rows}
        onSelectRun={() => undefined}
        selectedRunIds={new Set()}
        onToggleSelection={onToggle}
      />
    );

    await user.click(screen.getAllByRole("checkbox")[0]);

    expect(onToggle).toHaveBeenCalledTimes(1);
    expect(typeof onToggle.mock.calls[0][0]).toBe("string");
  });

  it("does not render checkboxes when onToggleSelection is omitted", () => {
    render(<EntityDataTable rows={rows} onSelectRun={() => undefined} />);

    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });
});
