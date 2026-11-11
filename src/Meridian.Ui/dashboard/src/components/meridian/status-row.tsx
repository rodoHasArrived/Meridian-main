import type { ReactNode } from "react";
import { CheckCircle2, ChevronDown } from "lucide-react";
import {
  statusToneLabel,
  summarizeStatusSignals,
  type StatusSignalTone
} from "@/lib/status-signals";
import { cn } from "@/lib/utils";

export interface StatusRowFact {
  id: string;
  label: string;
  value: ReactNode;
  detail?: ReactNode;
  tone: StatusSignalTone;
  action?: ReactNode;
}

export interface StatusRowProps {
  /** Accessible name of the region. */
  label: string;
  facts: StatusRowFact[];
  /** What the row says when every fact is ready. */
  clearText?: string;
  /** A card-level control that stays beside the row, outside "Details". */
  action?: ReactNode;
  className?: string;
}

const toneClasses: Record<StatusSignalTone, string> = {
  ready: "border-[var(--severity-ready-bd)] bg-[var(--severity-ready-bg)] text-[var(--severity-ready-fg)]",
  review: "border-[var(--severity-action-bd)] bg-[var(--severity-action-bg)] text-[var(--severity-action-fg)]",
  blocked: "border-[var(--severity-blocked-bd)] bg-[var(--severity-blocked-bg)] text-[var(--severity-blocked-fg)]",
  unknown: "border-[var(--severity-info-bd)] bg-[var(--severity-info-bg)] text-[var(--severity-info-fg)]",
  pending: "border-[var(--severity-info-bd)] bg-[var(--severity-info-bg)] text-[var(--severity-info-fg)]"
};

const dotClasses: Record<StatusSignalTone, string> = {
  ready: "bg-[var(--severity-ready-fg)]",
  review: "bg-[var(--severity-action-fg)]",
  blocked: "bg-[var(--severity-blocked-fg)]",
  unknown: "bg-[var(--severity-info-fg)]",
  pending: "bg-[var(--severity-info-fg)]"
};

/**
 * The one-line status row every workspace page carries.
 *
 * It says "All sources current" or names only the facts that are not ready
 * ("Scope: none selected · Freshness: 187d stale"). A row of pills that mostly read "Ready"
 * teaches the eye to skip all of them, so ready facts are not listed; the full per-fact breakdown,
 * with each fact's supporting detail and recovery action, is behind "Details". The worst tone and
 * the list of issues come from `summarizeStatusSignals`, the rule the masthead and Today also use.
 */
export function StatusRow({
  label,
  facts,
  clearText = "All sources current",
  action,
  className
}: StatusRowProps) {
  const summary = summarizeStatusSignals(facts);

  return (
    <section
      className={cn("operational-trust-summary flex items-start gap-2 rounded-[2px] border border-border bg-card", className)}
      aria-label={label}
      data-status-tone={summary.tone}
    >
      <details className="group min-w-0 flex-1">
        <summary className="flex cursor-pointer list-none items-center gap-3 px-3 py-1.5 focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40 [&::-webkit-details-marker]:hidden">
          <span className="sr-only">{label}. </span>
          {summary.clear ? (
            <span
              className="flex min-w-0 flex-1 items-center gap-1.5 text-xs font-medium leading-5 text-[var(--severity-ready-fg)]"
              data-testid="status-row-clear"
            >
              <CheckCircle2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              {clearText}
            </span>
          ) : (
            <span className="flex min-w-0 flex-1 flex-wrap items-baseline gap-x-4 gap-y-0.5" data-testid="status-row-issues">
              <span className={cn("inline-flex shrink-0 rounded-full border px-1.5 text-xs font-medium leading-5", toneClasses[summary.tone])}>
                {statusToneLabel(summary.tone)}
              </span>
              {/* Values are financially material, so a long one wraps onto another line; it
                  never truncates. */}
              {summary.issues.map((fact) => (
                <span key={fact.id} className="inline-flex min-w-0 max-w-full items-baseline gap-x-1.5 text-xs leading-5">
                  <span className={cn("h-1.5 w-1.5 shrink-0 self-center rounded-full", dotClasses[fact.tone])} aria-hidden="true" />
                  <span className="shrink-0 text-muted-foreground">{fact.label}:</span>
                  <span className="min-w-0 break-words font-medium text-foreground">{fact.value}</span>
                  <span className="sr-only">({statusToneLabel(fact.tone)})</span>
                </span>
              ))}
            </span>
          )}
          <span className="flex shrink-0 items-center gap-1 text-xs font-medium text-muted-foreground">
            Details
            <ChevronDown className="h-4 w-4 transition-transform group-open:rotate-180" aria-hidden="true" />
          </span>
        </summary>
        <div className="border-t border-border px-3 py-3">
          <dl className="grid min-w-0 grid-cols-[repeat(auto-fit,minmax(min(100%,12rem),1fr))] gap-2">
            {facts.map((fact) => (
              <div key={fact.id} className="min-w-0 rounded-[2px] border border-border/70 bg-secondary/20 px-3 py-2">
                <dt className="text-xs font-medium text-muted-foreground">{fact.label}</dt>
                <dd className="mt-1 min-w-0">
                  {/*
                    The status pill never shrinks, so on a 12rem tile it leaves the value roughly
                    100px -- narrower than a single long word like "unavailable". `break-words`
                    then splits that word mid-character rather than moving it down.

                    `basis-28` gives the value a 7rem hypothetical width, so when less than that
                    is free beside the pill the whole value wraps onto its own full-width line and
                    breaks at spaces again. Wider tiles still keep pill and value side by side.
                    `break-words` stays as the last resort for a genuinely unbreakable token.
                  */}
                  <div className="flex min-w-0 flex-wrap items-start gap-x-2 gap-y-1">
                    <span
                      className={cn("inline-flex shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium", toneClasses[fact.tone])}
                    >
                      {statusToneLabel(fact.tone)}
                    </span>
                    <span className="min-w-0 grow basis-28 break-words text-sm font-semibold leading-5 text-foreground">{fact.value}</span>
                  </div>
                  {fact.detail ? (
                    <p className="mt-1 break-words text-xs leading-5 text-muted-foreground">{fact.detail}</p>
                  ) : null}
                  {fact.action ? (
                    <div className="mt-2 flex flex-wrap items-center gap-2">{fact.action}</div>
                  ) : null}
                </dd>
              </div>
            ))}
          </dl>
        </div>
      </details>
      {action ? <div className="flex shrink-0 flex-wrap items-center gap-2 py-1 pr-2">{action}</div> : null}
    </section>
  );
}
