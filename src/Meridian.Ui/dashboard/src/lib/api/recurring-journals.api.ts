import { apiGetJson } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

/** Shared wire contract: Meridian.Contracts.Workstation.RecurringJournalOccurrenceDto. */
export interface RecurringJournalOccurrence {
  occurrenceId: string;
  scheduleId: string;
  scheduleVersion: number;
  templateId: string;
  templateVersion: number;
  effectiveDate: string;
  fundProfileId: string;
  ledgerBookId: string;
  entityId: string;
  periodId: string | null;
  state: string;
  journalEntryId: string | null;
  approvalStatus: string | null;
  blockers: string[];
  sourceEvidenceReferences: string[];
  periodLockOwner: string | null;
  governedReopenPath: string | null;
}

export interface RecurringJournalScope {
  fundProfileId: string;
  ledgerBookId: string;
  entityId: string;
}

export interface RecurringJournalQueue extends RecurringJournalScope {
  occurrences: RecurringJournalOccurrence[];
}

export async function getRecurringJournalQueue(scope: RecurringJournalScope, signal?: AbortSignal): Promise<RecurringJournalQueue> {
  const query = new URLSearchParams({ ...scope });
  const result = await apiGetJson<RecurringJournalQueue>(
    `${UI_API_ROUTES.LedgerJournalAutomationRecurringOccurrences}?${query}`,
    { signal, allowDevelopmentFallback: false }
  );
  const matches = (candidate: RecurringJournalScope) => candidate.fundProfileId === scope.fundProfileId
    && candidate.ledgerBookId.toLowerCase() === scope.ledgerBookId.toLowerCase()
    && candidate.entityId === scope.entityId;
  if (!matches(result) || !Array.isArray(result.occurrences) || result.occurrences.some((row) => !matches(row))) {
    throw new Error("Recurring journal queue does not match the selected fund, book and entity.");
  }
  return result;
}
