import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { DEFAULT_ONBOARDING_JOURNEY_ID, type OnboardingState } from "@/lib/onboarding";

// Declarative "first 10 minutes" tour: a short (<=5), route-anchored path over
// real screens. Each step is a genuine action the operator takes on the live UI;
// visiting its route marks it complete. Steps stay tied to stable route-catalog
// keys so the tour does not rot when screen internals change.

export interface OnboardingStepDefinition {
  id: string;
  title: string;
  description: string;
  /** Deep link the "Go" action navigates to. */
  href: string;
  /** Pathname that counts as completing the step when visited. */
  matchPath: string;
  actionLabel: string;
}

export interface OnboardingJourneyDefinition {
  id: string;
  label: string;
  description: string;
  steps: readonly OnboardingStepDefinition[];
}

export const ONBOARDING_JOURNEYS: readonly OnboardingJourneyDefinition[] = [
  {
    id: DEFAULT_ONBOARDING_JOURNEY_ID,
    label: "Financial operations",
    description: "Move source records through validation, reconciliation, approval, and reporting.",
    steps: [
      { id: "financial-operations:import", title: "Import source records", description: "Bring a statement into the governed intake workflow.", href: WORKSTATION_ROUTE_CATALOG.accountingStatementImport, matchPath: WORKSTATION_ROUTE_CATALOG.accountingStatementImport, actionLabel: "Open statement import" },
      { id: "financial-operations:validate", title: "Validate the ledger", description: "Trace balances and inspect the records behind them.", href: WORKSTATION_ROUTE_CATALOG.accountingLedger, matchPath: WORKSTATION_ROUTE_CATALOG.accountingLedger, actionLabel: "Open ledger" },
      { id: "financial-operations:reconcile", title: "Reconcile breaks", description: "Match source activity and investigate unresolved differences.", href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation, matchPath: WORKSTATION_ROUTE_CATALOG.accountingReconciliation, actionLabel: "Open reconciliation" },
      { id: "financial-operations:approve", title: "Review approvals", description: "Resolve controlled decisions before downstream publication.", href: WORKSTATION_ROUTE_CATALOG.accountingApprovals, matchPath: WORKSTATION_ROUTE_CATALOG.accountingApprovals, actionLabel: "Open approvals" },
      { id: "financial-operations:report", title: "Review report packs", description: "Validate the governed output and its supporting evidence.", href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks, matchPath: WORKSTATION_ROUTE_CATALOG.reportingReportPacks, actionLabel: "Open report packs" }
    ]
  },
  {
    id: "trading-portfolio",
    label: "Trading and portfolio",
    description: "Establish data trust, review readiness, and follow activity into portfolio records.",
    steps: [
      { id: "trading-portfolio:quotes", title: "Check market data", description: "Confirm a live quote and the provider behind it.", href: workstationRouteWithQuery("dataQuotes", { symbol: "AAPL" }), matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes, actionLabel: "Open quotes" },
      { id: "trading-portfolio:providers", title: "Review provider posture", description: "Check connectivity and data-quality evidence.", href: WORKSTATION_ROUTE_CATALOG.dataProviders, matchPath: WORKSTATION_ROUTE_CATALOG.dataProviders, actionLabel: "Open providers" },
      { id: "trading-portfolio:trading", title: "Open the trading workspace", description: "Review current readiness and controlled actions.", href: WORKSTATION_ROUTE_CATALOG.trading, matchPath: WORKSTATION_ROUTE_CATALOG.trading, actionLabel: "Open trading" },
      { id: "trading-portfolio:portfolio", title: "Trace portfolio impact", description: "Follow activity into positions and portfolio evidence.", href: WORKSTATION_ROUTE_CATALOG.portfolio, matchPath: WORKSTATION_ROUTE_CATALOG.portfolio, actionLabel: "Open portfolio" }
    ]
  },
  {
    id: "strategy-research",
    label: "Strategy and research",
    description: "Design, test, and promote a strategy through governed evidence gates.",
    steps: [
      { id: "strategy-research:design", title: "Design a strategy", description: "Start with a transparent, reviewable strategy definition.", href: WORKSTATION_ROUTE_CATALOG.strategyDesigner, matchPath: WORKSTATION_ROUTE_CATALOG.strategyDesigner, actionLabel: "Open designer" },
      { id: "strategy-research:test", title: "Run the strategy lab", description: "Evaluate the strategy with reproducible inputs.", href: WORKSTATION_ROUTE_CATALOG.strategyLab, matchPath: WORKSTATION_ROUTE_CATALOG.strategyLab, actionLabel: "Open strategy lab" },
      { id: "strategy-research:promote", title: "Review promotion gates", description: "Inspect evidence before advancing a strategy.", href: WORKSTATION_ROUTE_CATALOG.strategyPromotions, matchPath: WORKSTATION_ROUTE_CATALOG.strategyPromotions, actionLabel: "Open promotions" }
    ]
  },
  {
    id: "administration",
    label: "Administration",
    description: "Configure providers, access, accounting systems, and diagnostic recovery.",
    steps: [
      { id: "administration:providers", title: "Configure providers", description: "Connect and verify an operational data provider.", href: WORKSTATION_ROUTE_CATALOG.settingsProviders, matchPath: WORKSTATION_ROUTE_CATALOG.settingsProviders, actionLabel: "Open provider settings" },
      { id: "administration:access", title: "Review access", description: "Inspect the controls governing operator access.", href: WORKSTATION_ROUTE_CATALOG.settingsAccess, matchPath: WORKSTATION_ROUTE_CATALOG.settingsAccess, actionLabel: "Open access settings" },
      { id: "administration:accounting", title: "Connect accounting systems", description: "Configure the governed downstream accounting connection.", href: WORKSTATION_ROUTE_CATALOG.settingsAccountingSystems, matchPath: WORKSTATION_ROUTE_CATALOG.settingsAccountingSystems, actionLabel: "Open accounting systems" },
      { id: "administration:diagnostics", title: "Learn recovery diagnostics", description: "Find actionable health and recovery evidence.", href: WORKSTATION_ROUTE_CATALOG.settingsDiagnostics, matchPath: WORKSTATION_ROUTE_CATALOG.settingsDiagnostics, actionLabel: "Open diagnostics" }
    ]
  }
] as const;

