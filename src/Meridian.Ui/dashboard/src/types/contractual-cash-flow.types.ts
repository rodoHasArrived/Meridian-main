import type { BondDecimal } from "./bond-analytics.types";

export type ContractualCashFlowSubjectKind = "Security" | "Loan";
export const RATE_SCENARIOS = ["Base", "Up100", "Up200", "Up300", "Down100", "Down200", "Down300", "Stress"] as const;
export type RateScenario = (typeof RATE_SCENARIOS)[number];
export const PREPAYMENT_METHODS = ["Cpr", "Smm", "Psa", "Amount"] as const;
export type PrepaymentMethod = (typeof PREPAYMENT_METHODS)[number];
export type RecastPolicy = "PreservePayments" | "Recast";
export type ExerciseSide = "Call" | "Put";
export type ExerciseStyle = "European" | "American" | "Bermudan";

export interface SelectedCashFlowExercise {
  side: ExerciseSide;
  entryStartDate: string;
  streamId: string;
  exerciseDate: string;
  noticeDate: string;
  amount: string | number;
  hypotheticalNotice?: boolean;
  noticeEvidenceId?: string | null;
}

/** Intervals are [startDate, endDate); Amount assumptions pay on startDate. */
export interface CashFlowPrepaymentAssumption {
  streamId: string;
  startDate: string;
  endDate: string;
  method: PrepaymentMethod;
  value: string | number;
  seasoningOrigin?: string | null;
  policy?: RecastPolicy;
}

export interface CashFlowCurvePin {
  benchmarkKey: string;
  curveId: string;
  curveVersionId: string;
}

/** Hypothetical scenario inputs; nothing here changes authoritative terms or posts anything. */
export interface ContractualCashFlowRequest {
  valuationDate: string;
  knowledgeCutoff: string;
  horizon?: string | null;
  rateScenario?: RateScenario;
  exercises?: SelectedCashFlowExercise[] | null;
  prepayments?: CashFlowPrepaymentAssumption[] | null;
  curvePins?: CashFlowCurvePin[] | null;
  engineVersion?: string;
}

export interface CashFlowCalculationBlocker {
  code: string;
  message: string;
  streamId?: string | null;
}

export interface CashFlowExerciseEligibility {
  side: ExerciseSide;
  entryStartDate: string;
  streamId: string;
  windowEnd: string;
  style: ExerciseStyle;
  dates: string[];
  businessDayCalendarId: string | null;
}

export interface CashFlowExerciseCalculation {
  selection: SelectedCashFlowExercise;
  noticeWindow: { earliest: string | null; latest: string | null };
  dirtyPresentValue: BondDecimal;
  accruedInterest: BondDecimal;
  cleanFloor: BondDecimal;
  dirtyConsideration: BondDecimal;
  premium: BondDecimal;
}

export interface ContractualCashFlowEvent {
  eventId: string;
  streamId: string;
  periodId: string | null;
  date: string;
  accrualStart: string | null;
  accrualEnd: string | null;
  openingBalance: BondDecimal;
  cashInterest: BondDecimal;
  capitalizedInterest: BondDecimal;
  scheduledPrincipal: BondDecimal;
  forecastPrepayment: BondDecimal;
  redeemedPrincipal: BondDecimal;
  redemptionPremium: BondDecimal;
  prepaymentPenalty: BondDecimal;
  closingBalance: BondDecimal;
  direction: "Receive" | "Pay";
  netCash?: BondDecimal;
}

export interface ContractualCashFlowEvidence {
  engineVersion: string;
  mappingVersion: string;
  prepaymentVersion: string;
  kernelVersion: string;
  inputHash: string;
  outputHash: string;
  sourceTermsHash: string;
  sourceVersion: string;
  hasContractOverride: boolean;
  calendars: unknown[];
  curves: unknown[];
  warnings: string[];
}

export interface ContractualCashFlowResult {
  events: ContractualCashFlowEvent[];
  exerciseEligibility: CashFlowExerciseEligibility[];
  exerciseCalculations: CashFlowExerciseCalculation[];
  blockers: CashFlowCalculationBlocker[];
  evidence: ContractualCashFlowEvidence;
  isCalculated: boolean;
  isPostable: boolean;
}

/** Complete immutable replay packet retained by the server. */
export interface ContractualCashFlowRun {
  runId: string;
  subjectKind: ContractualCashFlowSubjectKind;
  subjectId: string;
  recordedAt: string;
  request: ContractualCashFlowRequest;
  result: ContractualCashFlowResult;
  actor: string;
}

export interface CreateContractualCashFlowRunRequest {
  runId: string;
  projection: ContractualCashFlowRequest;
}

export interface CashFlowDateDelta {
  date: string;
  streamId: string;
  leftNetCash: BondDecimal;
  rightNetCash: BondDecimal;
  delta: BondDecimal;
}

export interface CashFlowBlockerDelta {
  side: "LeftOnly" | "RightOnly";
  code: string;
  message: string;
  streamId: string | null;
}

/** Pure read over two retained packets of the same subject; nothing is recalculated against current terms. */
export interface ContractualCashFlowRunComparison {
  subjectKind: ContractualCashFlowSubjectKind;
  subjectId: string;
  leftRunId: string;
  rightRunId: string;
  leftReplayVerified: boolean;
  rightReplayVerified: boolean;
  sameRequest: boolean;
  leftOutputHash: string;
  rightOutputHash: string;
  cashDeltas: CashFlowDateDelta[];
  blockerDeltas: CashFlowBlockerDelta[];
  totalDelta: BondDecimal;
}
