import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";

interface Row {
  id: string;
  index: number;
  name: string;
}

const DETAIL_PANEL_ID = "virtual-grid-detail";
const ROW_HEIGHT = 20;
const VIEWPORT_ROWS = 10;

function buildRows(count: number): Row[] {
  return Array.from({ length: count }, (_, index) => ({
    id: `row-${index}`,
    index,
    name: index === 45 ? "Zebra account" : `Item ${index}`
  }));
}

const columns: DenseDataTableColumn<Row>[] = [
  { id: "name", label: "Name", render: (row) => row.name },
  { id: "index", label: "Index", render: (row) => String(row.index) }
];

function Harness({ rows, selectedRowId = "row-0" }: { rows: Row[]; selectedRowId?: string }) {
  return (
    <div>
      <DenseDataTable
        columns={columns}
        rows={rows}
        getRowId={(row) => row.id}
        getRowAriaLabel={(row) => `${row.name} row`}
        getRowSelectAriaLabel={(row) => `Select ${row.name}`}
        getRowAriaControls={() => DETAIL_PANEL_ID}
        getRowAriaExpanded={(row) => row.id === selectedRowId}
        getRowTypeaheadText={(row) => row.name}
        selectedRowId={selectedRowId}
        onRowSelect={() => undefined}
        emptyText="No rows"
        ariaLabel="Virtualized grid"
        virtualization={{ rowHeight: ROW_HEIGHT, viewportRowCount: VIEWPORT_ROWS, overscan: 4 }}
      />
      <DenseRowDetailPanel id={DETAIL_PANEL_ID} ariaLabel="Virtual grid detail">
        <p>Detail</p>
      </DenseRowDetailPanel>
    </div>
  );
}

function renderedRowIds(): string[] {
  return Array.from(document.querySelectorAll<HTMLElement>("tr[data-dense-row-id]")).map(
    (el) => el.getAttribute("data-dense-row-id") ?? ""
  );
}

describe("DenseDataTable virtualization", () => {
  afterEach(() => cleanup());

  it("mounts only a window of rows and exposes full-set row positions", () => {
    render(<Harness rows={buildRows(60)} />);

    const table = screen.getByRole("treegrid", { name: "Virtualized grid" });
    // aria-rowcount covers the full data set plus the header row.
    expect(table).toHaveAttribute("aria-rowcount", "61");

    const ids = renderedRowIds();
    expect(ids.length).toBeLessThan(60); // windowed, not all mounted
    expect(ids).toContain("row-0");
    expect(ids).not.toContain("row-59");

    // Header is row 1; the first data row is absolute index 0 => aria-rowindex 2.
    const firstRow = screen.getByRole("row", { name: "Select Item 0" });
    expect(firstRow).toHaveAttribute("aria-rowindex", "2");
  });

  it("navigates to a windowed-out row with End, revealing and focusing it", async () => {
    const user = userEvent.setup();
    render(<Harness rows={buildRows(60)} />);

    expect(renderedRowIds()).not.toContain("row-59");

    const firstRow = screen.getByRole("row", { name: "Select Item 0" });
    firstRow.focus();
    await user.keyboard("{End}");

    await waitFor(() => {
      const lastRow = screen.queryByRole("row", { name: "Select Item 59" });
      expect(lastRow).not.toBeNull();
      expect(lastRow).toHaveFocus();
    });
    expect(screen.getByRole("status")).toHaveTextContent("Select Item 59 selected. Detail panel updated.");
  });

  it("type-ahead jumps to a matching row outside the current window", async () => {
    const user = userEvent.setup();
    render(<Harness rows={buildRows(60)} />);

    expect(renderedRowIds()).not.toContain("row-45");

    const firstRow = screen.getByRole("row", { name: "Select Item 0" });
    firstRow.focus();
    await user.keyboard("z");

    await waitFor(() => {
      const zebra = screen.queryByRole("row", { name: "Select Zebra account" });
      expect(zebra).not.toBeNull();
      expect(zebra).toHaveFocus();
    });
  });

  it("Escape from the detail panel reveals a selected row that windowing unmounted", async () => {
    const user = userEvent.setup();
    // selectedRowId stays row-0 (controlled); navigating scrolls it out of the window.
    render(<Harness rows={buildRows(60)} selectedRowId="row-0" />);

    const firstRow = screen.getByRole("row", { name: "Select Item 0" });
    firstRow.focus();
    await user.keyboard("{End}");

    await waitFor(() => expect(renderedRowIds()).not.toContain("row-0"));

    const panel = screen.getByRole("region", { name: "Virtual grid detail" });
    panel.focus();
    await user.keyboard("{Escape}");

    await waitFor(() => {
      const revealed = screen.queryByRole("row", { name: "Select Item 0" });
      expect(revealed).not.toBeNull();
      expect(revealed).toHaveFocus();
    });
  });
});
