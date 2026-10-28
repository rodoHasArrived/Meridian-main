import { formatCount, formatCurrencyWithCode, formatDateTimeLabel } from "./accounting-screen.formatting";
import type { AssetAccountingWorkflowViewModel } from "./accounting-screen.asset-workflow";
import type {
  AccountingToolingTone,
  ManualJournalLifecycleChecklistItemViewModel,
  ManualJournalLifecycleCommandViewModel,
  ManualJournalLifecycleCorrectionViewModel,
  ManualJournalLifecycleTransitionViewModel,
} from "./accounting-screen.view-model";
import type { JournalEntryLifecycleAction, JournalEntryLifecycleTransition, ManualJournalEntryDraft } from "@/types";

export const ASSET_REVIEW_ONLY_REASON = "Asset accounting drafts require retained approval evidence through the governed asset workflow; generic approval, posting and corrections are unavailable.";

export function lifecycleActionNotes(action: JournalEntryLifecycleAction, draft: ManualJournalEntryDraft): string {
  switch (action) {
    case "Approve":
      return `Controller approval for journal entry ${draft.journalEntryId}.`;
    case "Reject":
      return `Controller rejection for journal entry ${draft.journalEntryId}.`;
    case "Post":
      return `Post approved journal entry ${draft.journalEntryId}.`;
    case "Reverse":
      return `Create reversal draft for posted journal entry ${draft.journalEntryId}.`;
    case "Rebook":
      return `Create rebook draft for posted journal entry ${draft.journalEntryId}.`;
    case "LockAfterClose":
      return `Lock posted journal entry ${draft.journalEntryId} after close.`;
    case "Submit":
      return `Submit journal entry ${draft.journalEntryId} for approval.`;
    case "Validate":
      return `Validate journal entry ${draft.journalEntryId}.`;
    default:
      return `Apply ${action} to journal entry ${draft.journalEntryId}.`;
  }
}

