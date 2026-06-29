import { fireEvent, render, screen, within } from "@testing-library/react";
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
    expect(screen.getByRole("search", { name: "Search" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Search" })).toHaveValue("provider: polygon");
    expect(screen.getByRole("searchbox", { name: "Search" })).toHaveAttribute("readonly");
    const filterSegments = screen.getByRole("list", { name: "Data filters filters" });
    expect(within(filterSegments).getByText("All").closest('[role="listitem"]')).toHaveAttribute(
      "aria-current",
      "true"
    );
    expect(screen.getByText("Sync").closest("dl")).toHaveTextContent("Healthy");
    expect(screen.getByRole("button", { name: "Import" })).toBeInTheDocument();
  });

  it("exposes editable filter search changes when a handler is supplied", () => {
    const onSearchChange = vi.fn();

    render(
      <WorkspaceFilterBar
        label="Ledger filters"
        searchLabel="Search ledger"
        searchValue="cash"
        onSearchChange={onSearchChange}
      />
    );

    const search = screen.getByRole("searchbox", { name: "Search ledger" });
    fireEvent.change(search, { target: { value: "cash breaks" } });
    expect(onSearchChange).toHaveBeenCalledWith("cash breaks");
    expect(search).not.toHaveAttribute("readonly");
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
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("tab", { name: "Diagnostics" })).toHaveAttribute("tabindex", "-1");
    fireEvent.click(screen.getByRole("tab", { name: "Diagnostics" }));
    expect(onSelect).toHaveBeenCalledWith("diagnostics");

    fireEvent.keyDown(screen.getByRole("tab", { name: "Overview" }), { key: "ArrowRight" });
    expect(onSelect).toHaveBeenCalledWith("diagnostics");
    expect(screen.getByRole("tab", { name: "Diagnostics" })).toHaveFocus();

    fireEvent.keyDown(screen.getByRole("tab", { name: "Diagnostics" }), { key: "Home" });
    expect(onSelect).toHaveBeenCalledWith("overview");
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveFocus();
  });

  it("keeps stateful tab keyboard navigation complete when selection is controlled elsewhere", () => {
    const onSelect = vi.fn();

    render(
      <WorkspaceTabStrip
        label="Evidence detail tabs"
        tabs={[
          { id: "summary", label: "Summary", panelId: "summary-panel" },
          { id: "lineage", label: "Lineage", panelId: "lineage-panel" },
          { id: "audit", label: "Audit Trail", panelId: "audit-panel" }
        ]}
        onSelect={onSelect}
      />
    );

    const tablist = screen.getByRole("tablist", { name: "Evidence detail tabs" });
    expect(tablist).toHaveAttribute("aria-orientation", "horizontal");
    expect(screen.getByRole("tab", { name: "Summary" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Summary" })).toHaveAttribute("tabindex", "0");

    fireEvent.keyDown(screen.getByRole("tab", { name: "Summary" }), { key: "End" });
    expect(onSelect).toHaveBeenCalledWith("audit");
    expect(screen.getByRole("tab", { name: "Audit Trail" })).toHaveFocus();

    fireEvent.keyDown(screen.getByRole("tab", { name: "Audit Trail" }), { key: "ArrowRight" });
    expect(onSelect).toHaveBeenCalledWith("summary");
    expect(screen.getByRole("tab", { name: "Summary" })).toHaveFocus();

    fireEvent.keyDown(screen.getByRole("tab", { name: "Summary" }), { key: "ArrowLeft" });
    expect(onSelect).toHaveBeenCalledWith("audit");
    expect(screen.getByRole("tab", { name: "Audit Trail" })).toHaveFocus();
  });

  it("invokes stateful anchor tabs without converting them into navigation task strips", () => {
    const onSelect = vi.fn();

    render(
      <WorkspaceTabStrip
        label="Proof tabs"
        tabs={[
          { id: "fields", label: "Fields", selected: true, panelId: "fields-panel", href: "#fields-panel" },
          { id: "audit", label: "Audit", panelId: "audit-panel", href: "#audit-panel" }
        ]}
        onSelect={onSelect}
      />
    );

    expect(screen.getByRole("tablist", { name: "Proof tabs" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "Audit" }));
    expect(onSelect).toHaveBeenCalledWith("audit");

    fireEvent.keyDown(screen.getByRole("tab", { name: "Fields" }), { key: " " });
    expect(onSelect).toHaveBeenCalledWith("fields");

    fireEvent.keyDown(screen.getByRole("tab", { name: "Audit" }), { key: "Enter" });
    expect(onSelect).toHaveBeenCalledWith("audit");
  });

  it("renders linked task strips as navigation instead of false tabs", () => {
    render(
      <WorkspaceTabStrip
        label="Trading sub-task screens"
        tabs={[
          { id: "overview", label: "Overview", selected: true, panelId: "trading-overview", href: "#trading-overview" },
          { id: "actions", label: "Order Actions", panelId: "trading-actions", href: "#trading-actions" }
        ]}
      />
    );

    const navigation = screen.getByRole("navigation", { name: "Trading sub-task screens" });
    expect(within(navigation).getByRole("link", { name: "Overview" })).toHaveAttribute("href", "#trading-overview");
    expect(within(navigation).getByRole("link", { name: "Overview" })).toHaveAttribute("aria-current", "location");
    expect(within(navigation).getByRole("link", { name: "Order Actions" })).not.toHaveAttribute("role", "tab");
    expect(screen.queryByRole("tablist", { name: "Trading sub-task screens" })).not.toBeInTheDocument();
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
