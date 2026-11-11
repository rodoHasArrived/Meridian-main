/**
 * Assurance — certification as carried state on a BALANCE (account × period × basis), not only on
 * a close. Stages climb unprepared → prepared → reviewed → certified, one at a time; each signature
 * is bound to the balance it vouched for, so a later movement voids it rather than letting it stand
 * over a different number. The lock gate reads `assuranceCoverage` of material accounts.
 *
 * @example
 * const rec = signAssurance(undefined, { actor: "R. Chen", authority: "Preparer", balance: 96881.5 });
 * assuranceState(rec, 96881.5).stage            // "prepared"
 * assuranceState(rec, 97000).voided             // true — the balance moved
 * <AssuranceChip record={rec} balance={96881.5} showActor />
 * <CertificationMatrix accounts={leaves} records={byCode} balances={byCode} onOpenAccount={open} />
 */
export type AssuranceStageId = "unprepared" | "prepared" | "reviewed" | "certified";
export interface AssuranceStage { id: AssuranceStageId; label: string; rank: number; note: string }
export declare const ASSURANCE_STAGES: ReadonlyArray<AssuranceStage>;

export interface AssuranceSignature {
  stage: Exclude<AssuranceStageId, "unprepared">;
  actor: string;
  authority?: string;
  /** ISO instant. */
  at: string;
  /** The balance signed against; a different current balance voids the signature. */
  balance: number;
  snapshotId?: string;
}
export interface AssuranceRecord { signatures: AssuranceSignature[] }
export interface AssuranceStateResult { stage: AssuranceStageId; voided: boolean; movedFrom?: number; signatures: AssuranceSignature[] }
export declare function assuranceState(record: AssuranceRecord | undefined, currentBalance?: number): AssuranceStateResult;

export interface AssuranceAccount { code: string; name: string; material?: boolean; type?: string }
export interface AssuranceCoverage {
  rows: Array<AssuranceAccount & { stage: AssuranceStageId; voided: boolean; covered: boolean }>;
  inScope: AssuranceCoverage["rows"]; uncovered: AssuranceCoverage["rows"];
  covered: number; total: number; complete: boolean;
}
export declare function assuranceCoverage(accounts: AssuranceAccount[], records: Record<string, AssuranceRecord | undefined>, currentBalances?: Record<string, number>, opts?: { requiredStage?: AssuranceStageId; materialOnly?: boolean }): AssuranceCoverage;

/** Advance one stage. Throws when asked to skip a stage. Pure — returns the new record. */
export declare function signAssurance(record: AssuranceRecord | undefined, sig: { stage?: Exclude<AssuranceStageId, "unprepared">; actor: string; authority?: string; at?: string; balance: number; snapshotId?: string }): AssuranceRecord;
export declare function nextAssuranceStage(stage: AssuranceStageId): AssuranceStageId | null;
/** "Prepare" | "Review" | "Certify" — the verb for the next step, or null when certified. */
export declare function assuranceVerb(stage: AssuranceStageId): string | null;

export interface AssuranceChipProps extends React.HTMLAttributes<HTMLSpanElement> {
  record?: AssuranceRecord; balance?: number;
  /** Bypass derivation and show this stage. */
  stage?: AssuranceStageId;
  showActor?: boolean;
}
export declare function AssuranceChip(props: AssuranceChipProps): JSX.Element;

export interface CertificationMatrixProps extends React.HTMLAttributes<HTMLDivElement> {
  accounts: AssuranceAccount[];
  records: Record<string, AssuranceRecord | undefined>;
  balances: Record<string, number>;
  currency?: string;
  /** @default "certified" */
  requiredStage?: AssuranceStageId;
  onOpenAccount?: (code: string) => void;
  caption?: string;
}
/** Accounts × stages — the close's real progress bar. */
export declare function CertificationMatrix(props: CertificationMatrixProps): JSX.Element;

export declare const Assurance: {
  STAGES: typeof ASSURANCE_STAGES; state: typeof assuranceState; coverage: typeof assuranceCoverage; sign: typeof signAssurance;
  next: typeof nextAssuranceStage; verb: typeof assuranceVerb; Chip: typeof AssuranceChip; Matrix: typeof CertificationMatrix;
};
