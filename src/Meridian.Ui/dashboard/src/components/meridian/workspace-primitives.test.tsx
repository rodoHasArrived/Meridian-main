import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  WorkspaceDocumentCanvas,
  WorkspaceFilterBar,
  WorkspaceInspectorHost,
  WorkspaceTabStrip
} from "@/components/meridian/workspace-primitives";

describe("workspace primitives", () => {
  it("renders filter search, segment, field, and action slots", () => {
    render(
      <WorkspaceFilterBar
        label="Data filters"
        searchValue="provider: polygon"
        options={[
          { id: "all", label: "All", count: "4", active: true },
          { id: "blocked", label: "Blocked", count: "1" }
        ]}
        fields={[{ id: "sync", label: "Sync", value: "Healthy" }]}
        actions={<button type="button">Import</button>}
      />
    );

    expect(screen.getByRole("region", { name: "Data filters" })).toBeInTheDocument();
    expect(screen.getByRole("search", { name: "Search" })).toHaveTextContent("provider: polygon");
    expect(screen.getByText("All")).toHaveAttribute("aria-current", "true");
    expect(screen.getByText("Sync").closest("dl")).toHaveTextContent("Healthy");
    expect(screen.getByRole("button", { name: "Import" })).toBeInTheDocument();
  });

  it("renders tabs with accessible selection and invokes tab changes", () => {
    const onSelect = vi.fn();

    render(
      <WorkspaceTabStrip
        label="Provider detail tabs"
        tabs={[
          { id: "overview", label: "Overview", selected: true, panelId: "overview-panel" },
          { id: "diagnostics", label: "Diagnostics", panelId: "diagnostics-panel" }
        ]}
        onSelect={onSelect}
      />
    );

    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute("aria-selected", "true");
    fireEvent.click(screen.getByRole("tab", { name: "Diagnostics" }));
    expect(onSelect).toHaveBeenCalledWith("diagnostics");
  });

  it("frames inspector and document canvas regions", () => {
    render(
      <>
        <WorkspaceInspectorHost label="Provider inspector" title="Polygon" subtitle="Healthy">
          <p>Trust evidence</p>
        </WorkspaceInspectorHost>
        <WorkspaceDocumentCanvas label="Report canvas" title="Investor statement">
          <p>Report page</p>
        </WorkspaceDocumentCanvas>
      </>
    );

    expect(screen.getByLabelText("Provider inspector")).toHaveTextContent("Polygon");
    expect(screen.getByLabelText("Provider inspector")).toHaveTextContent("Trust evidence");
    expect(screen.getByRole("region", { name: "Report canvas" })).toHaveTextContent("Investor statement");
    expect(screen.getByRole("region", { name: "Report canvas" })).toHaveTextContent("Report page");
  });
});
