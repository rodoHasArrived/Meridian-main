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
    expect(screen.getByRole("searchbox", { name: "Search" })).toHaveAttribute("type", "search");
    expect(screen.getByRole("searchbox", { name: "Search" })).toHaveAttribute("readonly");
    const filterSegments = screen.getByRole("list", { name: "Data filters filters" });
    expect(within(filterSegments).getByText("All").closest('[role="listitem"]')).toHaveAttribute(
      "aria-current",
      "true"
    );
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
    expect(screen.getByRole("tablist", { name: "Provider detail tabs" })).toHaveAttribute("aria-orientation", "horizontal");
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("tab", { name: "Diagnostics" })).toHaveAttribute("tabindex", "-1");
    fireEvent.click(screen.getByRole("tab", { name: "Diagnostics" }));
    expect(onSelect).toHaveBeenCalledWith("diagnostics");
  });

  it("supports roving focus keyboard behavior for tabs", () => {
    render(
      <WorkspaceTabStrip
        label="Provider detail tabs"
        tabs={[
          { id: "overview", label: "Overview", selected: true, panelId: "overview-panel" },
          { id: "diagnostics", label: "Diagnostics", panelId: "diagnostics-panel" },
          { id: "history", label: "History", panelId: "history-panel" }
        ]}
      />
    );

    const overview = screen.getByRole("tab", { name: "Overview" });
    const diagnostics = screen.getByRole("tab", { name: "Diagnostics" });
    const history = screen.getByRole("tab", { name: "History" });

    overview.focus();
    fireEvent.keyDown(overview, { key: "ArrowRight" });
    expect(diagnostics).toHaveFocus();
    expect(overview).toHaveAttribute("tabindex", "-1");
    expect(diagnostics).toHaveAttribute("tabindex", "0");

    fireEvent.keyDown(diagnostics, { key: "End" });
    expect(history).toHaveFocus();
    expect(diagnostics).toHaveAttribute("tabindex", "-1");
    expect(history).toHaveAttribute("tabindex", "0");

    fireEvent.keyDown(history, { key: "Home" });
    expect(overview).toHaveFocus();
    expect(overview).toHaveAttribute("tabindex", "0");
    expect(history).toHaveAttribute("tabindex", "-1");

    fireEvent.keyDown(overview, { key: "ArrowLeft" });
    expect(history).toHaveFocus();
    expect(history).toHaveAttribute("tabindex", "0");
    expect(overview).toHaveAttribute("tabindex", "-1");
  });

  it("activates focused button tabs with Enter and Space", () => {
    const onSelect = vi.fn();

    render(
      <WorkspaceTabStrip
        label="Provider detail tabs"
        tabs={[
          { id: "overview", label: "Overview", selected: true, panelId: "overview-panel" },
          { id: "diagnostics", label: "Diagnostics", panelId: "diagnostics-panel" },
          { id: "history", label: "History", panelId: "history-panel" }
        ]}
        onSelect={onSelect}
      />
    );

    const overview = screen.getByRole("tab", { name: "Overview" });
    const diagnostics = screen.getByRole("tab", { name: "Diagnostics" });
    const history = screen.getByRole("tab", { name: "History" });

    overview.focus();
    fireEvent.keyDown(overview, { key: "ArrowRight" });
    fireEvent.keyDown(diagnostics, { key: "Enter" });
    expect(onSelect).toHaveBeenCalledWith("diagnostics");

    fireEvent.keyDown(diagnostics, { key: "ArrowRight" });
    fireEvent.keyDown(history, { key: " " });
    expect(onSelect).toHaveBeenCalledWith("history");
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
