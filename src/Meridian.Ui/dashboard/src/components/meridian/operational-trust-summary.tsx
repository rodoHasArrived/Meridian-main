import type { ReactNode } from "react";
import { ChevronDown } from "lucide-react";
import { cn } from "@/lib/utils";

export type OperationalTrustTone = "ready" | "review" | "blocked" | "unknown";

export interface OperationalTrustFact {
  label?: string;
  value: ReactNode;
  detail?: ReactNode;
  tone?: OperationalTrustTone;
  action?: ReactNode;
}

export interface OperationalTrustSummaryProps {
  source: OperationalTrustFact;
  scope: OperationalTrustFact;
  freshness: OperationalTrustFact;
  completeness: OperationalTrustFact;
  blocker?: OperationalTrustFact;
  action?: ReactNode;
  label?: string;
  className?: string;
  disclosure?: boolean;
}

const toneClasses: Record<OperationalTrustTone, string> = {
  ready: "border-[var(--severity-ready-bd)] bg-[var(--severity-ready-bg)] text-[var(--severity-ready-fg)]",
  review: "border-[var(--severity-action-bd)] bg-[var(--severity-action-bg)] text-[var(--severity-action-fg)]",
  blocked: "border-[var(--severity-blocked-bd)] bg-[var(--severity-blocked-bg)] text-[var(--severity-blocked-fg)]",
  unknown: "border-[var(--severity-info-bd)] bg-[var(--severity-info-bg)] text-[var(--severity-info-fg)]"
};

const toneLabels: Record<OperationalTrustTone, string> = {
  ready: "Ready",
  review: "Needs review",
  blocked: "Blocked",
  unknown: "Unknown"
};

/**
 * Compact, reusable source-of-truth summary for financially material screens.
 *
 * It keeps source, operating scope, freshness, completeness, and any blocker in
 * one predictable region. Every tone also has visible text so color is never the
 * only status signal. Each fact can own its recovery action so an operator does
 * not have to infer which global control remediates a specific warning.
 */
export function OperationalTrustSummary({
  source,
  scope,
  freshness,
  completeness,
  blocker,
  action,
  label = "Data confidence",
  className,
  disclosure = false
}: OperationalTrustSummaryProps) {
  const facts: Array<{ id: string; label: string; fact: OperationalTrustFact }> = [
    { id: "source", label: source.label ?? "Source", fact: source },
    { id: "scope", label: scope.label ?? "Scope", fact: scope },
    { id: "freshness", label: freshness.label ?? "Freshness", fact: freshness },
    { id: "completeness", label: completeness.label ?? "Completeness", fact: completeness }
  ];

  if (blocker) {
    facts.push({ id: "blocker", label: blocker.label ?? "Blocker", fact: blocker });
  }

  const detail = (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <dl className="grid min-w-0 flex-1 grid-cols-[repeat(auto-fit,minmax(min(100%,12rem),1fr))] gap-2">
        {facts.map(({ id, label: factLabel, fact }) => {
          const tone = fact.tone ?? "unknown";
          return (
            <div key={id} className="min-w-0 rounded-[2px] border border-border/70 bg-secondary/20 px-3 py-2">
              <dt className="text-xs font-medium text-muted-foreground">{factLabel}</dt>
              <dd className="mt-1 min-w-0">
                {/*
                  The status pill never shrinks, so on a 12rem tile it leaves the value roughly
                  100px -- narrower than a single long word like "unavailable". `break-words` then
                  splits that word mid-character rather than moving it down, which is how the
                  control tower came to read "Timestamp unavailabl / e". The squeeze only happens
                  once a blocker adds a fifth tile, so it appeared exactly when an operator most
                  needed to read the status.

                  `basis-28` gives the value a 7rem hypothetical width, so when less than that is
                  free beside the pill the whole value wraps onto its own full-width line and
                  breaks at spaces again. Wider tiles still keep pill and value side by side.
                  `break-words` stays as the last resort for a genuinely unbreakable token.
                */}
                <div className="flex min-w-0 flex-wrap items-start gap-x-2 gap-y-1">
                  <span
                    className={cn("inline-flex shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium", toneClasses[tone])}
                  >
                    {toneLabels[tone]}
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
          );
        })}
      </dl>
      {action ? <div className="flex shrink-0 flex-wrap items-center gap-2">{action}</div> : null}
    </div>
  );

  return (
    <section
      className={cn("rounded-[2px] border border-border bg-card", !disclosure && "px-3.5 py-3", className)}
      aria-label={label}
    >
      {disclosure ? (
        <details className="group">
          <summary className="flex cursor-pointer list-none items-start gap-3 px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 [&::-webkit-details-marker]:hidden">
            <span className="sr-only">{label}. </span>
            <span className="grid min-w-0 flex-1 grid-cols-[repeat(auto-fit,minmax(min(100%,10rem),1fr))] gap-x-3 gap-y-2">
              {facts.map(({ id, label: factLabel, fact }) => (
                <span key={id} className="min-w-0 text-xs leading-5">
                  <span className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
                    <span className="text-muted-foreground">{factLabel}</span>
                    <span className={cn("inline-flex rounded-full border px-1.5 text-[11px]", toneClasses[fact.tone ?? "unknown"])}>
                      {toneLabels[fact.tone ?? "unknown"]}
                    </span>
                  </span>
                  <span className="block break-words font-medium text-foreground">{fact.value}</span>
                </span>
              ))}
            </span>
            <span className="flex shrink-0 items-center gap-1 text-xs font-medium text-muted-foreground">
              Details
              <ChevronDown className="h-4 w-4 transition-transform group-open:rotate-180" aria-hidden="true" />
            </span>
          </summary>
          <div className="border-t border-border px-3 py-3">{detail}</div>
        </details>
      ) : detail}
    </section>
  );
}
