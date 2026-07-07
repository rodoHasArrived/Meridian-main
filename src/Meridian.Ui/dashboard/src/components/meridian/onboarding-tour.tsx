import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { ArrowRight, CheckCircle2, ChevronDown, Compass, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Stepper } from "@/components/ui/stepper";
import {
  buildOnboardingTourViewModel,
  resolveVisitedStepId,
  type OnboardingTourViewModel
} from "@/app-shell.onboarding";
import {
  readOnboardingState,
  withCompletedStep,
  writeOnboardingState,
  type OnboardingState
} from "@/lib/onboarding";

export interface OnboardingTourController {
  viewModel: OnboardingTourViewModel;
  /** Coach-mark card expanded (vs. collapsed to just the header ring). */
  expanded: boolean;
  setExpanded: (expanded: boolean) => void;
  /** Permanently dismiss the tour. */
  dismiss: () => void;
}

/**
 * Owns onboarding state: persisted progress, auto-completion when the operator
 * visits a step's route, and the expand/dismiss UI state. Called once in the app
 * shell; the result drives both the header ring and the coach-mark card.
 */
export function useOnboardingTour(): OnboardingTourController {
  const { pathname } = useLocation();
  const [state, setState] = useState<OnboardingState>(readOnboardingState);
  const [expanded, setExpanded] = useState(true);

  const persist = useCallback((next: OnboardingState) => {
    setState(next);
    writeOnboardingState(next);
  }, []);

  // Visiting a step's route completes it — real actions, not a checklist.
  useEffect(() => {
    const visitedStepId = resolveVisitedStepId(pathname);
    if (!visitedStepId) {
      return;
    }
    setState((current) => {
      const next = withCompletedStep(current, visitedStepId);
      if (next === current) {
        return current;
      }
      writeOnboardingState(next);
      return next;
    });
  }, [pathname]);

  const dismiss = useCallback(() => {
    persist({ ...state, dismissed: true });
  }, [persist, state]);

  const viewModel = useMemo(
    () => buildOnboardingTourViewModel({ pathname, state }),
    [pathname, state]
  );

  return { viewModel, expanded, setExpanded, dismiss };
}

/** Header progress ring — visible until the operator graduates or dismisses. */
export function OnboardingHeaderProgress({ controller }: { controller: OnboardingTourController }) {
  const { viewModel, expanded, setExpanded } = controller;
  if (!viewModel.visible) {
    return null;
  }
  const label = `Getting started — ${viewModel.progressLabel} steps done`;
  return (
    <button
      type="button"
      aria-label={label}
      aria-expanded={expanded}
      title={label}
      onClick={() => setExpanded(!expanded)}
      className="relative inline-flex h-9 w-9 items-center justify-center rounded-md border border-border/60 bg-transparent text-muted-foreground transition-colors hover:bg-secondary/60 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
    >
      <ProgressRing fraction={viewModel.progressFraction} />
    </button>
  );
}

function ProgressRing({ fraction }: { fraction: number }) {
  const radius = 8;
  const circumference = 2 * Math.PI * radius;
  const dash = Math.max(0, Math.min(1, fraction)) * circumference;
  return (
    <span className="relative inline-flex h-5 w-5 items-center justify-center">
      <svg viewBox="0 0 20 20" className="h-5 w-5 -rotate-90" aria-hidden="true">
        <circle cx="10" cy="10" r={radius} fill="none" stroke="currentColor" strokeWidth="2" className="text-border" />
        <circle
          cx="10"
          cy="10"
          r={radius}
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeDasharray={`${dash} ${circumference}`}
          className="text-primary transition-[stroke-dasharray] duration-500"
        />
      </svg>
      <Compass className="absolute h-2.5 w-2.5" aria-hidden="true" />
    </span>
  );
}

/** Dismissible coach-mark card docked bottom-right, driven by the same controller. */
export function OnboardingCoachMark({ controller }: { controller: OnboardingTourController }) {
  const { viewModel, expanded, setExpanded, dismiss } = controller;
  if (!viewModel.visible || !expanded) {
    return null;
  }

  const activeStep = viewModel.steps[viewModel.activeIndex] ?? viewModel.steps[0];

  return (
    <aside
      role="region"
      aria-label="Getting started tour"
      className="fixed bottom-5 right-5 z-40 w-[min(22rem,calc(100vw-2.5rem))] overflow-hidden rounded-md border border-border bg-card shadow-[0_2px_10px_rgba(0,0,0,0.18)]"
    >
      <div className="flex items-start justify-between gap-2 border-b border-border px-4 py-3">
        <div className="min-w-0">
          <div className="flex items-center gap-1.5 text-sm font-semibold text-foreground">
            <Compass className="h-4 w-4 text-primary" aria-hidden="true" />
            First 10 minutes
          </div>
          <p className="mt-0.5 text-xs text-muted-foreground">{viewModel.progressLabel} steps complete</p>
        </div>
        <div className="flex flex-shrink-0 items-center gap-1">
          <button
            type="button"
            aria-label="Collapse getting-started tour"
            onClick={() => setExpanded(false)}
            className="inline-flex h-7 w-7 items-center justify-center rounded-[2px] text-muted-foreground transition-colors hover:bg-secondary/60 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          >
            <ChevronDown className="h-4 w-4" aria-hidden="true" />
          </button>
          <button
            type="button"
            aria-label="Skip getting-started tour"
            onClick={dismiss}
            className="inline-flex h-7 w-7 items-center justify-center rounded-[2px] text-muted-foreground transition-colors hover:bg-secondary/60 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      </div>

      <Stepper
        className="border-b-0"
        showStepNumber={false}
        activeStep={viewModel.activeIndex}
        steps={viewModel.steps.map((step) => ({
          label: step.title,
          badge: step.status === "complete" ? "✓" : undefined
        }))}
      />

      {activeStep ? (
        <div className="space-y-3 px-4 py-3">
          <div>
            <div className="flex items-center gap-1.5 text-sm font-semibold text-foreground">
              {activeStep.status === "complete" ? (
                <CheckCircle2 className="h-4 w-4 text-success" aria-hidden="true" />
              ) : null}
              {activeStep.title}
            </div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{activeStep.description}</p>
          </div>
          <div className="flex items-center justify-between gap-2">
            <Button asChild size="sm" variant="default" onClick={() => setExpanded(true)}>
              <Link to={activeStep.href} aria-label={activeStep.actionLabel}>
                <span>{activeStep.actionLabel}</span>
                <ArrowRight className="ml-1 h-3.5 w-3.5" aria-hidden="true" />
              </Link>
            </Button>
            <Button size="sm" variant="ghost" type="button" onClick={dismiss}>
              Skip tour
            </Button>
          </div>
        </div>
      ) : null}
    </aside>
  );
}
