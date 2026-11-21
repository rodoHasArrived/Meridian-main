import { useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import type { EntityOperationScope } from "@/types/entity-operations";
import type { IntercompanyObligationMonitor } from "@/types/intercompany-obligations";
import { EntityOperationTable, OperationError, OperationField, today, useEntityData } from "./accounting-screen.entity-operations.shared";
import { LegacyIntercompanyCoverageReview } from "./accounting-screen.entity-legacy-coverage";

const agreementLink = (id: string) => `/accounting/configure?agreementId=${encodeURIComponent(id)}#configure-section-agreements`;
function SafeDrillThrough({ href }: { href?: string | null }) {
  return href?.startsWith("/accounting/") && !href.includes("\\") ? <Link className="underline underline-offset-4" to={href}>Inspect retained source</Link> : null;
}
export function EntityObligationMonitor({ scope }: { scope: EntityOperationScope }) {
  const [asOf, setAsOf] = useState(() => new URLSearchParams(window.location.search).get("obligationAsOf") ?? today());
  const [horizon, setHorizon] = useState("30");
  const [request, setRequest] = useState<string | null>(null);
  const monitor = useEntityData<IntercompanyObligationMonitor>(request);
  const entityName = (id?: string | null) => scope.graph?.entities.find((item) => item.entityId === id)?.name ?? id ?? "Unknown entity";
  function evaluate() {
    const query = new URLSearchParams({ fundProfileId: scope.book.fundProfileId, ledgerBookId: scope.book.ledgerBookId, asOf, expiryHorizonDays: horizon });
    const path = `/api/ledger/intercompany/obligations?${query}`;
    if (path === request) monitor.refresh(); else setRequest(path);
  }
  return <section className="space-y-4" aria-label="Intercompany obligation monitoring"><h3 className="font-semibold">Contract coverage and obligation monitor</h3><p className="text-sm text-muted-foreground">Read-only advisories for the selected authoritative book. Findings and unavailable observations do not change close readiness or posting eligibility.</p>
    <form onSubmit={(event) => { event.preventDefault(); evaluate(); }} className="space-y-3"><FormGrid columns={3}><OperationField label="Obligation monitoring as of" value={asOf} onChange={(value) => { setAsOf(value); setRequest(null); }} type="date" required /><OperationField label="Agreement expiry horizon (calendar days)" value={horizon} onChange={(value) => { setHorizon(value); setRequest(null); }} type="number" required /><p className="self-center text-sm">{scope.book.displayName} · {scope.book.baseCurrency}</p></FormGrid><Button type="submit" variant="outline" disabled={!scope.capabilities.canViewLedger || monitor.loading || !asOf || !/^\d+$/.test(horizon)}>Evaluate intercompany obligations</Button></form>
    {!scope.capabilities.canViewLedger ? <p className="text-sm text-muted-foreground">Ledger report access is required to inspect intercompany obligations.</p> : null}
    <OperationError error={monitor.error} />{monitor.loading ? <p role="status">Reading authoritative agreements, lifecycle, and balance coverage…</p> : null}
    {monitor.data ? <><StatusBanner title={`${monitor.data.status} · ${monitor.data.isComplete ? "Complete observation" : "Incomplete observation"}`} tone={monitor.data.status === "Current" ? "info" : "warning"} detail={`As of ${monitor.data.asOf} · evaluated ${monitor.data.evaluatedAtUtc}`} />
      {monitor.data.findings.length ? <ul className="space-y-2" aria-label="Intercompany advisory findings">{monitor.data.findings.map((finding, index) => <li key={`${finding.code}:${finding.agreementId ?? finding.journalEntryId ?? index}`} className="space-y-1 rounded border border-border p-3 text-sm"><p className="font-semibold">{finding.severity} · {finding.message}</p><p className="text-muted-foreground">{finding.code}{finding.entityId ? ` · ${entityName(finding.entityId)}` : ""}</p><div className="flex flex-wrap gap-3">{finding.agreementId ? <Link className="underline underline-offset-4" to={agreementLink(finding.agreementId)}>Inspect agreement {finding.agreementVersion ?? ""}</Link> : null}<SafeDrillThrough href={finding.drillThrough} /></div></li>)}</ul> : <p className="text-sm">No advisory findings in the evaluated population.</p>}
      {monitor.data.balanceSchedule ? <div className="space-y-3"><h4 className="font-semibold">Canonical posted balance schedule</h4><p className="text-sm text-muted-foreground">Amounts and reviewed legacy attribution come from the shared balance schedule. Transaction and functional currencies remain separate.</p><EntityOperationTable label="Intercompany obligation balances" rows={monitor.data.balanceSchedule.rows} getRowId={(row) => [row.ledgerBookId, row.entityId, row.counterpartyEntityId, row.accountingArrangementId, row.accountingArrangementRevisionId, row.chartAccountNodeId, row.role, row.transactionCurrency, row.functionalCurrency, row.intercompanyAgreementId, row.intercompanyAgreementVersion, row.intercompanyAgreementPayloadHash].join(":")} emptyText="No posted intercompany balance rows in this observation." columns={[
        { id: "parties", label: "Posting entity → counterparty", rowHeader: true, render: (row) => `${entityName(row.entityId)} → ${entityName(row.counterpartyEntityId)}` },
        { id: "transaction", label: "Transaction balance", render: (row) => `${row.transactionSignedBalance} ${row.transactionCurrency}` },
        { id: "functional", label: "Functional balance", render: (row) => `${row.functionalSignedBalance} ${row.functionalCurrency}` },
        { id: "mapping", label: "Coverage authority", render: (row) => row.mappingVerified ? "Verified mapping" : "Unresolved mapping" },
        { id: "agreement", label: "Legal revision", render: (row) => row.intercompanyAgreementId ? <Link className="underline underline-offset-4" to={agreementLink(row.intercompanyAgreementId)}>Version {row.intercompanyAgreementVersion}</Link> : "No retained legal revision" },
        { id: "source", label: "Source evidence", render: (row) => <TechnicalDetails label={`${row.journalEntryIds.length} posted journals`}><ul className="space-y-1 break-all text-sm">{row.journalEntryIds.map((journalId) => <li key={journalId}><Link className="underline underline-offset-4" to={`/accounting/journal-entries/detail?${new URLSearchParams({ journalEntryId: journalId, ledgerBookId: row.ledgerBookId, fundProfileId: scope.book.fundProfileId })}`}>{journalId}</Link></li>)}</ul><p className="break-all text-sm">Binding revisions: {row.bindingRevisionIds.join(", ")}</p></TechnicalDetails> }
      ]} />{monitor.data.balanceSchedule.issues.map((issue, index) => <p key={index} className="text-sm text-warning">{issue.code} · {issue.message}</p>)}</div> : <p className="text-sm text-warning">The balance schedule is unavailable; coverage cannot be established from registry membership.</p>}
      <TechnicalDetails label="Guarantee disclosure register"><p className="mb-2 text-sm text-muted-foreground">Disclosure only. Guarantees never satisfy ordinary receivable/payable coverage.</p>{monitor.data.guarantees.length ? <ul className="space-y-3 text-sm">{monitor.data.guarantees.map((agreement) => <li key={agreement.agreementId}><Link className="underline underline-offset-4" to={agreementLink(agreement.agreementId)}>{agreement.code} · {agreement.name}</Link><p>{agreement.currency} · {agreement.effectiveStatus} · applicable version {agreement.applicableRevision?.agreementVersion ?? "None"}</p><p>Beneficiary {agreement.applicableRevision?.terms.terms?.structuredTerms?.guarantee?.beneficiaryReference ?? "Unstructured / unknown"} · limit {agreement.applicableRevision?.terms.terms?.structuredTerms?.guarantee?.limitAmount ?? "Unknown"}</p></li>)}</ul> : <p className="text-sm">No guarantee records in this observation.</p>}</TechnicalDetails>
      <TechnicalDetails label="Observation source versions and fingerprints"><p className="break-all text-sm">{monitor.data.sourceFingerprint}</p><ul className="space-y-1 break-all text-sm">{monitor.data.sourceVersions.map((source, index) => <li key={index}>{source.sourceKind} · {source.sourceId} · version {source.version} · {source.contentHash}</li>)}</ul></TechnicalDetails>
    </> : !request ? <p className="text-sm text-muted-foreground">Choose an as-of date and evaluate to inspect an explicit population.</p> : null}
    {scope.capabilities.canManageLedger ? <LegacyIntercompanyCoverageReview scope={scope} asOf={asOf} onSaved={() => { setRequest(null); }} /> : null}
  </section>;
}
