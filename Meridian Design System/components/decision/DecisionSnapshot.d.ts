import type { ObjectRef } from "../core/ObjectModel";
import type { ActionGuardValue } from "../operations/ActionGuard";
import type { ConsequenceSet } from "./ConsequenceRail";

/**
 * DecisionSnapshot — exactly what was reviewed, frozen at the moment of the decision.
 *
 * "A. Patel clicked Approve at 11:42" is not evidence of anything. A snapshot binds the decision to
 * the object AND its version, the workspace context, the time roles, the evidence set, the
 * calculation versions, the materiality assessment, the guards that passed, and the consequences
 * that were shown. `capture` deep-freezes; there is no setter. `verify` answers the only question
 * that matters later: is this proof still about the thing in front of me?
 *
 * `ActionGuard` answers *may I*. `ActionEnvelope` answers *did it land*. This answers *what did I
 * review* — and it is the fact an `AttestationPanel` binds to.
 *
 * @example
 * const snap = DecisionSnapshot.capture({
 *   object: { type: "journal", id: "JE-20418", version: 3 },
 *   context: { entity: "General Account", book: "STAT", period: "2026-07" },
 *   time: { asOf: "2026-07-31T23:59:00Z", evidenceAt: "2026-08-01T06:14:00Z" },
 *   evidence: [{ id: "DOC-8841", version: "2", hash: "9f2c", condition: "confirmed" }],
 *   calculations: [{ id: "CALC-7", version: "3", inputHash: "aa71" }],
 *   guards: guards,
 *   consequences: { reversibility: "compensatable" },
 *   decision: { type: "approved", actorId: "apatel", actorName: "A. Patel", authority: "Controller" },
 * });
 *
 * <DecisionSnapshotPanel snapshot={snap} current={{ object: live, evidence: liveEvidence }} />
 */

export interface EvidenceStamp {
  id: string;
  version?: string | number;
  /** Content hash where the source system provides one — the snapshot stays small by design. */
  hash?: string;
  condition?: string;
}

export interface CalculationStamp {
  id: string;
  version: string | number;
  inputHash?: string;
}

export interface DecisionRecord {
  /** "approved" · "rejected" · "posted" · "attested" — the domain's own verb. */
  type?: string;
  actorId?: string;
  actorName?: string;
  authority?: string;
  rationale?: string;
}

export interface DecisionSnapshotValue {
  id: string;
  object: ObjectRef | null;
  context: Record<string, React.ReactNode> | null;
  time: Record<string, string> | null;
  evidence: EvidenceStamp[];
  calculations: CalculationStamp[];
  materiality: { label?: string; classification?: string; note?: string } | null;
  guards: ActionGuardValue[];
  consequences: ConsequenceSet | null;
  decision: DecisionRecord | null;
  /** UTC instant, `YYYY-MM-DD HH:MM:SSZ`. */
  capturedAt: string;
}

export type DecisionSnapshotInput = Partial<Omit<DecisionSnapshotValue, "id">> & { id?: string };

/** What changed since capture, and whether that voids the proof or only warrants a re-read. */
export interface SnapshotFinding {
  field: string;
  was: unknown;
  now: unknown;
  effect: "void" | "review";
  note: string;
}

export interface DecisionSnapshotPanelProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  snapshot: DecisionSnapshotValue | null;
  /** Live values to check the snapshot against. Omit to render the proof without verification. */
  current?: { object?: ObjectRef; evidence?: EvidenceStamp[]; calculations?: CalculationStamp[]; context?: Record<string, unknown> };
  /** Pre-computed findings, if the screen already ran `verify`. */
  findings?: SnapshotFinding[];
  title?: React.ReactNode;
  /** The one-sentence proof above the grid. @default true */
  showSentence?: boolean;
}
/** Read-only proof block: sealed · re-read · superseded, then the bound facts. */
export declare function DecisionSnapshotPanel(props: DecisionSnapshotPanelProps): JSX.Element;

export interface UseDecisionSnapshot {
  snapshot: DecisionSnapshotValue | null;
  capture: (override?: DecisionSnapshotInput) => DecisionSnapshotValue;
  findings: SnapshotFinding[];
  isVoid: boolean;
  describe: () => string;
  release: () => void;
}

/** Bundle carrier — the whole contract, reachable from a template with no import. */
export declare const DecisionSnapshot: {
  SECTIONS: string[];
  /** Deep-freezes. The returned value has no setter and is safe to persist verbatim. */
  capture: (input?: DecisionSnapshotInput) => DecisionSnapshotValue;
  verify: (snapshot: DecisionSnapshotValue | null, current?: DecisionSnapshotPanelProps["current"]) => SnapshotFinding[];
  /** The auditable sentence — actor, object, version, context, evidence, calculation, guards, time. */
  describe: (snapshot: DecisionSnapshotValue | null) => string;
  Panel: typeof DecisionSnapshotPanel;
  use: (input: DecisionSnapshotInput, current?: DecisionSnapshotPanelProps["current"]) => UseDecisionSnapshot;
};

/** Hook carrier. */
export declare const SnapshotHooks: {
  useDecisionSnapshot: (input: DecisionSnapshotInput, current?: DecisionSnapshotPanelProps["current"]) => UseDecisionSnapshot;
  captureDecisionSnapshot: (input?: DecisionSnapshotInput) => DecisionSnapshotValue;
  verifyDecisionSnapshot: (snapshot: DecisionSnapshotValue | null, current?: DecisionSnapshotPanelProps["current"]) => SnapshotFinding[];
  describeDecisionSnapshot: (snapshot: DecisionSnapshotValue | null) => string;
};
