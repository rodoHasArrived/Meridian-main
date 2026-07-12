import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import type { OnboardingState } from "@/lib/onboarding";

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

export const ONBOARDING_TOUR_STEPS: readonly OnboardingStepDefinition[] = [
  {
    id: "quote",
    title: "Watch a live quote",
    description: "Open the quotes lane and watch a symbol stream in real time.",
    href: workstationRouteWithQuery("dataQuotes", { symbol: "AAPL" }),
    matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
    actionLabel: "Open quotes"
  },
  {
    id: "backtest",
    title: "Run a backtest",
    description: "Take a strategy through the lab and see how it would have performed.",
    href: WORKSTATION_ROUTE_CATALOG.strategyLab,
    matchPath: WORKSTATION_ROUTE_CATALOG.strategyLab,
    actionLabel: "Open strategy lab"
  },
  {
    id: "ledger",
    title: "Trace P&L to the ledger",
    description: "Follow a result into the accounting ledger — every number ties back.",
    href: WORKSTATION_ROUTE_CATALOG.accountingLedger,
    matchPath: WORKSTATION_ROUTE_CATALOG.accountingLedger,
    actionLabel: "Open ledger"
  },
  {
    id: "connect",
    title: "Connect your first provider",
    description: "Wire up a data provider to replace the sample feed with your own.",
    href: WORKSTATION_ROUTE_CATALOG.dataProviders,
    matchPath: WORKSTATION_ROUTE_CATALOG.dataProviders,
    actionLabel: "Open providers"
  }
] as const;

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
}

function normalizePath(pathname: string): string {
  if (pathname.length > 1 && pathname.endsWith("/")) {
    return pathname.slice(0, -1);
  }
  return pathname;
}

/** The step whose route matches the current location, or null. */
export function resolveVisitedStepId(pathname: string): string | null {
  const path = normalizePath(pathname);
  return ONBOARDING_TOUR_STEPS.find((step) => step.matchPath === path)?.id ?? null;
}

export function buildOnboardingTourViewModel({
  pathname,
  state,
  steps = ONBOARDING_TOUR_STEPS
}: {
  pathname: string;
  state: OnboardingState;
  steps?: readonly OnboardingStepDefinition[];
}): OnboardingTourViewModel {
  const path = normalizePath(pathname);
  const completedSet = new Set(state.completedStepIds);
  const activeIndex = Math.max(
    0,
    steps.findIndex((step) => !completedSet.has(step.id))
  );
  const completedCount = steps.reduce((count, step) => (completedSet.has(step.id) ? count + 1 : count), 0);
  const allComplete = completedCount === steps.length;

  const stepViewModels: OnboardingStepViewModel[] = steps.map((step, index) => {
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
    totalCount: steps.length,
    progressLabel: `${completedCount} / ${steps.length}`,
    progressFraction: steps.length === 0 ? 1 : completedCount / steps.length,
    allComplete,
    dismissed: state.dismissed
  };
}
