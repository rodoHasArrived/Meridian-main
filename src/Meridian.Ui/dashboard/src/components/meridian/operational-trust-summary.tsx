import type { ReactNode } from "react";
import { buildAttentionSignalFact } from "@/components/meridian/attention-signal-fact";
import { StatusRow, type StatusRowFact } from "@/components/meridian/status-row";
import { useShellStatusSignals } from "@/lib/shell-status-signals-context";
import { isStatusIssue } from "@/lib/status-signals";

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
}

/**
 * The page status row for financially material screens.
 *
 * A screen states its source, operating scope, freshness, completeness and any blocker; the row
 * shows one line that either reads "All sources current" or names only the facts that are not
 * ready, with the full breakdown behind "Details" (see {@link StatusRow}). Shell-wide source
 * issues -- provider posture, unverified provenance, workspace data that did not load -- are
 * folded in from the shell attention state, so this row and the masthead cannot disagree about
 * the same sources. Each fact can own its recovery action so an operator does not have to infer
 * which global control remediates a specific warning.
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
  const shell = useShellStatusSignals();
  const facts: StatusRowFact[] = [
    toFact("source", "Source", source),
    toFact("scope", "Scope", scope),
    toFact("freshness", "Freshness", freshness),
    toFact("completeness", "Completeness", completeness),
    ...(blocker ? [toFact("blocker", "Blocker", blocker)] : []),
    ...shell.sourceSignals
      .filter((signal) => isStatusIssue(signal.tone))
      .map((signal) => buildAttentionSignalFact(signal, shell.operatingScope))
  ];

  return <StatusRow label={label} facts={facts} action={action} className={className} />;
}

function toFact(id: string, defaultLabel: string, fact: OperationalTrustFact): StatusRowFact {
  return {
    id,
    label: fact.label ?? defaultLabel,
    value: fact.value,
    detail: fact.detail,
    // A fact with no stated tone has not been established, which is itself worth naming.
    tone: fact.tone ?? "unknown",
    action: fact.action
  };
}
