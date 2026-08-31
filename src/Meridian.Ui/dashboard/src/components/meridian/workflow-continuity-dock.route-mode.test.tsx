import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { buildWorkflowContinuityViewModel } from "@/app-shell.workflow-continuity-view-model";
import { WorkflowContinuityDock } from "@/components/meridian/workflow-continuity-dock";
import { workspaceForPath } from "@/lib/workspace";

function buildViewModel(pathname: string) {
  return buildWorkflowContinuityViewModel(
    pathname,
    "",
    "",
    workspaceForPath(pathname)
  );
}

describe("WorkflowContinuityDock route modes", () => {
  it("renders neutral task choice without an empty route workflow navigation", async () => {
    const user = userEvent.setup();
    const viewModel = buildViewModel("/settings");

    render(
      <MemoryRouter>
        <WorkflowContinuityDock viewModel={viewModel} />
      </MemoryRouter>
    );

    expect(screen.getByRole("region", { name: "Settings task choice" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Choose a task" })).toBeTruthy();
    expect(screen.queryByRole("navigation", { name: "Settings task workflow steps" })).toBeNull();

    await user.click(screen.getByText("Flow details"));

    expect(screen.queryByRole("navigation", { name: "Settings task workflow steps" })).toBeNull();
    const primaryFlow = screen.getByRole("navigation", { name: "Primary operator workflow steps" });
    expect(within(primaryFlow).queryByRole("link", { current: "step" })).toBeNull();
  });

  it("removes hidden continuity from the accessibility tree", () => {
    const viewModel = buildViewModel("/unknown");
    const { container } = render(
      <MemoryRouter>
        <WorkflowContinuityDock viewModel={viewModel} />
      </MemoryRouter>
    );

    expect(viewModel.mode).toBe("hidden");
    expect(container.childElementCount).toBe(0);
    expect(screen.queryByRole("region")).toBeNull();
  });
});
