import type { AppShellLinkedContextItem } from "@/app-shell.linked-context";
import type { AppShellOperatingScopeState } from "@/app-shell.operating-scope";
import type { PrimaryOperatorWorkflowStepId } from "@/app-shell.workflow-continuity";

export interface AppShellWorkflowContinuityStep {
  id: string;
  label: string;
  description: string;
  href: string;
  ariaLabel: string;
  statusLabel: string;
  statusTone: AppShellWorkflowContinuityStatusTone;
  active: boolean;
  next: boolean;
}

export type AppShellWorkflowContinuityStatusTone = "ready" | "review" | "blocked" | "pending";

export interface AppShellPrimaryOperatorWorkflowStep {
  id: PrimaryOperatorWorkflowStepId;
  label: string;
  description: string;
  href: string;
  active: boolean;
  statusLabel: string;
  statusTone: AppShellWorkflowContinuityStatusTone;
  ariaLabel: string;
}

export interface AppShellDecisionBrief {
  label: string;
  title: string;
  summary: string;
  reasonLabel: string;
  reason: string;
  statusLabel: string;
  statusTone: AppShellWorkflowContinuityStatusTone;
  evidenceLabel: string | null;
  actionLabel: string;
  actionHref: string;
  actionAriaLabel: string;
}

export interface AppShellWorkflowContinuityViewModel {
  title: string;
  summary: string;
  primaryOperatorFlowLabel: string;
  primaryOperatorFlowSummary: string;
  primaryOperatorFlowStepsLabel: string;
  primaryOperatorFlowSteps: AppShellPrimaryOperatorWorkflowStep[];
  contextLabel: string;
  contextValue: string;
  subjectSymbol: string | null;
  clearSubjectAriaLabel: string | null;
  operatingScope: AppShellOperatingScopeState;
  routeLabel: string;
  stepsLabel: string;
  ariaLabel: string;
  nextActionLabel: string;
  nextActionAriaLabel: string;
  nextActionHref: string;
  decisionBrief: AppShellDecisionBrief;
  steps: AppShellWorkflowContinuityStep[];
  disclosure: AppShellWorkflowContinuityDisclosureState;
  operatorFocusLabel: string;
  operatorFocusSummary: string;
  operatorFocusEmptyText: string;
  operatorFocusItemsLabel: string;
  operatorFocusOverflowLabel: string | null;
  operatorFocusItems: AppShellOperatorFocusItem[];
  operatorFocusCommandItems: AppShellOperatorFocusItem[];
  evidenceTimelineLabel: string;
  evidenceTimelineSummary: string;
  evidenceTimelineEmptyText: string;
  evidenceTimelineItemsLabel: string;
  evidenceTimelineOverflowLabel: string | null;
  evidenceTimelineItems: AppShellEvidenceTimelineItem[];
  linkedContextLabel: string;
  linkedContextSummary: string;
  linkedContextPostureLabel: string;
  linkedContextPostureTone: AppShellWorkflowContinuityStatusTone;
  linkedContextPrimaryActionLabel: string | null;
  linkedContextPrimaryActionHref: string | null;
  linkedContextPrimaryActionAriaLabel: string | null;
  linkedContextEmptyText: string;
  linkedContextItemsLabel: string;
  linkedContextItems: AppShellLinkedContextItem[];
}

export type AppShellWorkflowContinuityDisclosurePanelId = "linked-context" | "operator-focus" | "evidence-timeline";

export interface AppShellWorkflowContinuityDisclosurePanel {
  id: AppShellWorkflowContinuityDisclosurePanelId;
  label: string;
  summary: string;
  ariaLabel: string;
  defaultExpanded: boolean;
}

export interface AppShellWorkflowContinuityDisclosureState {
  label: string;
  summary: string;
  panels: AppShellWorkflowContinuityDisclosurePanel[];
}

export interface AppShellOperatorFocusItem {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  actionLabel: string;
  tone: AppShellWorkflowContinuityStatusTone;
  ariaLabel: string;
}

export interface AppShellEvidenceTimelineItem {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  timestampLabel: string;
  timestampIso: string;
  tone: AppShellWorkflowContinuityStatusTone;
  ariaLabel: string;
}
