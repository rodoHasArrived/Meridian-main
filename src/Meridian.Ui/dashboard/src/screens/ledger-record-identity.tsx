import { RecordIdentitySpine, useRecordWorkspaceContext } from "@/components/meridian/record-identity-spine";
import type { RecordIdentity } from "@/lib/record-workspace-context";
import { periodStatusLabel } from "@/screens/accounting-screen.posted-ledger.view-model";
import type { LedgerBook, LedgerPeriod, LedgerTrialBalanceLine } from "@/types";

export function LedgerFundIdentity({ book, period, periodError }: {
  book: LedgerBook;
  period: LedgerPeriod | null;
  periodError: boolean;
}) {
  const entityId = book.fundStructureNodeKind === "Entity" ? book.fundStructureNodeId : null;
  const bookName = book.displayName || book.ledgerBookId;
  const record: RecordIdentity = {
    kind: "fund",
    id: book.fundProfileId,
    name: book.fundProfileId,
    entity: entityId,
    book: bookName,
    status: period ? `Period ${periodStatusLabel(period.status).toLowerCase()}` : null,
    asOf: period?.endDate,
    trust: periodError ? "Ledger period unavailable" : "Ledger book on file",
    trustTone: periodError ? "review" : "ready",
    scope: {
      fundProfileId: book.fundProfileId,
      entityId,
      ledgerBookId: book.ledgerBookId,
      periodId: period?.periodId
    }
  };
  const context = useRecordWorkspaceContext(record, {
    ledgerBookId: book.ledgerBookId,
    periodId: period?.periodId
  });

  return <RecordIdentitySpine record={record} context={context} showName />;
}

export function ledgerAccountIdentity(
  line: LedgerTrialBalanceLine,
  source: { runId?: string; book?: LedgerBook | null; period?: LedgerPeriod | null } = {}
): RecordIdentity {
  const financialAccountId = line.financialAccountId ?? line.dimensions?.accountId;
  const entityId = line.entityScopeId ?? line.dimensions?.entityId;
  const hasBasis = Boolean(line.accountingBasis?.trim());
  return {
    kind: "account",
    id: financialAccountId || line.accountScopeId || line.accountName,
    name: line.accountName,
    identifier: line.accountScopeId,
    assetClass: line.security?.assetClass,
    entity: line.entityScopeDisplayName ?? entityId,
    book: source.book?.displayName ?? line.dimensions?.bookId,
    status: source.period ? "Posted balance" : null,
    asOf: source.period?.endDate,
    trust: hasBasis ? "Trial balance on file" : "Accounting basis missing",
    trustTone: hasBasis ? "ready" : "review",
    scope: {
      financialAccountId,
      accountScopeId: line.accountScopeId,
      entityId,
      ledgerBookId: source.book?.ledgerBookId ?? line.dimensions?.bookId,
      fundProfileId: source.book?.fundProfileId,
      periodId: source.period?.periodId,
      runId: source.runId
    }
  };
}
