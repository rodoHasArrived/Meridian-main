import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export type OperationalTrustTone = "ready" | "review" | "blocked" | "unknown";

export interface OperationalTrustFact {
  value: ReactNode;
  detail?: ReactNode;
  tone?: OperationalTrustTone;
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
}

const toneClasses: Record<OperationalTrustTone, string> = {
  ready: "border-success/35 bg-success/10 text-success",
  review: "border-warning/40 bg-warning/10 text-warning",
  blocked: "border-danger/40 bg-danger/10 text-danger",
  unknown: "border-border bg-secondary/35 text-muted-foreground"
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
 * only status signal. Route-owned recovery actions can be supplied in `action`.
 */
export function OperationalTrustSummary({
  source,
  scope,
  freshness,
  completeness,
  blocker,
  action,
  label = "Data confidence",
  className
}: OperationalTrustSummaryProps) {
  const facts: Array<{ id: string; label: string; fact: OperationalTrustFact }> = [
    { id: "source", label: "Source", fact: source },
    { id: "scope", label: "Scope", fact: scope },
    { id: "freshness", label: "Freshness", fact: freshness },
    { id: "completeness", label: "Completeness", fact: completeness }
  ];

  if (blocker) {
    facts.push({ id: "blocker", label: "Blocker", fact: blocker });
  }

  return (
    <section
      className={cn("rounded-[2px] border border-border bg-card px-3.5 py-3", className)}
      aria-label={label}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <dl className="grid min-w-0 flex-1 grid-cols-[repeat(auto-fit,minmax(min(100%,12rem),1fr))] gap-2">
          {facts.map(({ id, label: factLabel, fact }) => {
            const tone = fact.tone ?? "unknown";
            return (
              <div key={id} className="min-w-0 rounded-[2px] border border-border/70 bg-secondary/20 px-3 py-2">
                <dt className="text-xs font-medium text-muted-foreground">{factLabel}</dt>
                <dd className="mt-1 min-w-0">
                  <div className="flex min-w-0 items-start gap-2">
                    <span
                      className={cn("inline-flex shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium", toneClasses[tone])}
                    >
                      {toneLabels[tone]}
                    </span>
                    <span className="min-w-0 break-words text-sm font-semibold leading-5 text-foreground">{fact.value}</span>
                  </div>
                  {fact.detail ? (
                    <p className="mt-1 break-words text-xs leading-5 text-muted-foreground">{fact.detail}</p>
                  ) : null}
                </dd>
              </div>
            );
          })}
        </dl>
        {action ? <div className="flex shrink-0 flex-wrap items-center gap-2">{action}</div> : null}
      </div>
    </section>
  );
}
