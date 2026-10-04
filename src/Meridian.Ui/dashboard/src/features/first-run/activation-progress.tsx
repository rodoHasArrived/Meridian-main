import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowRight, Check, Circle, FlaskConical, ListChecks } from "lucide-react";
import { apiPostJson } from "@/lib/api";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import {
  primeActivationProgress,
  subscribeToActivationProgress
} from "@/lib/first-run/activation";
import {
  Sheet,
  SheetBody,
  SheetCloseButton,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle
} from "@/components/ui/sheet";
import { formatRelativeAge } from "@/lib/time";
import { cn } from "@/lib/utils";
import { buildGettingStartedViewModel, type GettingStartedStep } from "./getting-started.view-model";
import type { FirstRunStatus } from "./types";

/**
 * Masthead chip summarizing first-run activation progress, plus the checklist behind it.
 *
 * The count alone was a dead end: it named a denominator without ever saying which steps
 * were left or where to do them. The host already publishes a label, an action label and a
 * route per outcome, so the chip opens a drawer that walks the user to the next one.
 *
 * The status is fetched once by AppRoot and passed down — this component must not issue its
 * own fetch, so the shell renders from a single consistent first-run snapshot. It does track
 * outcomes reported later in the session, so finishing an import updates the chip in place.
 */
export function ActivationHeaderProgress({ status }: { status?: FirstRunStatus | null }) {
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [liveStatus, setLiveStatus] = useState<FirstRunStatus | null>(status ?? null);

  useEffect(() => {
    setLiveStatus(status ?? null);
    primeActivationProgress(status ?? null);
  }, [status]);

  useEffect(() => subscribeToActivationProgress(setLiveStatus), []);

  const model = buildGettingStartedViewModel(liveStatus);
  if (!model.visible || !liveStatus) {
    return null;
  }

  const openStep = (route: string) => {
    setOpen(false);
    navigate(route);
  };

  return (
    <div className="flex items-center gap-2">
      {liveStatus.workspace.isSample ? (
        <span
          className="inline-flex items-center gap-1.5 rounded-full bg-amber-400/15 px-2.5 py-1 text-xs font-semibold text-amber-200"
          title={liveStatus.workspace.safetyMessage}
        >
          <FlaskConical size={13} />
          {liveStatus.workspace.badge}
        </span>
      ) : null}

      <button
        type="button"
        aria-label={model.triggerAriaLabel}
        title={model.triggerTitle}
        onClick={() => setOpen(true)}
        className={cn(
          "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs transition-colors",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          model.finished
            ? "bg-emerald-400/15 text-emerald-200 hover:bg-emerald-400/25"
            : "bg-slate-800 text-slate-300 hover:bg-slate-700 hover:text-slate-100"
        )}
      >
        {model.finished ? <Check size={13} aria-hidden="true" /> : <ListChecks size={13} aria-hidden="true" />}
        {model.triggerLabel}
      </button>

      <button
        className="rounded-full border border-slate-700 px-2.5 py-1 text-xs text-slate-300 hover:border-cyan-400 hover:text-cyan-200"
        onClick={() => void apiPostJson(WORKSTATION_API_ENDPOINTS.desktopLaunch, { page: "Portfolio" })}
      >
        Open desktop workstation
      </button>

      <Sheet open={open} onOpenChange={setOpen}>
        <SheetContent aria-labelledby="getting-started-title" aria-describedby="getting-started-summary" side="right">
          <SheetHeader>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <SheetTitle id="getting-started-title">{model.headline}</SheetTitle>
                <SheetDescription id="getting-started-summary">{model.summary}</SheetDescription>
              </div>
              <SheetCloseButton onClick={() => setOpen(false)} />
            </div>
          </SheetHeader>
          <SheetBody>
            <ol className="space-y-2">
              {model.steps.map((step) => (
                <li key={step.key}>
                  <GettingStartedRow step={step} onOpen={openStep} />
                </li>
              ))}
            </ol>
          </SheetBody>
        </SheetContent>
      </Sheet>
    </div>
  );
}

function GettingStartedRow({
  step,
  onOpen
}: {
  step: GettingStartedStep;
  onOpen: (route: string) => void;
}) {
  return (
    <div
      className={cn(
        "rounded-lg border p-3",
        step.isNext ? "border-primary/50 bg-primary/5" : "border-border/60 bg-transparent"
      )}
    >
      <div className="flex items-start gap-2.5">
        <span
          className={cn("mt-0.5 flex-shrink-0", step.isComplete ? "text-success" : "text-muted-foreground")}
          aria-hidden="true"
        >
          {step.isComplete ? <Check className="h-4 w-4" /> : <Circle className="h-4 w-4" />}
        </span>
        <div className="min-w-0 flex-1">
          <p className={cn("text-sm", step.isComplete ? "text-muted-foreground line-through" : "text-foreground")}>
            {step.label}
          </p>
          <p className="mt-0.5 text-xs text-muted-foreground">
            {step.isComplete ? `Done ${formatRelativeAge(step.completedAtUtc).toLowerCase()}` : step.isNext ? "Next step" : "Not done yet"}
          </p>
        </div>
        <button
          type="button"
          onClick={() => onOpen(step.route)}
          className="inline-flex flex-shrink-0 items-center gap-1 rounded-md border border-border/60 px-2 py-1 text-xs text-foreground transition-colors hover:bg-secondary/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        >
          {step.isComplete ? "Revisit" : step.actionLabel}
          <ArrowRight className="h-3 w-3" aria-hidden="true" />
        </button>
      </div>
    </div>
  );
}
