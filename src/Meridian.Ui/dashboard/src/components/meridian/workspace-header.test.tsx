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
        workspace={workspaceForKey("trading")}
        session={session}
        onOpenCommandPalette={vi.fn()}
        onRefresh={vi.fn()}
        refreshing
      />
    );

    expect(screen.getByRole("heading", { name: "Trading Workstation" })).toBeInTheDocument();
    expect(screen.getByLabelText("paper environment")).toHaveTextContent("PAPER");
    expect(screen.getByLabelText("Trading workspace status Review")).toHaveTextContent("Review");
    expect(screen.getByLabelText("Session Ops Desk, role Operator")).toHaveTextContent("Ops Desk");
    expect(screen.getByRole("button", { name: "Refreshing Trading workspace data" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Open workspace command palette" })).toHaveTextContent(
      "Open command palette"
    );
    expect(screen.getByText("Refreshing Trading workspace data.")).toHaveClass("sr-only");
  });
});
