import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { WorkspaceHeader } from "@/components/meridian/workspace-header";
import { workspaceForKey } from "@/lib/workspace";
import type { SessionInfo } from "@/types";

const session: SessionInfo = {
  displayName: "Ops Desk",
  role: "Operator",
  environment: "paper",
  activeWorkspace: "trading",
  commandCount: 8
};

describe("WorkspaceHeader", () => {
  it("renders view-model derived labels and loading refresh state", () => {
    render(
      <WorkspaceHeader
        breadcrumbItems={[{ label: "Workstation" }, { label: "Trading", current: true }]}
        workspace={workspaceForKey("trading")}
        session={session}
        onRefresh={vi.fn()}
        refreshing
      />
    );

    expect(screen.getByRole("heading", { name: "Trading Workstation" })).toBeInTheDocument();
    expect(screen.getByLabelText("Breadcrumb")).toHaveTextContent("Workstation");
    expect(screen.getByText("Trading")).toHaveAttribute("aria-current", "page");
    expect(screen.queryByLabelText("paper environment")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Trading product maturity Available")).toHaveTextContent("Available");
    expect(screen.getByLabelText("Session Ops Desk, role Operator")).toHaveTextContent("Ops Desk");
    const refreshButton = screen.getByRole("button", { name: "Refreshing Trading workspace data" });
    expect(refreshButton).toBeDisabled();
    expect(refreshButton).toHaveAttribute("title", "Trading workspace data is refreshing.");
    expect(refreshButton.querySelector("svg")).toHaveClass("animate-spin");
    expect(screen.queryByLabelText("Canonical route /trading")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("8 commands available in the command palette")).not.toBeInTheDocument();
    expect(screen.getByText("Refreshing Trading workspace data.")).toHaveClass("sr-only");
  });

  it("renders shell-provided actions before the refresh button", () => {
    render(
      <WorkspaceHeader
        workspace={workspaceForKey("trading")}
        session={session}
        onRefresh={vi.fn()}
        actions={<button type="button">Copy link to this view</button>}
      />
    );

    const buttons = screen.getAllByRole("button");
    expect(buttons[0]).toHaveTextContent("Copy link to this view");
    expect(screen.getByRole("button", { name: /Refresh Trading workspace data/ })).toBeInTheDocument();
  });
});
