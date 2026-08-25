import type {
  JournalEntryLifecycleTransition,
  LedgerJournalLine,
  LedgerPostedJournalEntry,
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
  /**
   * The date to show against this line. Posted lines carry their own timestamp, which is the only
   * date a posted entry has — there is no draft accounting date to fall back on — so the
   * projection must keep it or the detail table renders a blank Date column for the whole entry.
   */
  date: string | null;
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
  journalLine,
  postedEntry = null
}: {
  journalEntryId: string;
  draft: ManualJournalEntryDraft | null;
  journalLine: LedgerJournalLine | null;
  /**
   * The governed period's own entry, when the drill-through came from a posted journal. It
   * carries full posting lines, so a posted entry is not reduced to a summary that discards
   * detail the response already contained.
   */
  postedEntry?: LedgerPostedJournalEntry | null;
}): JournalEntryDetailViewState {
  if (draft) {
    const attachedEvidence = (draft.evidenceAttachments ?? []).map((attachment) => ({
      attachmentId: attachment.attachmentId,
      displayName: attachment.displayName,
      uri: attachment.uri,
      addedBy: attachment.addedBy,
      addedAtUtc: attachment.addedAtUtc
    }));
    const attachedUris = new Set(attachedEvidence.map((attachment) => attachment.uri));
    const linkedEvidence = (draft.evidenceLinks ?? [])
      .filter((uri) => !attachedUris.has(uri))
      .map((uri, index) => ({
        attachmentId: `retained-evidence-${index + 1}`,
        displayName: `Retained posting evidence ${index + 1}`,
        uri,
        addedBy: draft.preparedBy,
        addedAtUtc: draft.updatedAtUtc
      }));

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
        date: draft.accountingDate ?? null,
        debit: line.side === "Debit" ? line.amount : undefined,
        credit: line.side === "Credit" ? line.amount : undefined,
        description: line.description ?? null,
        evidenceLink: line.evidenceLink ?? null
      })),
      totalDebits: draft.totalDebits,
      totalCredits: draft.totalCredits,
      lifecycle: (draft.lifecycleTransitions ?? []).map(buildLifecycleRow),
      evidence: [...attachedEvidence, ...linkedEvidence],
      summaryOnlyNotice: null,
      notFoundText: null
    };
  }

  // A posted entry from the governed period carries its own lines; render them rather than
  // reporting "summary only" over detail the response already returned.
  if (postedEntry && postedEntry.lines.length > 0) {
    return {
      dataCompleteness: "full",
      journalEntryId,
      title: postedEntry.description || postedEntry.journalEntryId,
      statusLabel: "Posted",
      statusTone: "default",
      summaryFields: [
        { label: "Journal entry", value: postedEntry.journalEntryId },
        { label: "Posted at", value: postedEntry.timestamp },
        { label: "Ledger book", value: postedEntry.ledgerBookId ?? "Unassigned" },
        { label: "Basis", value: postedEntry.accountingBasis ?? "Primary" },
        { label: "Line count", value: String(postedEntry.lines.length) }
      ],
      currency: "USD",
      lines: postedEntry.lines.map((line, index) => ({
        lineId: line.entryId || `posted-line-${index + 1}`,
        account: line.accountName,
        date: line.timestamp || postedEntry.timestamp || null,
        debit: line.debit > 0 ? line.debit : undefined,
        credit: line.credit > 0 ? line.credit : undefined,
        description: line.description ?? null,
        evidenceLink: null
      })),
      totalDebits: postedEntry.totalDebits,
      totalCredits: postedEntry.totalCredits,
      lifecycle: [],
      evidence: [],
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
    title: "Journal entry not found",
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
    notFoundText: "No matching journal entry was found in the manual workbench or the selected run's ledger."
  };
}
