import * as React from "react";
/**
 * ActionEnvelope — the write-side twin of `ValueEnvelope`. `ActionGuard` answers *may I*;
 * this answers *did it*. A mutating control declares its endpoint, idempotency key, permission,
 * posture (optimistic or confirmed) and failure surface BEFORE the attempt, and then renders the
 * only four fates an instruction can have: in flight · landed · **unlanded** · rejected.
 *
 * "Failed" is deliberately not one of them. Silence from an adapter means the fate is unknown, so
 * the retry carries the same idempotency key rather than sending a second instruction.
 *
 * @example
 * <ActionEnvelopeProvider actions={{ "order.submit": {
 *     label: "Submit order", endpoint: "POST /v1/orders",
 *     idempotencyKey: draft.token, permission: { required: "trade.submit", held: true, actor: "k.ito" },
 *     posture: "confirmed", failureSurface: "Blotter row · status bar", payload: draft } }}
 *   transport={desk.transport} timeoutMs={6000}>
 *   <ActionEnvelope action="order.submit" submitLabel="Send order" />
 *   <InstructionLedger />
 * </ActionEnvelopeProvider>
 */
export type ActionState = "idle" | "inflight" | "landed" | "unlanded" | "rejected";
export type ActionPosture = "confirmed" | "optimistic";

export interface ActionStateMeta {
  id: ActionState;
  label: string;
  glyph: string;
  severity: "info" | "review" | "ready" | "action" | "blocked";
  /** The operator-facing sentence for this fate — rendered, not paraphrased. */
  meaning: string;
}
export declare const ACTION_STATES: ReadonlyArray<ActionStateMeta>;

export interface ActionPermission {
  /** The permission the action consumes, e.g. "trade.submit". */
  required: string;
  /** False renders the action unavailable and says so. @default true */
  held?: boolean;
  actor?: string;
}

export interface ActionReceipt {
  /** The system-of-record id, when one came back. */
  id?: string;
  at?: string;
  latencyMs?: number;
  attempt?: number;
  /** Set when a duplicate submit was deduped against an earlier instruction. */
  duplicateOf?: string;
}

export interface ActionDeclaration {
  label?: string;
  endpoint?: string;
  /** Declared up front — the instruction's identity, not a send-time detail. */
  idempotencyKey?: string;
  permission?: ActionPermission;
  posture?: ActionPosture;
  /** Where a failure will be visible to the operator. Undeclared renders as a defect. */
  failureSurface?: string;
  payload?: unknown;
}

export interface Instruction {
  id: string;
  action: string;
  label: string;
  idempotencyKey: string;
  state: ActionState;
  reason: string | null;
  receipt: ActionReceipt;
}

export interface ActionEnvelopeProps extends ActionDeclaration, React.HTMLAttributes<HTMLElement> {
  /** Action id — inside a provider the live instruction and its fate are read from it. */
  action?: string;
  /** Controlled state; omit inside a provider. */
  state?: ActionState;
  receipt?: ActionReceipt;
  reason?: string;
  submitLabel?: string;
  onSubmit?: () => void;
  onRetry?: () => void;
  onAcknowledge?: () => void;
  disabled?: boolean;
  /** Hide the declaration block when a compact surface already states it. @default true */
  showDeclaration?: boolean;
}
export declare function ActionEnvelope(props: ActionEnvelopeProps): React.JSX.Element;

export interface ActionStateChipProps extends React.HTMLAttributes<HTMLSpanElement> {
  state?: ActionState;
  attempt?: number;
  idempotencyKey?: string;
}
/** The fate as an inline mark — blotter cell, queue row, status bar. */
export declare function ActionStateChip(props: ActionStateChipProps): React.JSX.Element;

export interface InstructionLedgerProps extends React.HTMLAttributes<HTMLElement> {
  /** Omit inside a provider to render the session's instructions. */
  instructions?: Instruction[];
  label?: string;
  emptyLabel?: string;
}
export declare function InstructionLedger(props: InstructionLedgerProps): React.JSX.Element;

export interface TransportInstruction {
  action: string;
  endpoint?: string;
  idempotencyKey: string;
  attempt: number;
  posture: ActionPosture;
  permission?: ActionPermission;
  payload?: unknown;
}
export interface TransportAnswer {
  /** False = rejected WITH a reason. A thrown error or a timeout is unlanded, not rejected. */
  ok?: boolean;
  id?: string;
  reason?: string;
  duplicateOf?: string;
}

export interface ActionEnvelopeProviderProps {
  actions?: Record<string, ActionDeclaration>;
  transport?: (instruction: TransportInstruction) => Promise<TransportAnswer> | TransportAnswer;
  /** No answer within this window is unlanded. @default 8000 */
  timeoutMs?: number;
  onSettled?: (settled: { action: string; id: string; state: ActionState; reason: string | null; receipt: ActionReceipt }) => void;
  children?: React.ReactNode | ((api: ActionApi) => React.ReactNode);
}
export declare function ActionEnvelopeProvider(props: ActionEnvelopeProviderProps): React.JSX.Element;

export interface ActionApi {
  instructions: Instruction[];
  instructionFor(action: string): Instruction | null;
  /** Submitting a key that is already in flight or landed dedupes — a double click is one instruction. */
  submit(action: string, overrides?: Partial<ActionDeclaration>): Promise<{ id: string; state: ActionState; deduped?: boolean }>;
  /** Same key, next attempt — asks whether the first one landed. */
  retry(action: string): Promise<{ id: string; state: ActionState }>;
  unresolved: Instruction[];
}

export declare const ActionHooks: {
  useAction(): ActionApi | null;
  ActionContext: React.Context<ActionApi | null>;
  ACTION_STATES: typeof ACTION_STATES;
  stateMeta(id: ActionState): ActionStateMeta;
};
