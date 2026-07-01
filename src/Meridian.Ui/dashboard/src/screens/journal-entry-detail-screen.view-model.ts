import type {
  JournalEntryLifecycleTransition,
  LedgerJournalLine,
  ManualJournalEntryDraft,
  ManualJournalEntryStatus
} from "@/types";

export type JournalEntryDetailDataCompleteness = "full" | "summary-only" | "not-found";
export type JournalEntryDetailStatusTone = "default" | "success" | "warning" | "danger";

export interface JournalEntryDetailSummaryField {
  label: string;
  value: string;
}

export interface JournalEntryDetailLineRow {
  lineId: string;
  account: string;
  debit?: number;
  credit?: number;
  description: string | null;
  evidenceLink: string | null;
}

export interface JournalEntryDetailLifecycleRow {
  transitionId: string;
  label: string;
  actor: string;
  recordedAtUtc: string;
  notes: string | null;
}

export interface JournalEntryDetailEvidenceRow {
  attachmentId: string;
  displayName: string;
  uri: string;
  addedBy: string;
  addedAtUtc: string;
}

export interface JournalEntryDetailViewState {
  dataCompleteness: JournalEntryDetailDataCompleteness;
  journalEntryId: string;
  title: string;
  statusLabel: string;
  statusTone: JournalEntryDetailStatusTone;
  summaryFields: JournalEntryDetailSummaryField[];
  currency: string;
  lines: JournalEntryDetailLineRow[];
  totalDebits: number;
  totalCredits: number;
  lifecycle: JournalEntryDetailLifecycleRow[];
  evidence: JournalEntryDetailEvidenceRow[];
  summaryOnlyNotice: string | null;
  notFoundText: string | null;
}

function journalEntryStatusTone(status: ManualJournalEntryStatus): JournalEntryDetailStatusTone {
  switch (status) {
    case "Posted":
    case "Approved":
      return "success";
    case "Rejected":
    case "NeedsFix":
      return "danger";
    case "Submitted":
      return "warning";
    default:
      return "default";
  }
}

function buildLifecycleRow(transition: JournalEntryLifecycleTransition): JournalEntryDetailLifecycleRow {
  return {
    transitionId: transition.transitionId,
    label: `${transition.fromStatus} -> ${transition.toStatus} (${transition.action})`,
    actor: transition.actor,
    recordedAtUtc: transition.recordedAtUtc,
    notes: transition.notes ?? null
  };
}

export function buildJournalEntryDetailViewState({
  journalEntryId,
  draft,
  journalLine
}: {
  journalEntryId: string;
  draft: ManualJournalEntryDraft | null;
  journalLine: LedgerJournalLine | null;
}): JournalEntryDetailViewState {
  if (draft) {
    return {
      dataCompleteness: "full",
      journalEntryId,
      title: draft.memo || draft.journalEntryId,
      statusLabel: draft.status,
      statusTone: journalEntryStatusTone(draft.status),
      summaryFields: [
        { label: "Journal entry", value: draft.journalEntryId },
        { label: "Accounting date", value: draft.accountingDate },
        { label: "Fund", value: draft.fundProfileId },
        { label: "Ledger book", value: draft.ledgerBookId ?? "Unassigned" },
        { label: "Basis", value: draft.accountingBasis },
        { label: "Prepared by", value: draft.preparedBy },
        { label: "Currency", value: draft.currency }
      ],
      currency: draft.currency,
      lines: draft.lines.map((line) => ({
        lineId: line.lineId,
        account: line.accountPath,
        debit: line.side === "Debit" ? line.amount : undefined,
        credit: line.side === "Credit" ? line.amount : undefined,
        description: line.description ?? null,
        evidenceLink: line.evidenceLink ?? null
      })),
      totalDebits: draft.totalDebits,
      totalCredits: draft.totalCredits,
      lifecycle: (draft.lifecycleTransitions ?? []).map(buildLifecycleRow),
      evidence: (draft.evidenceAttachments ?? []).map((attachment) => ({
        attachmentId: attachment.attachmentId,
        displayName: attachment.displayName,
        uri: attachment.uri,
        addedBy: attachment.addedBy,
        addedAtUtc: attachment.addedAtUtc
      })),
      summaryOnlyNotice: null,
      notFoundText: null
    };
  }

  if (journalLine) {
    return {
      dataCompleteness: "summary-only",
      journalEntryId,
      title: journalLine.description || journalLine.journalEntryId,
      statusLabel: "Posted (summary only)",
      statusTone: "default",
      summaryFields: [
        { label: "Journal entry", value: journalLine.journalEntryId },
        { label: "Posted at", value: journalLine.timestamp },
        { label: "Line count", value: String(journalLine.lineCount) }
      ],
      currency: "USD",
      lines: [],
      totalDebits: journalLine.totalDebits,
      totalCredits: journalLine.totalCredits,
      lifecycle: [],
      evidence: [],
      summaryOnlyNotice: "This entry isn't in the manual workbench, so only the run-ledger summary is available. Line-level detail, lifecycle history, and evidence are not shown for system-posted entries.",
      notFoundText: null
    };
  }

  return {
    dataCompleteness: "not-found",
    journalEntryId,
    title: journalEntryId,
    statusLabel: "Not found",
    statusTone: "warning",
    summaryFields: [],
    currency: "USD",
    lines: [],
    totalDebits: 0,
    totalCredits: 0,
    lifecycle: [],
    evidence: [],
    summaryOnlyNotice: null,
    notFoundText: `No journal entry matching "${journalEntryId}" was found in the manual workbench or the selected run's ledger.`
  };
}
