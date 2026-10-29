import userEvent from "@testing-library/user-event";
import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";

describe("WorkspaceWorkbenchShell", () => {
  it("renders workbench slots in the expected order with accessible labels", () => {
    const { container } = render(
      <WorkspaceWorkbenchShell
        label="Operations record workbench"
        statusBand={<p>Status posture</p>}
        contextRail={<p>Release context</p>}
        contextRailLabel="Release context rail"
        inspector={<p>Selected release step</p>}
        inspectorLabel="Release step inspector"
        evidenceDrawer={<p>Evidence drawer rows</p>}
        evidenceDrawerLabel="Release evidence drawer"
      >
        <p>Release path canvas</p>
      </WorkspaceWorkbenchShell>
    );

    expect(screen.getByRole("region", { name: "Operations record workbench" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Operations record workbench status" })).toHaveTextContent(
      "Status posture"
    );
    expect(screen.getByLabelText("Release context rail")).toHaveTextContent("Release context");
    expect(screen.getByRole("region", { name: "Operations record workbench work surface" })).toHaveTextContent(
      "Release path canvas"
    );
    expect(screen.getByLabelText("Release step inspector")).toHaveTextContent("Selected release step");
    expect(screen.getByRole("region", { name: "Release evidence drawer" })).toHaveTextContent(
      "Evidence drawer rows"
    );

    expect(Array.from(container.querySelectorAll("[data-workbench-slot]")).map((slot) => slot.getAttribute("data-workbench-slot"))).toEqual([
      "status",
      "context",
      "main",
      "inspector",
      "evidence"
    ]);
  });

  it("keeps the inspector in a sticky layout slot and marks responsive layout state with stable classes", () => {
    const { container } = render(
      <WorkspaceWorkbenchShell
        label="Trading blotter workbench"
        contextRail={<p>Risk rail</p>}
        inspector={<p>Order inspector</p>}
      >
        <p>Blotter table</p>
      </WorkspaceWorkbenchShell>
    );

    expect(container.querySelector(".workspace-workbench-shell-layout")).toHaveClass(
      "workspace-workbench-shell-layout--with-context",
      "workspace-workbench-shell-layout--with-inspector"
    );
    expect(container.querySelector('[data-workbench-slot="inspector"]')).toHaveClass(
      "workspace-workbench-shell-inspector"
    );
  });

  it("omits optional slots and does not create nested card shells", () => {
    const { container } = render(
      <WorkspaceWorkbenchShell label="Portfolio workbench">
        <p>Holdings table</p>
      </WorkspaceWorkbenchShell>
    );

    expect(screen.getByRole("region", { name: "Portfolio workbench work surface" })).toHaveTextContent(
      "Holdings table"
    );
    expect(container.querySelector('[data-workbench-slot="status"]')).toBeNull();
    expect(container.querySelector('[data-workbench-slot="context"]')).toBeNull();
    expect(container.querySelector('[data-workbench-slot="inspector"]')).toBeNull();
    expect(container.querySelector(".card .card")).toBeNull();
  });
});


describe("workbench inspection recovery", () => {
  it("returns to the work surface when no selected row is mounted", async () => {
    const user = userEvent.setup();
    render(<WorkspaceWorkbenchShell label="Filtered queue" switchInspector inspectorRole="region" inspector={<p>Retained selection</p>}><p>No matching rows</p></WorkspaceWorkbenchShell>);
    await user.click(screen.getByText("Inspect selection"));
    expect(screen.getByText("Back to work list").closest("button")).toHaveAttribute("aria-expanded", "true");
    await user.click(screen.getByText("Back to work list"));
    await waitFor(() => expect(screen.getByRole("region", { name: "Filtered queue work surface" })).toHaveFocus());
  });
  it("leaves Escape handling to a nested dialog", async () => {
    const user = userEvent.setup();
    render(<WorkspaceWorkbenchShell label="Governed queue" switchInspector inspectorRole="region" inspector={<div role="dialog" aria-label="Review decision"><input aria-label="Rationale" /></div>}><p>Work list</p></WorkspaceWorkbenchShell>);
    await user.click(screen.getByText("Inspect selection"));
    await waitFor(() => expect(screen.getByRole("region", { name: "Selected object inspector" })).toHaveFocus());
    screen.getByRole("textbox", { name: "Rationale" }).focus();
    await user.keyboard("{Escape}");
    expect(screen.getByText("Back to work list").closest("button")).toHaveAttribute("aria-expanded", "true");
  });
});


describe("workbench proof links", () => {
  it("reveals an initial deep link and reopens the inspector when the same proof hash is activated again", async () => {
    const originalUrl = window.location.href;
    window.history.replaceState(null, "", "#retained-holding");
    try {
      const user = userEvent.setup();
      render(<><a href="#retained-holding">Holding proof</a><WorkspaceWorkbenchShell label="Holdings" switchInspector inspectorRole="region" inspector={<section id="retained-holding" tabIndex={-1} aria-label="Holding proof detail">Retained evidence</section>}><p>Holdings list</p></WorkspaceWorkbenchShell></>);
      const proof = screen.getByLabelText("Holding proof detail");
      await waitFor(() => expect(proof).toHaveFocus());
      expect(screen.getByRole("region", { name: "Holdings" })).toHaveClass("workspace-workbench-shell--detail-open");
      await user.click(screen.getByText("Back to work list"));
      await waitFor(() => expect(screen.getByRole("region", { name: "Holdings work surface" })).toHaveFocus());
      expect(window.location.hash).toBe("#retained-holding");
      await user.click(screen.getByRole("link", { name: "Holding proof" }));
      await waitFor(() => expect(proof).toHaveFocus());
      expect(screen.getByRole("region", { name: "Holdings" })).toHaveClass("workspace-workbench-shell--detail-open");
    } finally {
      window.history.replaceState(null, "", originalUrl);
    }
  });
  it("does not reveal an inspector for an unrelated hash target", () => {
    const originalUrl = window.location.href;
    window.history.replaceState(null, "", "#other-work");
    try {
      render(<WorkspaceWorkbenchShell label="Holdings" switchInspector inspector={<p id="retained-holding">Evidence</p>}><p id="other-work">Work list</p></WorkspaceWorkbenchShell>);
      expect(screen.getByRole("region", { name: "Holdings" })).not.toHaveClass("workspace-workbench-shell--detail-open");
    } finally {
      window.history.replaceState(null, "", originalUrl);
    }
  });
});
