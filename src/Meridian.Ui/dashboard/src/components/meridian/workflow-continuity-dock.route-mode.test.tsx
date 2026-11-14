import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { buildWorkflowContinuityViewModel } from "@/app-shell.workflow-continuity-view-model";
import { WorkflowContextDisclosure } from "@/components/meridian/workflow-continuity-dock";
import { OperatingScopeBar } from "@/components/meridian/operating-scope-bar";
import { workspaceForPath } from "@/lib/workspace";

function buildViewModel(pathname: string) {
  return buildWorkflowContinuityViewModel(
    pathname,
    "",
    "",
    workspaceForPath(pathname)
  );
}

describe("Operating scope and workflow context route modes", () => {
  it("renders neutral task choice without an empty route workflow navigation", async () => {
    const user = userEvent.setup();
    const viewModel = buildViewModel("/settings");

    render(
      <MemoryRouter>
        <OperatingScopeBar operatingScope={viewModel.operatingScope} onEditOperatingScope={() => undefined} />
        <WorkflowContextDisclosure viewModel={viewModel} />
      </MemoryRouter>
    );

    expect(screen.getByRole("button", { name: "Operating scope: Account All authorized, Period All authorized" })).toBeInTheDocument();
    expect(screen.queryByRole("navigation", { name: "Settings task workflow steps" })).toBeNull();

    await user.click(screen.getByText("Workflow context"));

    expect(screen.queryByRole("navigation", { name: "Settings task workflow steps" })).toBeNull();
    expect(screen.getByText("Choose a task", { selector: "span" })).toBeInTheDocument();
    const primaryFlow = screen.getByRole("navigation", { name: "Primary operator workflow steps" });
    expect(within(primaryFlow).queryByRole("link", { current: "step" })).toBeNull();
  });

  it("removes hidden workflow context from the accessibility tree but keeps the scope bar", () => {
    const viewModel = buildViewModel("/unknown");
    const { container } = render(
      <MemoryRouter>
        <OperatingScopeBar operatingScope={viewModel.operatingScope} onEditOperatingScope={() => undefined} />
        <WorkflowContextDisclosure viewModel={viewModel} />
      </MemoryRouter>
    );

    expect(viewModel.mode).toBe("hidden");
    // The scope still carries on an unknown route, so the bar stays; the workflow detail does not.
    expect(container.childElementCount).toBe(1);
    expect(screen.getByRole("region", { name: "Operating scope bar" })).toBeInTheDocument();
    expect(screen.queryByText("Workflow context")).toBeNull();
  });
});
