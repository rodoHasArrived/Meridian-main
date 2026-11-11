/**
 * AdapterDiagnostics — posture, retry schedule, last error, queue depth and signing-key state for
 * the **outbound** integrations that carry an operator's instructions away from the workstation
 * (4.1, track F). `ConnectionState` covers inbound reads: its failure makes a figure old, while an
 * adapter's failure makes an instruction *unlanded*. Closes `CONTRACT_AUDIT.md` §A-5 / §D-2.
 *
 * Five postures — `linked · degraded · retrying · suspended · unconfigured`. A `retrying` row with
 * no `retry.nextAt` is rendered as the contradiction it is: retrying with no schedule is a
 * suspension. Law: `guidelines/ADAPTER_SEMANTICS.md`.
 *
 * @example
 * <AdapterDiagnostics asOf="14:32:08Z" scope="LIVE" budgetNote="ack budget 250ms p99"
 *   adapters={[{
 *     id:"ibkr-live", name:"IBKR order adapter", venue:"IBKR", environment:"LIVE",
 *     posture:"retrying", lastRoundTrip:"6m ago",
 *     latency:{p50:88,p99:412,budget:250},
 *     outbound:{inFlight:2,queued:14,oldest:"6m",onResume:"queue replays oldest first"},
 *     retry:{attempt:4,of:8,backoff:"30s",nextAt:"14:33:12Z",onExhaustion:"suspend + page desk"},
 *     lastError:{code:"WS-1006",message:"websocket closed abnormally (no close frame)",at:"14:32:42Z",count:4},
 *     key:{id:"oms-sig-2026-07",algorithm:"Ed25519",age:"41d",rotatesAt:"2026-08-20",overdue:false},
 *     version:18,
 *   }]}
 *   onRetryNow={guarded(retry)} onSuspend={guarded(suspend)} onResume={guarded(resume)} />
 */
export type AdapterPosture = "linked" | "degraded" | "retrying" | "suspended" | "unconfigured";

export interface AdapterRow {
  id: string;
  name?: string;
  /** The counterparty. Never merge venue health into the adapter's own posture (rule 6). */
  venue?: string;
  venueStatus?: string;
  /** `PAPER` and `LIVE` twins are separate rows, never one row with a toggle (rule 8). */
  environment?: string;
  route?: string;
  posture?: AdapterPosture;
  postureLabel?: string;
  /** One sentence of why this posture, in the adapter's terms. Falls back to the posture's own. */
  statement?: string;
  lastRoundTrip?: string;
  cadence?: string;
  latency?: { p50?: number; p99?: number; budget?: number };
  /** Depth alone is not a severity — always pass `oldest` with a non-empty queue (rule 4). */
  outbound?: { inFlight?: number; queued?: number; oldest?: string; onResume?: string };
  /** A schedule, not a spinner (rule 3). `nextAt` is mandatory while `posture === "retrying"`. */
  retry?: { attempt?: number; of?: number; backoff?: string; nextAt?: string; lastAttemptAt?: string; onExhaustion?: string };
  /** Quoted verbatim with its code and clock — never normalized (rule 2). */
  lastError?: { code?: string; message: string; at?: string; count?: number; correlationId?: string };
  /** Rotation is a clock, not a checkbox (rule 5). */
  key?: { id: string; algorithm?: string; age?: string; rotatesAt?: string; overdue?: boolean };
  suspendedBy?: string;
  suspendedAt?: string;
  /** `OmsOutboundContract.Version` — optimistic concurrency. */
  version?: number;
  /** Server version has moved; mutating controls disable with the reason stated (rule 9). */
  versionStale?: boolean;
}

export interface AdapterDiagnosticsProps extends React.HTMLAttributes<HTMLElement> {
  adapters?: AdapterRow[];
  /** @default "Outbound adapters" */
  title?: React.ReactNode;
  /** Environment or desk this panel covers — diagnostics are per adapter, per environment. */
  scope?: string;
  asOf?: string;
  /** States the budget the p99 column is judged against. */
  budgetNote?: string;
  /** Controlled expansion. Omit for internal state. */
  selectedId?: string | null;
  defaultSelectedId?: string | null;
  onSelect?: (id: string | null) => void;
  /** Consequential — route through `ActionGuard` and declare reversibility (rule 7). */
  onRetryNow?: (row: AdapterRow) => void;
  onSuspend?: (row: AdapterRow) => void;
  onResume?: (row: AdapterRow) => void;
  onRotateKey?: (row: AdapterRow) => void;
  /** @default false */
  dense?: boolean;
  /** Worst posture first: retrying → degraded → suspended → linked → unconfigured. @default true */
  sortByPosture?: boolean;
  emptyStatement?: React.ReactNode;
  footNote?: React.ReactNode;
}
export declare function AdapterDiagnostics(props: AdapterDiagnosticsProps): JSX.Element;

/** Cell-sized posture, for a blotter row, a status bar, or an adapter picker. */
export declare function AdapterPostureChip(props: React.HTMLAttributes<HTMLSpanElement> & {
  posture?: AdapterPosture;
  label?: React.ReactNode;
  /** Rendered inline — a retrying chip with no next attempt is misleading. */
  nextAttempt?: string;
}): JSX.Element;

/** The five postures with severity mapping and the sentence each one means. */
export declare const ADAPTER_POSTURES: Array<{ id: AdapterPosture; label: string; severity: "ready" | "action" | "blocked" | "review" | "info"; says: string }>;
export declare function adapterPosture(id?: string): { id: AdapterPosture; label: string; severity: string; says: string };
/** Rule 1 — true when a row claims `retrying` without a next attempt. */
export declare function adapterScheduleMissing(row?: AdapterRow): boolean;
/** Bundle carrier. The compiler exposes only capitalized exports. */
export declare const AdapterHooks: { adapterPosture: typeof adapterPosture; adapterScheduleMissing: typeof adapterScheduleMissing };