export function buildManualJournalLifecycleCommands(
  draft: ManualJournalEntryDraft,
  busyAction: JournalEntryLifecycleAction | null,
  assetWorkflow?: AssetAccountingWorkflowViewModel
): ManualJournalLifecycleCommandViewModel[] {
  const hasEvidence = (draft.evidenceLinks?.length ?? 0) > 0 || (draft.evidenceAttachments?.length ?? 0) > 0;
  const isBalanced = Math.abs(draft.imbalance) === 0;
  const hasCriticalIssues = draft.validationIssues.some((issue) => issue.severity === "Critical");
  const commonBlocker = !hasEvidence
    ? "Attach retained evidence before lifecycle transitions."
    : !isBalanced
      ? "Balance debits and credits before lifecycle transitions."
      : hasCriticalIssues
        ? "Resolve critical validation issues before lifecycle transitions."
        : null;

  return [
    lifecycleCommand("Approve", "Approve", "Move a submitted journal entry to approved.", draft.status === "Submitted" ? commonBlocker : `Requires Submitted status; current status is ${draft.status}.`, "success", busyAction),
    lifecycleCommand("Reject", "Reject", "Reject a submitted journal entry for correction.", draft.status === "Submitted" ? null : `Requires Submitted status; current status is ${draft.status}.`, "danger", busyAction),
    lifecycleCommand("Post", "Post", "Post an approved journal entry without mutating it afterward.", draft.status === "Approved" ? commonBlocker : `Requires Approved status; current status is ${draft.status}.`, "success", busyAction),
    lifecycleCommand("Reverse", "Reverse", "Generate a separate reversal draft for a posted journal entry.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "warning", busyAction),
    lifecycleCommand("Rebook", "Rebook", "Generate a separate rebook draft using the current posted lines.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "warning", busyAction),
    lifecycleCommand("LockAfterClose", "Lock after close", "Lock a posted journal entry after close.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "default", busyAction)
  ].map((command) => {
    if (!draft.assetAccountingDraft) return command;
    if ((command.action === "Approve" || command.action === "Post") && assetWorkflow) return {
      ...command, disabledReason: assetWorkflow.blockedReason(command.action),
      label: assetWorkflow.pendingAction === command.action ? `Resume ${command.action.toLowerCase()}` : command.label
    };
    if (command.action === "Reject" && !assetWorkflow?.pendingAction && !assetWorkflow?.hasPendingPost) return command;
    return { ...command, disabledReason: ASSET_REVIEW_ONLY_REASON };
  });
}

export function buildManualJournalLifecycleChecklist(
  draft: ManualJournalEntryDraft,
  commands: ManualJournalLifecycleCommandViewModel[],
  transitions: ManualJournalLifecycleTransitionViewModel[],
  correctionRows: ManualJournalLifecycleCorrectionViewModel[]
): ManualJournalLifecycleChecklistItemViewModel[] {
  const evidenceCount = new Set([
    ...(draft.evidenceLinks ?? []),
    ...(draft.evidenceAttachments ?? []).map((attachment) => attachment.uri)
  ].filter((value) => value.trim().length > 0)).size;
  const criticalIssueCount = draft.validationIssues.filter((issue) => issue.severity === "Critical").length;
  const warningIssueCount = draft.validationIssues.filter((issue) => issue.severity === "Warning").length;
  const isBalanced = Math.abs(draft.imbalance) === 0;
  const commandByAction = new Map(commands.map((command) => [command.action, command]));
  const lifecycleState = new Set([draft.status, ...transitions.map((transition) => transition.title)]);
  const approved = draft.status === "Approved" || draft.status === "Posted" || draft.status === "Reversed" || draft.status === "Rebooked" || draft.status === "CloseLocked" || Boolean(draft.approvedAtUtc);
  const posted = draft.status === "Posted" || draft.status === "Reversed" || draft.status === "Rebooked" || draft.status === "CloseLocked" || Boolean(draft.postedAtUtc);
  const reversed = draft.status === "Reversed" || Boolean(draft.reversalOfJournalEntryId) || correctionRows.some((row) => /reversal/i.test(row.sourceLabel));
  const rebooked = draft.status === "Rebooked" || Boolean(draft.rebookedFromJournalEntryId) || correctionRows.some((row) => /rebook/i.test(row.sourceLabel));
  const locked = draft.status === "CloseLocked" || Boolean(draft.closedLockedAtUtc);

  const commandReadiness = (action: JournalEntryLifecycleAction): { value: string; detail: string; tone: AccountingToolingTone } => {
    const command = commandByAction.get(action);
    if (!command) {
      return { value: "Unavailable", detail: "Lifecycle command is not surfaced for this journal entry.", tone: "default" };
    }

    if (command.disabledReason) {
      return { value: "Blocked", detail: command.disabledReason, tone: "warning" };
    }

    return { value: "Ready", detail: command.description, tone: command.tone === "danger" ? "danger" : command.tone === "success" ? "success" : "warning" };
  };
  const approveReadiness = commandReadiness("Approve");
  const postReadiness = commandReadiness("Post");
  const reverseReadiness = commandReadiness("Reverse");
  const rebookReadiness = commandReadiness("Rebook");
  const lockReadiness = commandReadiness("LockAfterClose");

  return [
    {
      id: "draft",
      label: "Draft",
      value: draft.version > 0 ? `v${draft.version}` : "Unsaved",
      detail: draft.version > 0
        ? `Retained draft ${draft.journalEntryId} is loaded for ${draft.accountingDate}.`
        : "Save the draft before relying on version-guarded lifecycle actions.",
      tone: draft.version > 0 ? "success" : "warning"
    },
    {
      id: "validate",
      label: "Validate",
      value: criticalIssueCount > 0 ? `${criticalIssueCount} critical` : warningIssueCount > 0 ? `${warningIssueCount} warning` : "Clear",
      detail: criticalIssueCount > 0
        ? "Critical validation issues block approval and posting."
        : warningIssueCount > 0
          ? "Warnings are retained for reviewer attention."
          : isBalanced
            ? "No validation issues are attached and debits equal credits."
            : `Entry is out of balance by ${formatCurrencyWithCode(Math.abs(draft.imbalance), draft.currency)}.`,
      tone: criticalIssueCount > 0 ? "danger" : warningIssueCount > 0 || !isBalanced ? "warning" : "success"
    },
    {
      id: "evidence",
      label: "Attach evidence",
      value: evidenceCount > 0 ? formatCount(evidenceCount, "evidence link") : "Missing",
      detail: evidenceCount > 0
        ? "Source support is retained on the header or line evidence set."
        : "Approval, posting, reversal, rebook, and close-lock transitions require retained evidence.",
      tone: evidenceCount > 0 ? "success" : "warning"
    },
    {
      id: "submit",
      label: "Submit",
      value: draft.status === "Submitted" || approved || posted ? "Submitted" : draft.status === "Draft" ? "Draft" : draft.status,
      detail: draft.status === "Submitted" || approved || posted
        ? "Journal entry has crossed the approval submission gate."
        : "Use Submit approval after validation, balance, and evidence are ready.",
      tone: draft.status === "Submitted" || approved || posted ? "success" : "warning"
    },
    {
      id: "approve",
      label: "Approve",
      value: approved ? "Approved" : approveReadiness.value,
      detail: approved
        ? `${draft.approvedBy ?? "Reviewer"} approved the entry${draft.approvedAtUtc ? ` on ${formatDateTimeLabel(draft.approvedAtUtc)}` : ""}.`
        : approveReadiness.detail,
      tone: approved ? "success" : approveReadiness.tone
    },
    {
      id: "post",
      label: "Post",
      value: posted ? "Posted" : postReadiness.value,
      detail: posted
        ? `${draft.postedBy ?? "Operator"} posted the immutable entry${draft.postedAtUtc ? ` on ${formatDateTimeLabel(draft.postedAtUtc)}` : ""}.`
        : postReadiness.detail,
      tone: posted ? "success" : postReadiness.tone
    },
    {
      id: "reverse",
      label: "Reverse",
      value: reversed ? "Linked" : reverseReadiness.value,
      detail: reversed
        ? "A reversal relationship or generated reversal draft is retained; the posted entry was not edited in place."
        : reverseReadiness.detail,
      tone: reversed ? "success" : reverseReadiness.tone
    },
    {
      id: "rebook",
      label: "Rebook",
      value: rebooked ? "Linked" : rebookReadiness.value,
      detail: rebooked
        ? "A rebook relationship or generated rebook draft is retained; the original posted entry remains immutable."
        : rebookReadiness.detail,
      tone: rebooked ? "success" : rebookReadiness.tone
    },
    {
      id: "lock-after-close",
      label: "Lock after close",
      value: locked ? "Locked" : lockReadiness.value,
      detail: locked
        ? `${draft.closeLockedBy ?? "Close controller"} locked the entry after close${draft.closedLockedAtUtc ? ` on ${formatDateTimeLabel(draft.closedLockedAtUtc)}` : ""}.`
        : lockReadiness.detail,
      tone: locked ? "success" : lockReadiness.tone
    },
    {
      id: "audit",
      label: "Audit transitions",
      value: transitions.length > 0 ? formatCount(transitions.length, "transition") : "None",
      detail: transitions.length > 0
        ? "Lifecycle transitions retain audit id, correlation id, actor, notes, and evidence routes."
        : "No lifecycle transition audit rows are retained on this journal entry yet.",
      tone: transitions.length > 0 ? "success" : lifecycleState.has("Draft") ? "warning" : "default"
    }
  ];
}

function lifecycleCommand(
  action: JournalEntryLifecycleAction,
  label: string,
  description: string,
  disabledReason: string | null,
  tone: ManualJournalLifecycleCommandViewModel["tone"],
  busyAction: JournalEntryLifecycleAction | null
): ManualJournalLifecycleCommandViewModel {
  return {
    action,
    label,
    description,
    disabledReason: busyAction && busyAction !== action ? `Lifecycle action ${busyAction} is already running.` : disabledReason,
    tone,
    busy: busyAction === action
  };
}

export function formatManualJournalLifecycleTransition(
  transition: JournalEntryLifecycleTransition
): ManualJournalLifecycleTransitionViewModel {
  const evidenceRows = transition.evidenceLinks.length > 0
    ? transition.evidenceLinks
    : ["No transition evidence links retained."];

  return {
    id: transition.transitionId,
    title: `${transition.action}: ${transition.fromStatus} -> ${transition.toStatus}`,
    detail: `${transition.actor} / ${formatDateTimeLabel(transition.recordedAtUtc)}${transition.notes ? ` / ${transition.notes}` : ""}`,
    auditLabel: `Audit ${transition.transitionId}`,
    correlationLabel: transition.correlationId ? `Correlation ${transition.correlationId}` : "No correlation id retained",
    evidenceLabel: transition.evidenceLinks.length > 0
      ? `${transition.evidenceLinks.length.toLocaleString()} evidence link(s)`
      : "No transition evidence links",
    evidenceTone: transition.evidenceLinks.length > 0 ? "success" : "outline",
    evidenceRows
  };
}

export function formatManualJournalLifecycleCorrection(
  draft: ManualJournalEntryDraft
): ManualJournalLifecycleCorrectionViewModel {
  const source = draft.reversalOfJournalEntryId
    ? `Reversal of ${draft.reversalOfJournalEntryId}`
    : draft.rebookedFromJournalEntryId
      ? `Rebook from ${draft.rebookedFromJournalEntryId}`
      : "Generated correction draft";

  return {
    id: draft.journalEntryId,
    title: draft.memo || "Generated correction draft",
    subtitle: `${draft.status} / v${draft.version} / ${draft.entryType}`,
    balanceLabel: `${formatCurrencyWithCode(draft.totalDebits, draft.currency)} debit / ${formatCurrencyWithCode(draft.totalCredits, draft.currency)} credit`,
    sourceLabel: source
  };
}