export const ONBOARDING_TOUR_STEPS: readonly OnboardingStepDefinition[] = ONBOARDING_JOURNEYS[0].steps;

export type OnboardingStepStatus = "complete" | "active" | "upcoming";

export interface OnboardingStepViewModel {
  id: string;
  index: number;
  title: string;
  description: string;
  href: string;
  actionLabel: string;
  status: OnboardingStepStatus;
  /** True when the operator is currently on this step's route. */
  isCurrentRoute: boolean;
}

export interface OnboardingTourViewModel {
  /** Whether the coach-mark / progress affordance should render at all. */
  visible: boolean;
  steps: OnboardingStepViewModel[];
  /** Index of the first incomplete step (0 when all complete). */
  activeIndex: number;
  completedCount: number;
  totalCount: number;
  /** e.g. "2 / 4". */
  progressLabel: string;
  /** 0..1 completion fraction, for the header ring. */
  progressFraction: number;
  allComplete: boolean;
  dismissed: boolean;
  journeys: readonly Pick<OnboardingJourneyDefinition, "id" | "label" | "description">[];
  journeyId: string;
  journeyLabel: string;
  journeyDescription: string;
}

function normalizePath(pathname: string): string {
  if (pathname.length > 1 && pathname.endsWith("/")) {
    return pathname.slice(0, -1);
  }
  return pathname;
}

/** The step whose route matches the current location, or null. */
export function resolveVisitedStepId(pathname: string, journeyId = DEFAULT_ONBOARDING_JOURNEY_ID): string | null {
  const path = normalizePath(pathname);
  return resolveOnboardingJourney(journeyId).steps.find((step) => step.matchPath === path)?.id ?? null;
}

export function buildOnboardingTourViewModel({
  pathname,
  state,
  steps
}: {
  pathname: string;
  state: OnboardingState;
  steps?: readonly OnboardingStepDefinition[];
}): OnboardingTourViewModel {
  const journey = resolveOnboardingJourney(state.journeyId);
  const selectedSteps = steps ?? journey.steps;
  const path = normalizePath(pathname);
  const completedSet = new Set(state.completedStepIds);
  const activeIndex = Math.max(
    0,
    selectedSteps.findIndex((step) => !completedSet.has(step.id))
  );
  const completedCount = selectedSteps.reduce((count, step) => (completedSet.has(step.id) ? count + 1 : count), 0);
  const allComplete = completedCount === selectedSteps.length;

  const stepViewModels: OnboardingStepViewModel[] = selectedSteps.map((step, index) => {
    const complete = completedSet.has(step.id);
    const status: OnboardingStepStatus = complete ? "complete" : index === activeIndex ? "active" : "upcoming";
    return {
      id: step.id,
      index,
      title: step.title,
      description: step.description,
      href: step.href,
      actionLabel: step.actionLabel,
      status,
      isCurrentRoute: step.matchPath === path
    };
  });

  return {
    visible: !state.dismissed && !allComplete,
    steps: stepViewModels,
    activeIndex,
    completedCount,
    totalCount: selectedSteps.length,
    progressLabel: `${completedCount} / ${selectedSteps.length}`,
    progressFraction: selectedSteps.length === 0 ? 1 : completedCount / selectedSteps.length,
    allComplete,
    dismissed: state.dismissed,
    journeys: ONBOARDING_JOURNEYS.map(({ id, label, description }) => ({ id, label, description })),
    journeyId: journey.id,
    journeyLabel: journey.label,
    journeyDescription: journey.description
  };
}

export function resolveOnboardingJourney(journeyId: string): OnboardingJourneyDefinition {
  return ONBOARDING_JOURNEYS.find((journey) => journey.id === journeyId) ?? ONBOARDING_JOURNEYS[0];
}
