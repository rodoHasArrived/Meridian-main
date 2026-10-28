import type { LedgerAmountProof } from "@/types/ledger-amount-proof";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

export interface NumberPassportItem {
  label: string;
  value: string;
  detail: string;
}

export function NumberPassport(props: {
  explorer: FinancialRecordExplorerDto;
  record: FinancialRecordExplorerSelectedRecordDto;
} | { proof: LedgerAmountProof; title: string }) {
  const items = "proof" in props
    ? buildLedgerAmountPassportItems(props.proof)
    : buildNumberPassportItems(props.explorer, props.record);
  const title = "proof" in props ? props.title : props.record.title;
  const Heading = "proof" in props ? "h3" : "h4";

  return (
    <section className="rounded-md border border-border/70 bg-secondary/15 p-3" aria-label={`${title} Number Passport`}>
      <Heading className="text-xs font-semibold uppercase text-muted-foreground">Number Passport</Heading>
      <dl className="mt-2 grid gap-2">
        {items.map((item) => (
          <div key={item.label} className="rounded-md border border-border/60 bg-background/60 px-3 py-2">
            <dt className="text-[11px] text-muted-foreground">{item.label}</dt>
            <dd className="mt-1 break-all font-mono text-sm text-foreground">{item.value}</dd>
            <dd className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

export function buildNumberPassportItems(
  explorer: FinancialRecordExplorerDto,
  record: FinancialRecordExplorerSelectedRecordDto
): NumberPassportItem[] {
  // Display copy and routes are never evidence identifiers. A similarly named account,
  // symbol or action must not acquire another record's proof by substring matching.
  const reconciliation = proofById(record, "reconciliation-case") ?? "Review required: no reconciliation evidence";
  const approvals = proofById(record, "approval-gate") ?? "Review required: no approval evidence";
  const reportUsage = proofById(record, "report-line") ?? "Review required: no report usage evidence";
  const evidencePacket = proofById(record, "evidence-packet") ?? "Review required: no evidence packet";
  const auditTrail = proofById(record, "audit-trail") ?? "Review required: no audit trail evidence";
  const freshness = "Review required: no structured freshness marker";
  const blockers = record.tone === "Danger" ? record.description : "Review required: proof completeness is unverified";

  return [
    {
      label: "Source",
      value: record.recordType,
      detail: record.subtitle || explorer.title
    },
    {
      label: "Freshness",
      value: freshness,
      detail: explorer.sourceState
    },
    {
      label: "Reconciliation",
      value: reconciliation,
      detail: "Related record navigation; scoped amount evidence has not been verified."
    },
    {
      label: "Approvals",
      value: approvals,
      detail: "Related record navigation; scoped approval evidence has not been verified."
    },
    {
      label: "Report Usage",
      value: reportUsage,
      detail: "Related record navigation; scoped report usage has not been verified."
    },
    {
      label: "Blockers",
      value: blockers,
      detail: record.description
    },
    {
      label: "Evidence Packet",
      value: evidencePacket,
      detail: "Evidence must be explicitly retained for this selected record."
    },
    {
      label: "Audit Trail",
      value: auditTrail,
      detail: "Related record navigation; scoped audit evidence has not been verified."
    }
  ];
}

function proofById(record: FinancialRecordExplorerSelectedRecordDto, id: string): string | null {
  const relationships = [...record.usedIn, ...record.impacts].filter((item) => item.relationshipId === id);
  const actions = record.proofActions.filter((item) => item.actionId === id && item.isEnabled);
  const matches = [...relationships, ...actions];
  return matches.length === 1 && matches[0]?.href?.trim()
    ? `Review required: unverified record navigation ${matches[0].href}` : null;
}

export function buildLedgerAmountPassportItems(proof: LedgerAmountProof): NumberPassportItem[] {
  const { scope } = proof;
  const retained = proof.evidence.filter((item) => item.retainedAt && Number.isFinite(Date.parse(item.retainedAt)) && Date.parse(item.retainedAt) <= Date.now() && item.status === "Ready");
  return [
    { label: "Source", value: proof.subjectId, detail: proof.subjectId.startsWith("report:")
      ? "Generated report amount bound to its retained ledger population."
      : "Immutable posted journal line and debit or credit side." },
    { label: "Amount", value: `${proof.amount} ${proof.currency}`, detail: "Selected amount from the shared evidence payload." },
    { label: "Tenant", value: scope.tenantId, detail: "Authenticated evidence scope." },
    { label: "Company", value: scope.companyId, detail: "Authenticated evidence scope." },
    { label: "Fund", value: scope.fundProfileId, detail: "Exact retained fund identity." },
    { label: "Ledger book", value: scope.ledgerBookId, detail: "Exact retained ledger book identity." },
    { label: "Period", value: scope.periodId, detail: "Exact retained period identity." },
    { label: "Freshness", value: retained.length ? retained.map((item) => item.retainedAt).join("; ") : "Review required: no current retained evidence", detail: "Retention timestamps supplied by the evidence service." },
    { label: "Blockers", value: proof.warnings.length ? proof.warnings.join("; ") : proof.status === "Ready" ? "No blocker reported" : "Evidence review required", detail: "Shared evidence validation result." }
  ];
}
