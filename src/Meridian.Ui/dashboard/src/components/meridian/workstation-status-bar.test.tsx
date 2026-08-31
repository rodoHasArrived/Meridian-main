import { render, screen } from "@testing-library/react";
import { WorkstationStatusBar, buildWorkstationStatusItems } from "./workstation-status-bar";

describe("WorkstationStatusBar", () => {
  it("renders status items with pushed workspace layout marker", () => {
    render(
      <WorkstationStatusBar
        items={[
          { key: "session", label: "Session", value: "paper", status: "ok" },
          { key: "data", label: "Data", value: "demo fixtures", status: "warn" },
          { key: "sync", label: "Sync", value: "failed", status: "err" },
          { key: "workspace", label: "Workspace", value: "Accounting", push: true }
        ]}
      />
    );

    expect(screen.getByRole("contentinfo", { name: "Workstation status" })).toBeInTheDocument();
    expect(screen.getByText("paper")).toBeInTheDocument();
    expect(screen.getByText("demo fixtures")).toBeInTheDocument();
    expect(screen.getByText("failed")).toBeInTheDocument();
    expect(screen.getByText("Accounting").closest(".workstation-statusbar-item"))
      .toHaveClass("workstation-statusbar-item-push");
    expect(document.querySelector(".workstation-statusbar-dot-ok")).toBeInTheDocument();
    expect(document.querySelector(".workstation-statusbar-dot-warn")).toBeInTheDocument();
    expect(document.querySelector(".workstation-statusbar-dot-err")).toBeInTheDocument();
  });

  it("builds ok, warn, and error status text from shell state", () => {
    const items = buildWorkstationStatusItems({
      workspaceLabel: "Reporting",
      refreshing: true,
      hasError: true
    });

    expect(items).toEqual([
      { key: "sync", status: "err", label: "Sync", value: "attention required" },
      { key: "workspace", label: "Workspace", value: "Reporting", push: true }
    ]);
  });

  it("keeps the footer focused on current sync and workspace context", () => {
    expect(buildWorkstationStatusItems({
      workspaceLabel: "Portfolio",
      refreshing: false,
      hasError: false
    })).toEqual([
      { key: "sync", status: "ok", label: "Sync", value: "up to date" },
      { key: "workspace", label: "Workspace", value: "Portfolio", push: true }
    ]);
  });
});
