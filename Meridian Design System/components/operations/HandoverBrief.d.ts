import type React from "react";
/**
 * HandoverBrief — what moved on the operator's objects since the last-seen mark (4.1, track A of
 * session continuity). `EventTimeline` is per object; this is per *person*, and answers the question
 * no export in 3.5.0 could: "I was out four hours — what changed?"
 *
 * Four kinds in fixed reading order — **breached → assigned → changed → resolved** — with severity
 * ordering rows inside a kind. What broke outranks what arrived; what arrived outranks what moved.
 * Every row carries its own clock and actor: a change with no time and no author cannot be handed
 * over. Acknowledgement moves the mark and nothing else — it is not an approval, not an attestation,
 * and never a bulk action (`guidelines/SESSION_CONTINUITY.md`).
 *
 * @example
 * <HandoverBrief
 *   since="09:12" elapsed="4h 18m" actor="a.patel" owned={14}
 *   items={[
 *     {id:"1", kind:"breached", status:"Blocked", at:"11:04", actor:"system",
 *      object:{type:"Reconciliation", id:"REC-2026-07-CASH", label:"July cash — General Account"},
 *      statement:"Custodian file arrived 92 minutes late; the 14:00 SLA is breached.",
 *      action:{label:"Open case", onClick:openCase}},
 *     {id:"2", kind:"assigned", status:"ReviewRequired", at:"10:47", actor:"m.chen",
 *      object:{type:"Journal", id:"JE-88213", label:"Amortization — July STAT"},
 *      statement:"Reassigned to you when the controller escalated the reopened period."}]}
 *   onOpen={open} onAcknowledge={moveMark} />
 */
export type HandoverKind = "breached" | "assigned" | "changed" | "resolved";
export interface HandoverItem {
  id?: string;
  kind: HandoverKind | string;
  /** Any Meridian readiness string — normalized to the five canonical severities. */
  status?: string;
  statusLabel?: React.ReactNode;
  object?: { type?: string; typeLabel?: string; id?: string | number; label?: React.ReactNode };
  label?: React.ReactNode;
  /** One sentence naming what happened and why it matters. Not a log line. */
  statement?: React.ReactNode;
  /** This row's own clock. Required in practice — a change with no time cannot be handed over. */
  at?: string;
  /** Who caused it. `"system"` is a legitimate actor. */
  actor?: string;
  action?: { label: React.ReactNode; onClick?: (item: HandoverItem) => void };
}
export interface HandoverBriefProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  /** The last-seen mark. The window is explicit — "recent" is not a window. */
  since?: string;
  /** @default "now" */
  until?: string;
  /** Human duration of the window — "4h 18m". */
  elapsed?: string;
  actor?: string;
  items?: HandoverItem[];
  /** How many objects the operator owns or watches — the denominator for the empty statement. */
  owned?: number;
  /** Hide anything below this severity: `"blocked" | "action" | "review" | "info" | "ready"`. */
  severityFloor?: "blocked" | "action" | "review" | "info" | "ready";
  onOpen?: (item: HandoverItem) => void;
  /** Moves the mark. Never approves, attests, or acts on the listed objects. */
  onAcknowledge?: () => void;
  /** Set once the mark has moved; replaces the acknowledge button with a statement. */
  acknowledgedAt?: string;
  /** Tighter rows for a rail. Guard rows keep review density regardless. @default false */
  dense?: boolean;
  /** Override kind labels or order. Defaults to the four canonical kinds. */
  kinds?: { id: string; label: React.ReactNode; fallback?: string }[];
  title?: React.ReactNode;
  /** Override the empty render. Empty is always a statement, never a blank panel. */
  emptyStatement?: React.ReactNode;
  footNote?: React.ReactNode;
}
export declare function HandoverBrief(props: HandoverBriefProps): React.JSX.Element;

/** Topbar chip — count plus the mark, for opening the brief. Pairs with `NotificationCenter`. */
export declare function HandoverBriefChip(props: React.HTMLAttributes<HTMLButtonElement> & {
  count?: number;
  severity?: "blocked" | "action" | "review" | "info" | "ready";
  since?: string;
  label?: React.ReactNode;
}): React.JSX.Element;
