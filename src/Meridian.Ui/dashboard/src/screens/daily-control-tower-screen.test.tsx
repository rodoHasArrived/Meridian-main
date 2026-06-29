import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import type {
  AppShellDecisionBrief,
  AppShellEvidenceTimelineItem,
  AppShellLinkedContextItem,
  AppShellOperatorFocusItem,
  AppShellWorkflowContinuityViewModel
} from "@/lib/app-shell-workflow-continuity";
import { DailyControlTowerScreen } from "@/screens/daily-control-tower-screen";

describe("DailyControlTowerScreen", () => {
  it("renders the default landing queue with a shared proof passport", () => {
    render(
      <MemoryRouter>
        <DailyControlTowerScreen viewModel={workflowViewModel()} trustStrip={trustStrip()} />
      </MemoryRouter>
    );

    expect(screen.getByRole("heading", { name: "What needs an operator decision now" })).toBeInTheDocument();
    const queue = screen.getByRole("table", { name: "Daily control tower blocked output queue" });
    expect(within(queue).getByText("Brokerage sync failed")).toBeInTheDocument();

    const passport = within(queue).getByLabelText("Brokerage sync failed Proof Passport");
    expect(within(passport).getByRole("heading", { name: "Proof Passport" })).toBeInTheDocument();
    expect(within(passport).getByText(/Proof Passport for Settings: Brokerage sync failed/)).toBeInTheDocument();

    for (const label of ["Source", "Freshness", "Reconciliation", "Approvals", "Report Usage", "Blockers", "Evidence Packet", "Audit Trail"]) {
      expect(within(passport).getByText(label)).toBeInTheDocument();
    }

    expect(within(passport).getByText("Brokerage sync failed / 2026-05-14 20:00 UTC").closest("a"))
      .toHaveAttribute("href", "/settings#alpaca-provider-setup");
    expect(within(passport).getAllByText("Route-owned").length).toBeGreaterThan(0);
    expect(within(passport).getByText("Blocked")).toBeInTheDocument();
  });
});

function workflowViewModel(): AppShellWorkflowContinuityViewModel {
  return {
    title: "Daily workflow",
    summary: "Operator workflow summary.",
    primaryOperatorFlowLabel: "Primary operator workflow",
    primaryOperatorFlowSummary: "Import -> Validate -> Reconcile -> Investigate -> Approve -> Report",
    primaryOperatorFlowStepsLabel: "Primary operator workflow steps",
    primaryOperatorFlowSteps: [],
    contextLabel: "Operating context",
    contextValue: "Settings / Provider setup",
    subjectSymbol: null,
    clearSubjectAriaLabel: null,
    operatingScope: {
      label: "Operating scope",
      hasScope: false,
      summary: "All workspaces",
      subjectSymbol: null,
      fundAccountId: null,
      runId: null,
      provider: null,
      clearAriaLabel: null,
      items: [],
      queryParams: []
    },
    routeLabel: "/",
    stepsLabel: "Daily workflow steps",
    ariaLabel: "Daily workflow continuity",
    nextActionLabel: "Fix provider setup",
    nextActionAriaLabel: "Settings: Brokerage sync failed. Fix provider setup.",
    nextActionHref: "/settings#alpaca-provider-setup",
    decisionBrief: decisionBrief(),
    steps: [],
    disclosure: {
      label: "Workflow details",
      summary: "Operator focus and evidence.",
      panels: []
    },
    operatorFocusLabel: "Operator focus",
    operatorFocusSummary: "1 focus item across workspaces: 1 blocked.",
    operatorFocusEmptyText: "Loaded workspaces have no ranked blockers.",
    operatorFocusItemsLabel: "Ranked cross-workspace operator focus items",
    operatorFocusOverflowLabel: null,
    operatorFocusItems: [
      operatorFocus({
        id: "focus:settings",
        label: "Brokerage sync failed",
        workspaceLabel: "Settings",
        route: "/settings#alpaca-provider-setup",
        actionLabel: "Fix provider setup",
        tone: "blocked"
      })
    ],
    operatorFocusCommandItems: [],
    evidenceTimelineLabel: "Evidence timeline",
    evidenceTimelineSummary: "1 evidence event across 1 workspace.",
    evidenceTimelineEmptyText: "No evidence.",
    evidenceTimelineItemsLabel: "Recent cross-workspace evidence events",
    evidenceTimelineOverflowLabel: null,
    evidenceTimelineItems: [
      evidenceItem({
        id: "focus:settings",
        label: "Brokerage sync failed",
        workspaceLabel: "Settings",
        timestampLabel: "2026-05-14 20:00 UTC",
        route: "/settings#alpaca-provider-setup",
        tone: "blocked"
      })
    ],
    linkedContextLabel: "Linked context",
    linkedContextSummary: "Related workspace context.",
    linkedContextPostureLabel: "Ready",
    linkedContextPostureTone: "ready",
    linkedContextPrimaryActionLabel: null,
    linkedContextPrimaryActionHref: null,
    linkedContextPrimaryActionAriaLabel: null,
    linkedContextEmptyText: "No linked context.",
    linkedContextItemsLabel: "Linked context routes",
    linkedContextItems: [linkedContextItem()]
  };
}

