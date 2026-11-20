import type { LedgerAmountProof } from "@/types/ledger-amount-proof";
import type { AmountPassport, AmountPassportIdentity, AmountPassportRecord } from "@/types/amount-passport";
import { amountPassportHref } from "@/lib/amount-passport-api";
import type { PreparerAttestationEvidenceProjection } from "@/types/preparer-attestation-evidence";
import { PreparerAttestationEvidence } from "@/components/meridian/preparer-attestation-evidence";
import { AccountingPreparerAttestation } from "@/components/meridian/accounting-preparer-attestation";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

export interface NumberPassportItem {
  label: string;
  value: string;
  detail: string;
}

const componentLabels: Record<string, string> = {
  scope: "Scope", source: "Source records", calculation: "Computation and exchange rate",
  freshness: "Freshness", evidence: "Evidence links", history: "History", permissions: "Permissions"
};

export const amountPassportStatusLabels: Record<string, string> = {
  complete: "Complete", "review-required": "Review required", stale: "Stale",
  blocked: "Blocked", superseded: "Superseded", current: "Current",
  sourced: "Sourced", calculated: "Calculated", overridden: "Overridden", estimated: "Estimated",
  unavailable: "Unavailable", allowed: "View allowed", "read-only": "Cannot amend",
  "scope-mismatch": "Scope mismatch", restricted: "Permission denied", missing: "Source unavailable"
};

export function PassportFacts({ facts }: { facts: Array<[string, string | null | undefined]> }) {
  return <dl className="divide-y divide-border">
    {facts.map(([label, value]) => <div key={label} className="grid gap-1 py-2 sm:grid-cols-[minmax(7rem,1fr)_minmax(0,2fr)] sm:gap-3">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="min-w-0 [overflow-wrap:anywhere]">{value || "Unavailable"}</dd>
    </div>)}
  </dl>;
}

function passportScopeFacts(identity?: AmountPassportIdentity | null): Array<[string, string | null | undefined]> {
  const scope = identity?.scope;
  const brokerage = identity?.subjectKind === "position-valuation";
  return [
    ["As of", identity?.asOf], ["Ledger book", scope?.ledgerBookId ?? (brokerage ? "Not applicable" : null)],
    ["Basis", scope?.basis], ["Period", scope?.periodId ?? (brokerage ? "Not applicable" : null)],
    ["Fund", scope?.fundProfileId ?? "Not declared"], ["Entity", scope?.entityId ?? "Not declared"],
    ["Account", scope?.accountId ?? "Not declared"], ["Organization", scope?.organizationId ?? "Not declared"],
    ["Company", scope?.companyId], ["Tenant", scope?.tenantId],
    ["Subject ID", identity?.subjectId], ["Revision", identity?.revision]
  ];
}

