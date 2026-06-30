import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

export interface NumberPassportItem {
  label: string;
  value: string;
  detail: string;
}

export function NumberPassport({
  explorer,
  record
}: {
  explorer: FinancialRecordExplorerDto;
  record: FinancialRecordExplorerSelectedRecordDto;
}) {
  const items = buildNumberPassportItems(explorer, record);

  return (
    <section className="rounded-md border border-border/70 bg-secondary/15 p-3" aria-label={`${record.title} Number Passport`}>
      <h4 className="text-xs font-semibold uppercase text-muted-foreground">Number Passport</h4>
      <dl className="mt-2 grid gap-2">
        {items.map((item) => (
          <div key={item.label} className="rounded-md border border-border/60 bg-background/60 px-3 py-2">
            <dt className="text-[11px] text-muted-foreground">{item.label}</dt>
            <dd className="mt-1 font-mono text-sm text-foreground">{item.value}</dd>
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
  const reconciliation = findProofText(record, "reconciliation") ?? "No reconciliation link on selected record";
  const approvals = findProofText(record, "approval", "approve") ?? "No approval link on selected record";
  const reportUsage = findProofText(record, "report") ?? "No report usage link on selected record";
  const evidencePacket = firstNonBlank(findProofText(record, "passport"), record.proofActions[0]?.href, record.fullRecordHref) ?? "No evidence packet link";
  const auditTrail = firstNonBlank(findProofText(record, "audit"), record.fullRecordHref, explorer.sourceState) ?? "No audit trail marker";
  const freshness = firstNonBlank(
    findFieldValue(record, "retrieved", "updated", "as of", "timestamp", "date"),
    explorer.sourceState
  ) ?? "No freshness marker";
  const blockers = record.tone === "Danger"
    ? record.description
    : "No blocker flagged on selected record";

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
      detail: "Reconciliation relationship or field retained by the selected FREX record."
    },
    {
      label: "Approvals",
      value: approvals,
      detail: "Approval relationship, action, or field retained by the selected FREX record."
    },
    {
      label: "Report Usage",
      value: reportUsage,
      detail: "Report-line or package relationship retained by the selected FREX record."
    },
    {
      label: "Blockers",
      value: blockers,
      detail: record.description
    },
    {
      label: "Evidence Packet",
      value: evidencePacket,
      detail: record.proofActions[0]?.description || "Primary evidence route for this selected number."
    },
    {
      label: "Audit Trail",
      value: auditTrail,
      detail: "Audit, full-record, or source-state route retained with the selected number."
    }
  ];
}

function findFieldValue(record: FinancialRecordExplorerSelectedRecordDto, ...tokens: string[]): string | null {
  const item = record.fields.find((field) => {
    const haystack = `${field.label} ${field.value} ${field.detail}`.toLowerCase();
    return tokens.some((token) => haystack.includes(token));
  });

  return firstNonBlank(item?.value, item?.detail) ?? null;
}

function findProofText(record: FinancialRecordExplorerSelectedRecordDto, ...tokens: string[]): string | null {
  const relationship = [...record.usedIn, ...record.impacts].find((item) => {
    const haystack = `${item.label} ${item.description} ${item.href}`.toLowerCase();
    return tokens.some((token) => haystack.includes(token));
  });

  if (relationship) {
    return firstNonBlank(relationship.href, relationship.label);
  }

  const action = record.proofActions.find((item) => {
    const haystack = `${item.label} ${item.description} ${item.href}`.toLowerCase();
    return tokens.some((token) => haystack.includes(token));
  });

  if (action) {
    return firstNonBlank(action.href, action.label);
  }

  return findFieldValue(record, ...tokens);
}

function firstNonBlank(...values: Array<string | null | undefined>): string | null {
  return values.find((value) => value !== undefined && value !== null && value.trim().length > 0) ?? null;
}