function decisionBrief(overrides: Partial<AppShellDecisionBrief> = {}): AppShellDecisionBrief {
  return {
    label: "Decision brief",
    title: "Resolve Brokerage sync failed",
    summary: "Settings is the highest-priority loaded issue.",
    reasonLabel: "Why now",
    reason: "Account sync failed after the last provider heartbeat.",
    statusLabel: "Blocked",
    statusTone: "blocked",
    evidenceLabel: "Latest evidence: Settings 2026-05-14 20:00 UTC",
    actionLabel: "Fix provider setup",
    actionHref: "/settings#alpaca-provider-setup",
    actionAriaLabel: "Settings: Brokerage sync failed. Fix provider setup.",
    ...overrides
  };
}

function operatorFocus(overrides: Partial<AppShellOperatorFocusItem> = {}): AppShellOperatorFocusItem {
  return {
    id: "focus:settings",
    label: "Brokerage sync failed",
    detail: "Account sync failed after the last provider heartbeat.",
    route: "/settings#alpaca-provider-setup",
    workspaceLabel: "Settings",
    actionLabel: "Fix provider setup",
    tone: "blocked",
    ariaLabel: "Settings: Brokerage sync failed. Fix provider setup.",
    ...overrides
  };
}

function evidenceItem(overrides: Partial<AppShellEvidenceTimelineItem> = {}): AppShellEvidenceTimelineItem {
  return {
    id: "focus:settings",
    label: "Brokerage sync failed",
    detail: "Audit: audit-1.",
    route: "/settings#alpaca-provider-setup",
    workspaceLabel: "Settings",
    timestampLabel: "2026-05-14 20:00 UTC",
    timestampIso: "2026-05-14T20:00:00Z",
    tone: "blocked",
    ariaLabel: "Settings: Brokerage sync failed. Audit: audit-1. Open evidence.",
    ...overrides
  };
}

function linkedContextItem(overrides: Partial<AppShellLinkedContextItem> = {}): AppShellLinkedContextItem {
  return {
    id: "linked-1",
    label: "Settings diagnostics",
    detail: "Provider diagnostics are available.",
    route: "/settings",
    workspaceLabel: "Settings",
    statusLabel: "Ready",
    tone: "ready",
    ariaLabel: "Settings: Settings diagnostics. Provider diagnostics are available.",
    ...overrides
  };
}

function trustStrip(): AppShellTrustStripState {
  return {
    ariaLabel: "Workstation trust strip",
    items: [
      {
        id: "source",
        label: "Source",
        value: "Fixture",
        detail: "Fixture data is loaded.",
        tone: "ready",
        ariaLabel: "Source Fixture",
        href: null,
        actionLabel: null
      }
    ]
  };
}