export function AmountPassportFactsView({ passport, onFollow, baseline = false }: {
  passport: AmountPassport; onFollow: (identity: AmountPassportIdentity) => void; baseline?: boolean
}) {
  const { reference, freshness, permissions } = passport;
  const scope = passportScopeFacts(reference.identity);
  const regionLabel = (label: string) => baseline ? `Baseline ${label.toLowerCase()}` : label;
  const unresolved = passport.components.filter(item => item.status !== "available" && item.status !== "not-applicable");
  return <div className="space-y-4">
    {passport.history.some(item => item.kind === "successor") ? <p className="border-l-2 border-border pl-3 font-semibold">
      Superseded · Inspect the original value or follow its retained successor in History.
    </p> : null}
    <PassportFacts facts={[
      ["Exact retained value", passport.exactValue], ["Currency", reference.currency ?? (reference.unit === "position units" ? "Not applicable" : null)],
      ["Units", reference.unit], ["Origin", amountPassportStatusLabels[reference.origin]],
      ["Effective date", passport.effectiveDate ?? (reference.identity.subjectKind === "position-valuation" ? "See observation timestamp" : null)]
    ]} />
    <section aria-label={regionLabel("Scope")}>
      <h3 className="font-semibold">Scope</h3>
      <PassportFacts facts={scope.slice(0, 7)} />
      <details>
        <summary className="cursor-pointer font-medium">Identity and revision</summary>
        <PassportFacts facts={scope.slice(7)} />
      </details>
    </section>
    <section aria-label={regionLabel("Freshness")}>
      <h3 className="font-semibold">Freshness</h3>
      <PassportFacts facts={[
        ["Status", amountPassportStatusLabels[freshness.status] ?? freshness.status],
        ["Evaluated at", freshness.evaluatedAt], ["Policy", freshness.policy],
        [reference.identity.subjectKind === "position-valuation" ? "Snapshot retrieved at" : "Supporting evidence retained at", freshness.observedAt],
        ["Source age", freshness.ageMinutes == null ? null : `${freshness.ageMinutes} min`],
        ["Decision", freshness.detail]
      ]} />
    </section>
    <section aria-label={regionLabel("Computation and exchange rate")}>
      <h3 className="font-semibold">Computation and exchange rate</h3>
      <PassportFacts facts={[["Computation", passport.calculation], ["Exchange-rate rule", passport.exchangeRateRule]]} />
    </section>
    <PassportRecords title="Source records" regionLabel={regionLabel("Source records")} records={passport.sources} onFollow={onFollow} />
    <PassportRecords title="Evidence links" regionLabel={regionLabel("Evidence links")} records={passport.evidence} onFollow={onFollow} />
    <PassportRecords title="History" regionLabel={regionLabel("History")} records={passport.history} onFollow={onFollow} />
    <section aria-label={regionLabel("Permissions")}>
      <h3 className="font-semibold">Permissions</h3>
      <PassportFacts facts={[
        ["Inspect", amountPassportStatusLabels[permissions.view] ?? permissions.view],
        ["Amend", amountPassportStatusLabels[permissions.amend] ?? permissions.amend],
        ...(permissions.periodStatus ? [["Period status", permissions.periodStatus] as [string, string]] : []),
        ["Controls", permissions.detail]
      ]} />
    </section>
    {passport.accountingPreparerAttestations?.map(attestation => <p className="border-l-2 border-border pl-3" key={attestation.attestationId ?? attestation.disclosure}>
      {attestation.disclosure}
    </p>)}
    <section aria-label={regionLabel("Known gaps")}>
      <h3 className="font-semibold">Known gaps</h3>
      {unresolved.length || passport.gaps.length ? <ul className="list-disc space-y-1 pl-5">
        {unresolved.map(item => <li key={item.component}>
          <strong>{componentLabels[item.component] ?? item.component}</strong> · {item.detail}
        </li>)}
        {passport.gaps.filter(gap => !unresolved.some(item => item.detail === gap)).map(gap => <li key={gap}>{gap}</li>)}
      </ul> : <p>All declared proof components are available.</p>}
    </section>
  </div>;
}

