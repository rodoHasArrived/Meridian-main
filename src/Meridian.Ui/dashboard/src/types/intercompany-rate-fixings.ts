/**
 * Reviewed index fixings for floating-rate intercompany accrual (rule intercompany-accrual-v2).
 * Tenant and actor are server owned. Rates are exact decimal text on the wire and are never parsed to float.
 */
export type IntercompanyRateFixingAction = "Propose" | "Approve" | "Reject";

/** One proposed or approved value. Its digest binds rate, source and justification together. */
export interface IntercompanyRateFixingValue {
  /** Annual decimal fraction as exact text: "0.0425" means 4.25%. */
  rateFraction: string;
  sourceReference: string; justification: string; proposedBy: string; proposedAt: string; valueDigest: string;
}
export interface IntercompanyRateFixingEvent {
  eventId: string; requestId: string; action: IntercompanyRateFixingAction; actor: string; recordedAt: string;
  valueDigest: string; reason?: string | null;
}
/** Only `approved` sets accrual rates; a correction stays `pending` until a second person approves it. */
export interface IntercompanyRateFixing {
  fixingId: string; tenantId: string; indexReference: string; fixingDate: string; version: number;
  approved: IntercompanyRateFixingValue | null; pending: IntercompanyRateFixingValue | null;
  events: IntercompanyRateFixingEvent[];
}
/** `expectedVersion` is 0 for a date with no record yet, else the record's current version. */
export interface ProposeIntercompanyRateFixingRequest {
  requestId: string; indexReference: string; fixingDate: string; rateFraction: string;
  sourceReference: string; justification: string; expectedVersion: number;
}
/** Decides exactly the pending value the reviewer saw. A reason is required to reject. */
export interface DecideIntercompanyRateFixingRequest {
  requestId: string; action: "Approve" | "Reject"; expectedVersion: number; expectedValueDigest: string; reason: string | null;
}
