import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import {
  DENSE_ROW_DETAIL_ACCESSIBILITY_SPEC,
  DENSE_ROW_DETAIL_KEYBOARD_INSTRUCTIONS,
  DenseRowDetailPanel
} from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";

interface ContractRow {
  id: string;
  label: string;
  status: string;
}

interface DenseRowModuleCase {
  moduleName: string;
  tableLabel: string;
  detailPanelId: string;
  detailPanelLabel: string;
}

const moduleCases: DenseRowModuleCase[] = [
  {
    moduleName: "Portfolio positions",
    tableLabel: "Portfolio positions dense row contract",
    detailPanelId: "portfolio-position-detail-contract",
    detailPanelLabel: "Portfolio holding detail contract"
  },
  {
    moduleName: "Portfolio run evidence",
    tableLabel: "Portfolio run evidence dense row contract",
    detailPanelId: "portfolio-run-evidence-detail-contract",
    detailPanelLabel: "Run evidence detail contract"
  },
  {
    moduleName: "Trading recent fills",
    tableLabel: "Trading recent fills dense row contract",
    detailPanelId: "trading-recent-fills-detail-contract",
    detailPanelLabel: "Trading fill detail contract"
  },
  {
    moduleName: "Data backfill queue",
    tableLabel: "Data backfill queue dense row contract",
    detailPanelId: "data-backfill-queue-detail-contract",
    detailPanelLabel: "Backfill queue detail contract"
  },
  {
    moduleName: "Security Master lots",
    tableLabel: "Security Master lots dense row contract",
    detailPanelId: "security-master-lots-detail-contract",
    detailPanelLabel: "Security Master lot detail contract"
  }
];

const rows: ContractRow[] = [
  { id: "first", label: "First row", status: "Open" },
  { id: "second", label: "Second row", status: "Review" }
];

const columns: DenseDataTableColumn<ContractRow>[] = [
  { id: "label", label: "Label", render: (row) => row.label },
  { id: "status", label: "Status", render: (row) => row.status }
];

describe("dense row detail accessibility contract", () => {
  it("documents the shared keyboard, focus, ARIA, and announcement rules", () => {
    expect(DENSE_ROW_DETAIL_ACCESSIBILITY_SPEC.keyboardNavigation).toContain(
      "Enter and Space select the focused row and hand focus to the controlled detail panel."
    );
    expect(DENSE_ROW_DETAIL_ACCESSIBILITY_SPEC.focusHandoff).toContain(
      "Escape resolves aria-controls back to the currently selected row before falling back to the first controlling row."
    );
    expect(DENSE_ROW_DETAIL_ACCESSIBILITY_SPEC.ariaRolesAndStates).toContain(
      "Rows expose aria-controls pointing at their detail panel and aria-expanded to mirror the selected/expanded relationship."
    );
    expect(DENSE_ROW_DETAIL_ACCESSIBILITY_SPEC.announcements).toContain(
      "Selection changes are announced through a polite, atomic live region scoped to the table."
    );
  });

  it.each(moduleCases)("applies identical keyboard and ARIA behavior to $moduleName", async (moduleCase) => {
    const user = userEvent.setup();
    const Harness = () => {
      const selectedRowId = "first";
      return (
        <div>
          <DenseDataTable
            columns={columns}
            rows={rows}
            getRowId={(row) => row.id}
            getRowAriaLabel={(row) => `${moduleCase.moduleName} ${row.label} ${row.status}`}
            getRowSelectAriaLabel={(row) => `Select ${moduleCase.moduleName} ${row.label}`}
            getRowAriaControls={() => moduleCase.detailPanelId}
            getRowAriaExpanded={(row) => row.id === selectedRowId}
            onRowSelect={() => undefined}
            selectedRowId={selectedRowId}
            emptyText="No rows"
            ariaLabel={moduleCase.tableLabel}
            caption={`${moduleCase.moduleName} shared contract regression harness.`}
          />
          <DenseRowDetailPanel id={moduleCase.detailPanelId} ariaLabel={moduleCase.detailPanelLabel}>
            <h2>{moduleCase.detailPanelLabel}</h2>
            <p>Panel content updates when selection changes.</p>
          </DenseRowDetailPanel>
        </div>
      );
    };

    render(<Harness />);

    const table = screen.getByRole("treegrid", { name: moduleCase.tableLabel });
    expect(table).toHaveAccessibleDescription(DENSE_ROW_DETAIL_KEYBOARD_INSTRUCTIONS);

    const firstRow = screen.getByRole("row", { name: `Select ${moduleCase.moduleName} First row` });
    const secondRow = screen.getByRole("row", { name: `Select ${moduleCase.moduleName} Second row` });
    const panel = screen.getByRole("region", { name: moduleCase.detailPanelLabel });

    expect(firstRow).toHaveAttribute("aria-controls", moduleCase.detailPanelId);
    expect(firstRow).toHaveAttribute("aria-expanded", "true");
    expect(firstRow).toHaveAttribute("aria-selected", "true");
    expect(firstRow).toHaveAttribute("tabindex", "0");
    expect(secondRow).toHaveAttribute("aria-controls", moduleCase.detailPanelId);
    expect(secondRow).toHaveAttribute("aria-expanded", "false");
    expect(secondRow).not.toHaveAttribute("aria-selected");
    expect(panel).toHaveAttribute("aria-live", "polite");
    expect(panel).toHaveAttribute("tabindex", "-1");

    firstRow.focus();
    await user.keyboard("{ArrowDown}");
    expect(secondRow).toHaveFocus();
    expect(within(table.parentElement as HTMLElement).getByRole("status")).toHaveTextContent(
      `Select ${moduleCase.moduleName} Second row selected. Detail panel updated.`
    );

    await user.keyboard(" ");
    await waitFor(() => expect(panel).toHaveFocus());

    await user.keyboard("{Escape}");
    expect(firstRow).toHaveFocus();
  });
});
