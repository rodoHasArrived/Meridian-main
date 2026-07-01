import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { WorkspaceSection } from "./workspace-section";

describe("WorkspaceSection", () => {
  it("renders title, summary, and body content", () => {
    render(
      <WorkspaceSection id="recon" title="Reconciliation" summary="3 open exceptions">
        <p>lane content</p>
      </WorkspaceSection>,
    );
    expect(screen.getByRole("heading", { name: "Reconciliation" })).toBeInTheDocument();
    expect(screen.getByText("3 open exceptions")).toBeInTheDocument();
    expect(screen.getByText("lane content")).toBeInTheDocument();
  });

  it("renders the jump affordance as an anchor when jumpHref is set", () => {
    render(<WorkspaceSection title="R" jump="Open lane" jumpHref="#recon" />);
    expect(screen.getByRole("link", { name: "Open lane" })).toHaveAttribute("href", "#recon");
  });

  it("renders the jump affordance as a button and fires onJump", async () => {
    const onJump = vi.fn();
    render(<WorkspaceSection title="R" jump="Open lane" onJump={onJump} />);
    await userEvent.click(screen.getByRole("button", { name: "Open lane" }));
    expect(onJump).toHaveBeenCalledOnce();
  });
});
