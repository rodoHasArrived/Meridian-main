import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { WorkspaceSection } from "./workspace-section";

describe("WorkspaceSection", () => {
  it("supports standalone description preferences without duplicating opt-in heading help", () => {
    const { rerender } = render(<WorkspaceSection title="Review queue" summary="Inspect evidence." descriptionKey="main-section" />);
    expect(screen.getByRole("button", { name: "Hide description: About Review queue" })).toBeVisible();
    rerender(<WorkspaceSection title="Review queue" summary="Inspect evidence." descriptionKey="main-section" helpId="section-heading" />);
    expect(screen.getAllByText("Inspect evidence.")).toHaveLength(1);
    expect(screen.queryByRole("button", { name: "Hide description: About Review queue" })).not.toBeInTheDocument();
    expect(screen.getAllByRole("heading", { name: "Review queue" })).toHaveLength(1);
  });

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
