import { describe, expect, it } from "vitest";
import { buildWorkflowContinuityViewModel } from "@/app-shell.workflow-continuity-view-model";
import { workspaceForPath } from "@/lib/workspace";

function buildViewModel(pathname: string, hash = "") {
  return buildWorkflowContinuityViewModel(
    pathname,
    "",
    hash,
    workspaceForPath(pathname)
  );
}

describe("workflow continuity view model route modes", () => {
  it.each([
    ["/accounting", "Accounting", "reconcile"],
    ["/reporting", "Reporting", "report"],
    ["/data", "Data", "import"],
    ["/settings", "Settings", null]
  ])(
    "builds a neutral task choice for the unmatched root %s",
    (pathname, workspaceLabel, activePrimaryStepId) => {
      const viewModel = buildViewModel(pathname);

      expect(viewModel).toMatchObject({
        mode: "choose-task",
        title: "Choose a task",
        contextValue: `${workspaceLabel} / Choose a task`,
        routeLabel: pathname,
        stepsLabel: `${workspaceLabel} task workflow steps`,
        ariaLabel: `${workspaceLabel} task choice`,
        nextActionLabel: "Choose a task",
        nextActionAriaLabel: `Choose a task in ${workspaceLabel}`,
        nextActionHref: pathname,
        steps: []
      });
      expect(viewModel.summary).toContain(`No continuity step is selected for ${workspaceLabel}.`);
      expect(viewModel.primaryOperatorFlowSteps.filter((step) => step.active).map((step) => step.id))
        .toEqual(activePrimaryStepId ? [activePrimaryStepId] : []);
    }
  );

  it("builds a hidden recovery model for an unknown route", () => {
    const viewModel = buildViewModel("/unknown");

    expect(viewModel).toMatchObject({
      mode: "hidden",
      title: "Choose a task",
      routeLabel: "/unknown",
      nextActionLabel: "Open Daily Control Tower",
      nextActionAriaLabel: "Open Daily Control Tower to choose a task",
      nextActionHref: "/",
      steps: []
    });
    expect(viewModel.primaryOperatorFlowSteps.some((step) => step.active)).toBe(false);
  });

  it("keeps the provider setup hash as the current route-specific step", () => {
    const viewModel = buildViewModel("/settings", "#alpaca-provider-setup");

    expect(viewModel).toMatchObject({
      mode: "matched",
      title: "Market Data To Paper",
      contextValue: "Settings / Provider setup",
      nextActionLabel: "Stay on Provider setup",
      nextActionHref: "/settings#alpaca-provider-setup"
    });
    expect(viewModel.steps.find((step) => step.id === "provider-setup")).toMatchObject({
      active: true,
      ariaLabel: "Provider setup, current workflow step, Waiting"
    });
  });

  it("keeps the Reporting report-pack route matched", () => {
    const viewModel = buildViewModel("/reporting/report-packs");

    expect(viewModel).toMatchObject({
      mode: "matched",
      title: "Daily Control Tower",
      contextValue: "Reporting / Reports",
      nextActionLabel: "Next: Evidence"
    });
    expect(viewModel.steps.find((step) => step.id === "reports")).toMatchObject({
      active: true,
      href: "/reporting/report-packs"
    });
  });
});
