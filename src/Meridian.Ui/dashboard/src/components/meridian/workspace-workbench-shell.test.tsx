import { render, screen } from "@testing-library/react";
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
