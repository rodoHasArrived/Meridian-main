import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { OperatingScopeChip, WorkflowContextDisclosure } from "@/components/meridian/workflow-continuity-dock";
import type { AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";

const workflowViewModel: AppShellWorkflowContinuityViewModel = {
  mode: "matched",
  title: "Market Data To Paper",
  summary: "Continue quote validation before paper trading.",
  primaryOperatorFlowLabel: "Primary operator workflow",
  primaryOperatorFlowSummary: "Import -> Validate -> Reconcile -> Investigate -> Approve -> Report",
  primaryOperatorFlowStepsLabel: "Primary operator workflow steps",
  primaryOperatorFlowSteps: [
    {
      id: "validate",
      label: "Validate",
      description: "Current validation step",
      href: "/data/backfills?symbol=MSFT",
      active: true,
      statusLabel: "Current",
      statusTone: "review",
      ariaLabel: "Validate, current primary operator workflow step, Current"
    },
    {
      id: "reconcile",
      label: "Reconcile",
      description: "Next reconciliation step",
      href: "/accounting/reconciliation?symbol=MSFT",
      active: false,
      statusLabel: "Next",
      statusTone: "pending",
      ariaLabel: "Reconcile, primary operator workflow step, Next"
    }
  ],
  contextLabel: "Operating context",
  contextValue: "Data / MSFT",
  subjectSymbol: "MSFT",
  clearSubjectAriaLabel: null,
  operatingScope: {
    label: "Operating scope",
    summary: "Subject: MSFT",
    subjectSymbol: "MSFT",
    fundAccountId: null,
    runId: null,
    provider: null,
    hasScope: true,
    clearAriaLabel: null,
    items: [
      {
        id: "subject",
        label: "Subject",
        value: "MSFT",
        ariaLabel: "Subject MSFT"
      }
    ],
    queryParams: [
      {
        key: "symbol",
        value: "MSFT",
        scopeKey: "symbol"
      }
    ]
  },
  routeLabel: "/data/quotes?symbol=MSFT",
  stepsLabel: "Market Data To Paper workflow steps",
  ariaLabel: "Market Data To Paper continuity",
  nextActionLabel: "Price alerts",
  nextActionAriaLabel: "Continue workflow to Price alerts",
  nextActionHref: "/data/alerts?symbol=MSFT",
  decisionBrief: {
    label: "Decision brief",
    title: "Review provider posture",
    summary: "Provider warning affects paper trading readiness.",
    reasonLabel: "Why now",
    reason: "Provider degraded before a paper workflow.",
    statusLabel: "Review",
    statusTone: "review",
    evidenceLabel: "Provider warning",
    actionLabel: "Open provider posture",
    actionHref: "/data/providers?symbol=MSFT",
    actionAriaLabel: "Open provider posture"
  },
  steps: [
    {
      id: "quotes",
      label: "Live quotes",
      description: "Current workflow step",
      href: "/data/quotes?symbol=MSFT",
      ariaLabel: "Live quotes, current workflow step, Waiting",
      statusLabel: "Waiting",
      statusTone: "pending",
      active: true,
      next: false
    },
    {
      id: "alerts",
      label: "Price alerts",
      description: "Next workflow step",
      href: "/data/alerts?symbol=MSFT",
      ariaLabel: "Price alerts, next workflow step, Waiting",
      statusLabel: "Waiting",
      statusTone: "pending",
      active: false,
      next: true
    }
  ],
  disclosure: {
    label: "Workflow context",
    summary: "Supporting workflow context",
    panels: []
  },
  operatorFocusLabel: "Operator focus",
  operatorFocusSummary: "",
  operatorFocusEmptyText: "",
  operatorFocusItemsLabel: "Operator focus items",
  operatorFocusOverflowLabel: null,
  operatorFocusItems: [],
  operatorFocusCommandItems: [],
  evidenceTimelineLabel: "Evidence timeline",
  evidenceTimelineSummary: "",
  evidenceTimelineEmptyText: "",
  evidenceTimelineItemsLabel: "Evidence timeline items",
  evidenceTimelineOverflowLabel: null,
  evidenceTimelineItems: [],
  linkedContextLabel: "Linked context",
  linkedContextSummary: "",
  linkedContextPostureLabel: "Review",
  linkedContextPostureTone: "review",
  linkedContextPrimaryActionLabel: null,
  linkedContextPrimaryActionHref: null,
  linkedContextPrimaryActionAriaLabel: null,
  linkedContextEmptyText: "",
  linkedContextItemsLabel: "Linked context items",
  linkedContextItems: []
};

describe("OperatingScopeChip", () => {
  it("shows the scope value in one chip that opens the scope picker", async () => {
    const user = userEvent.setup();
    const onEdit = vi.fn();

    render(<OperatingScopeChip viewModel={workflowViewModel} onEditOperatingContext={onEdit} />);

    expect(screen.getByRole("region", { name: "Market Data To Paper continuity" })).toBeInTheDocument();
    const chip = screen.getByRole("button", { name: "Operating scope: MSFT" });
    expect(chip).toHaveTextContent("MSFT");
    expect(chip).toHaveAttribute("aria-haspopup", "dialog");
    // The old scope row's label, action and empty-state copy are gone; so is the inline workflow detail.
    expect(screen.queryByText("Operating scope")).not.toBeInTheDocument();
    expect(screen.queryByText("Change scope")).not.toBeInTheDocument();
    expect(screen.queryByText("Workflow context")).not.toBeInTheDocument();

    await user.click(chip);
    expect(onEdit).toHaveBeenCalledTimes(1);
  });

  it("reads All funds when no scope is set", () => {
    render(
      <OperatingScopeChip
        viewModel={{ ...workflowViewModel, operatingScope: { ...workflowViewModel.operatingScope, hasScope: false, items: [] } }}
        onEditOperatingContext={vi.fn()}
      />
    );

    expect(screen.getByRole("button", { name: "Operating scope: All funds" })).toHaveTextContent("All funds");
    expect(screen.queryByText("No scope selected")).not.toBeInTheDocument();
  });

  it("distinguishes applied scope from retained scope in the chip and its description", () => {
    render(
      <OperatingScopeChip
        viewModel={{
          ...workflowViewModel,
          operatingScope: {
            ...workflowViewModel.operatingScope,
            items: [
              { id: "fundAccountId", label: "Account", value: "Desk A", ariaLabel: "Account Desk A" },
              { id: "provider", label: "Provider", value: "Source B", ariaLabel: "Provider Source B" }
            ]
          }
        }}
        onEditOperatingContext={vi.fn()}
        scopeDimensionsInEffect={["fundAccountId"]}
      />
    );

    const chip = screen.getByRole("button", { name: "Operating scope: Desk A · Source B" });
    expect(chip).toHaveAccessibleDescription(
      "Applied on this screen: Account Desk A. Retained for other screens: Provider Source B."
    );
    expect(screen.getByText("Desk A")).toHaveAttribute("title", "Account: applied here");
    expect(screen.getByText("Source B")).toHaveAttribute("title", "Provider: carried, but not applied on this screen");
  });
});

describe("WorkflowContextDisclosure", () => {
  it("keeps workflow detail collapsed until asked for", async () => {
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <WorkflowContextDisclosure viewModel={workflowViewModel} />
      </MemoryRouter>
    );

    expect(screen.queryByRole("navigation", { name: "Market Data To Paper workflow steps" })).not.toBeInTheDocument();
    expect(screen.queryByText("Import -> Validate -> Reconcile -> Investigate -> Approve -> Report")).not.toBeInTheDocument();
    // The decision brief renders as the masthead status pill, not here.
    expect(screen.queryByRole("link", { name: "Open provider posture" })).not.toBeInTheDocument();

    await user.tab();
    expect(screen.getByText("Workflow context").closest("summary")).toHaveFocus();

    await user.keyboard("{Enter}");
    expect(screen.getByText("Primary operator workflow")).toBeInTheDocument();
    expect(screen.getByText("Current workflow")).toBeInTheDocument();
    expect(screen.getByText("Import -> Validate -> Reconcile -> Investigate -> Approve -> Report")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Market Data To Paper workflow steps" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Price alerts, next workflow step, Waiting" })).toHaveAttribute(
      "href",
      "/data/alerts?symbol=MSFT"
    );
  });
});