function PassportRecords({ title, regionLabel, records, onFollow }: {
  title: string; regionLabel: string; records: AmountPassportRecord[]; onFollow: (identity: AmountPassportIdentity) => void
}) {
  return <section aria-label={regionLabel}>
    <h3 className="font-semibold">{title}</h3>
    {records.length === 0 ? <p className="text-muted-foreground">No records supplied.</p> :
      <ul className="divide-y divide-border">
        {records.map((record) => <li key={`${record.kind}:${record.id}`} className="py-2 [overflow-wrap:anywhere]">
          {record.passport ? <a className="font-medium underline underline-offset-4" href={amountPassportHref(record.passport)}
            onClick={event => {
              if (event.button !== 0 || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
              event.preventDefault();
              onFollow(record.passport!);
            }}>{record.label}</a>
            : record.href?.startsWith("/workstation/") &&
                !Array.from(record.href).some((character) => character === "\\" || character.charCodeAt(0) < 32)
              ? <a className="font-medium underline underline-offset-4" href={record.href} target="_blank" rel="noopener noreferrer">
                {record.label}<span className="sr-only"> (opens in a new tab)</span>
              </a> : <span>{record.label}</span>}
          <p className="font-mono text-xs text-muted-foreground">{record.id}</p>
          {record.contentHash ? <p className="font-mono text-xs text-muted-foreground">{record.contentHash}</p> : null}
        </li>)}
      </ul>}
  </section>;
}

export function NumberPassport(props: ({
  explorer: FinancialRecordExplorerDto;
  record: FinancialRecordExplorerSelectedRecordDto;
} | { proof: LedgerAmountProof; title: string }) & { inline?: boolean; preparerAttestations?: PreparerAttestationEvidenceProjection | null;
  sourceJournalEntryIds?: string[] }) {
  const items = "proof" in props
    ? buildLedgerAmountPassportItems(props.proof)
    : buildNumberPassportItems(props.explorer, props.record);
  const title = "proof" in props ? props.title : props.record.title;
  const Heading = "proof" in props ? "h3" : "h4";

  // Headline previews sit inside inline number readouts. Keep valid phrasing
  // content while sharing the exact same passport fields and evidence rules.
  if (props.inline) return <span role="group" className="mt-2 block border-t border-border pt-2" aria-label={`${title} Number Passport`}>
    <span className="block font-semibold">Number Passport</span>
    <span role="list" className="mt-2 block space-y-2">{items.map(item => <span role="listitem" key={item.label} className="block">
      <span className="block font-medium">{item.label}</span>
      <span className="block break-words tabular-nums">{item.value}</span>
      <span className="block text-muted-foreground">{item.detail}</span>
    </span>)}</span>
    {"proof" in props ? <AccountingPreparerAttestation proof={props.proof} sourceJournalEntryIds={props.sourceJournalEntryIds} inline /> : null}
    <PreparerAttestationEvidence projection={props.preparerAttestations}
      expectedScope={"proof" in props ? props.proof.scope : undefined} inline />
  </span>;

  return (
    <section className="rounded-md border border-border/70 bg-secondary/15 p-3" aria-label={`${title} Number Passport`}>
      <Heading className="text-xs font-semibold uppercase text-muted-foreground">Number Passport</Heading>
      <dl className="mt-2 grid gap-2">
        {items.map((item) => (
          <div key={item.label} className="rounded-md border border-border/60 bg-background/60 px-3 py-2">
            <dt className="text-xs text-muted-foreground">{item.label}</dt>
            <dd className="mt-1 break-all font-mono text-sm text-foreground">{item.value}</dd>
            <dd className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</dd>
          </div>
        ))}
      </dl>
      {"proof" in props ? <AccountingPreparerAttestation proof={props.proof} sourceJournalEntryIds={props.sourceJournalEntryIds} /> : null}
      <PreparerAttestationEvidence projection={props.preparerAttestations}
        expectedScope={"proof" in props ? props.proof.scope : undefined} />
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
    { label: "Amount", value: `${proof.amount} ${proof.currency}`, detail: "Selected amount from the shared evidence record." },
    { label: "Tenant", value: scope.tenantId, detail: "Authenticated evidence scope." },
    { label: "Company", value: scope.companyId, detail: "Authenticated evidence scope." },
    { label: "Fund", value: scope.fundProfileId, detail: "Exact retained fund identity." },
    { label: "Ledger book", value: scope.ledgerBookId, detail: "Exact retained ledger book identity." },
    { label: "Period", value: scope.periodId, detail: "Exact retained period identity." },
    { label: "Freshness", value: retained.length ? retained.map((item) => item.retainedAt).join("; ") : "Review required: no current retained evidence", detail: "Retention timestamps supplied by the evidence service." },
    { label: "Blockers", value: proof.warnings.length ? proof.warnings.join("; ") : proof.status === "Ready" ? "No blocker reported" : "Evidence review required", detail: "Shared evidence validation result." }
  ];
}
