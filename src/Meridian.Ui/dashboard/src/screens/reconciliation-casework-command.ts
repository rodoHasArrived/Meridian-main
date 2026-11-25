import { isApiError } from "@/lib/api-errors";
import type { ReconciliationBreakQueueItem, ReconciliationCaseworkCommand, ReconciliationCaseworkOperationResult } from "@/types";

/**
 * Outcome of one versioned reconciliation casework command.
 *
 * The casework endpoints answer HTTP 200 even when the server refuses a command, so the
 * classification reads `transitionStatus`, `errorCode` and `outcome`, never the HTTP status alone.
 * - `succeeded`: the server retained the change; `item` is the new version.
 * - `conflict`: the case changed after it was loaded; reload before submitting again.
 * - `refused`: the server checked the command and declined it (eligibility, approval, taxonomy).
 * - `failed`: the request did not complete; retry with the same command identity.
 */
export type ReconciliationCaseworkCommandOutcome =
  | { kind: "succeeded"; item: ReconciliationBreakQueueItem }
  | { kind: "conflict"; message: string }
  | { kind: "refused"; message: string; errorCode: string | null }
  | { kind: "failed"; message: string };

const CONFLICT_MESSAGE = "This break changed since you opened it. Reload it, check the current state, then submit again.";

const REFUSAL_MESSAGES: Record<string, string> = {
  MissingActor: "The server could not identify you. Sign in again, then retry.",
  MissingReason: "Give a reason. It is retained in the case history.",
  MissingEvidence: "Add at least one evidence link that this decision relies on.",
  IllegalTransition: "This break is not in a state that allows that action. Reload it to see its current state.",
  DualReviewRequired: "A second reviewer has to approve this action.",
  ReopenNotAllowed: "Only a signed-off break can be reopened, and only by an administrator.",
  MissingRootCause: "Set a root cause before closing this break.",
  MissingResolutionCode: "Set a resolution before closing this break.",
  ResolverSignerConflict: "You resolved this break, so someone else has to sign it off. An administrator can sign off with a reason.",
  InvalidTaxonomy: "That code is not in the current reconciliation taxonomy, or it needs evidence of a different kind.",
  MaterialActionRequiresHumanOperator: "A person has to take this action directly. Automated and statement-handoff actions are not accepted.",
  MissingApproval: "Name the approver and the approval reference.",
  SelfApprovalNotAllowed: "The approver has to be someone other than you.",
  MissingSuccessor: "Name the break that replaces this one.",
  CommandIdConflict: "This command identity was already used for a different change. Start the action again.",
  IdempotencyConflict: "This command identity was already used for a different change. Start the action again.",
  AccountingPeriodHardClosed: "The accounting period for this break is hard closed, so the break cannot change.",
  InvalidRequest: "The request was incomplete. Check the fields, then submit again."
};

const STATUS_MESSAGES: Record<string, string> = {
  NotFound: "This break no longer exists, or it is outside your scope.",
  InvalidTransition: REFUSAL_MESSAGES.IllegalTransition,
  Unauthorized: "Your role does not allow this action on this break."
};

/** Plain operator wording for a refused casework result. Server detail is kept after the plain sentence. */
export function describeCaseworkRefusal(result: ReconciliationCaseworkOperationResult): string {
  const code = result.errorCode && result.errorCode !== "None" ? result.errorCode : null;
  const plain = (code ? REFUSAL_MESSAGES[code] : undefined) ?? STATUS_MESSAGES[result.transitionStatus];
  const detail = result.error ?? result.outcome?.issues?.[0]?.message ?? null;
  const missing = result.validationIssues?.filter(Boolean) ?? [];
  const parts = [plain ?? detail ?? `The server did not accept this change (${result.transitionStatus}).`];
  if (plain && detail && detail !== plain) parts.push(`Server detail: ${detail}`);
  if (missing.length > 0) parts.push(`Missing: ${missing.join(", ")}.`);
  return parts.join(" ");
}

export function isCaseworkConflict(result: ReconciliationCaseworkOperationResult): boolean {
  return result.transitionStatus === "Conflict" || result.errorCode === "ConcurrencyConflict";
}

export function isStatementHandoffMarker(reference: string): boolean {
  return reference.startsWith("urn:meridian:statement-casework-handoff:");
}

/** Classify a casework result without throwing. */
export function classifyCaseworkResult(
  result: ReconciliationCaseworkOperationResult,
  expectedBreakId: string
): ReconciliationCaseworkCommandOutcome {
  if (isCaseworkConflict(result)) return { kind: "conflict", message: CONFLICT_MESSAGE };
  const succeeded = result.outcome?.state === "Succeeded" || result.outcome?.state === "CompletedWithWarnings";
  if (succeeded && result.item) {
    if (result.item.breakId !== expectedBreakId) {
      return { kind: "failed", message: "The casework result belongs to another break. Reload this break before continuing." };
    }
    return { kind: "succeeded", item: result.item };
  }
  return { kind: "refused", message: describeCaseworkRefusal(result), errorCode: result.errorCode ?? null };
}

/** Send one command and classify the result. HTTP 409 counts as a conflict; other transport errors as failed. */
export async function runReconciliationCaseworkCommand(
  command: ReconciliationCaseworkCommand,
  send: (command: ReconciliationCaseworkCommand) => Promise<ReconciliationCaseworkOperationResult>
): Promise<ReconciliationCaseworkCommandOutcome> {
  try {
    return classifyCaseworkResult(await send(command), command.breakId);
  } catch (failure) {
    if (isApiError(failure) && failure.status === 409) return { kind: "conflict", message: CONFLICT_MESSAGE };
    if (isApiError(failure) && [400, 401, 403, 404].includes(failure.status)) {
      return { kind: "refused", message: failure.message, errorCode: `Http${failure.status}` };
    }
    const message = failure instanceof Error ? failure.message : "Casework request failed.";
    return { kind: "failed", message: `${message} Retry uses the same command identity, so the change cannot be applied twice.` };
  }
}

/** A new versioned command. The endpoint replaces `actor` with the authenticated principal. */
export function createVersionedCaseworkCommand(
  item: Pick<ReconciliationBreakQueueItem, "breakId" | "version">,
  action: ReconciliationCaseworkCommand["action"],
  source: string,
  fields: Partial<ReconciliationCaseworkCommand> = {}
): ReconciliationCaseworkCommand | null {
  if (item.version == null) return null;
  return {
    breakId: item.breakId,
    action,
    actor: "",
    commandId: crypto.randomUUID(),
    correlationId: crypto.randomUUID(),
    source,
    expectedVersion: item.version,
    ...fields
  };
}
