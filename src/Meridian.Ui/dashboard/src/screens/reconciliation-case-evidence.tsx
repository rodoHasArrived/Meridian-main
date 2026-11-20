import { useEffect, useId, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { formatExactMagnitude, parseExactDecimal } from "@/lib/exact-decimal";
import { getReconciliationBreakAudit } from "@/lib/api/statement-connectors.api";
import { evidenceWorkbenchPath } from "@/lib/workspace";
import type { ReconciliationBreakMeasure, ReconciliationBreakQueueItem } from "@/types";
import { formatAccountingDate, formatDateTimeLabel } from "./accounting-screen.formatting";
import { lifecycleStateLabel } from "./reconciliation-break-lifecycle";
import { isStatementHandoffMarker } from "./reconciliation-casework-command";
import { originalComparisonScope, sourceObservationAge, sourceRunChangeLabel } from "./reconciliation-source-observation";
import "@/styles/reconciliation-workbench.css";

const MEASURE_LABELS: Record<ReconciliationBreakMeasure["kind"], string> = {
  Value: "Value",
  Quantity: "Quantity",
  CostBasis: "Cost basis"
};

function displayAmount(value: number | null, signed = false): string {
  const decimal = parseExactDecimal(value);
  if (!decimal) return "Unavailable";
  const sign = decimal.units === 0n ? "" : decimal.negative ? "−" : signed ? "+" : "";
  return `${sign}${formatExactMagnitude(decimal, decimal.scale).text}`;
}

function retainedResidual(measure: ReconciliationBreakMeasure): number | null {
  if (measure.unavailableReason || !Number.isFinite(measure.actual) || !Number.isFinite(measure.expected)) return null;
  return Number.isFinite(measure.variance) ? measure.variance : null;
}

function measureUnit(unit: string): string {
  return !unit || unit === "currency" ? "Currency unavailable" : unit;
}

export function caseQueueResidual(item: ReconciliationBreakQueueItem): { amount: string; unit: string | null } {
  const measures = item.measures ?? [];
  const reported = measures.filter((measure) => retainedResidual(measure) != null);
  if (reported.length > 1) return { amount: "Multiple", unit: null };
  if (reported.length === 1) return { amount: displayAmount(reported[0].variance, true), unit: measureUnit(reported[0].unit) };
  return measures.length > 0
    ? { amount: "Unavailable", unit: null }
    : { amount: displayAmount(item.variance, true), unit: "Unit unavailable" };
}

function CaseFact({ label, children }: { label: string; children?: ReactNode }) {
  return <div><dt>{label}</dt><dd>{children ?? "Unavailable"}</dd></div>;
}

function compareColumns(statement: boolean, prior: ReconciliationBreakQueueItem | null): DenseDataTableColumn<ReconciliationBreakMeasure>[] {
  const columns: DenseDataTableColumn<ReconciliationBreakMeasure>[] = [
    { id: "measure", label: "Measure", rowHeader: true, render: (measure) => <>{MEASURE_LABELS[measure.kind]}<span className="block text-xs font-normal text-muted-foreground">{measureUnit(measure.unit)}</span></> },
    { id: "observed", label: statement ? "Statement" : "Observed", numeric: true, render: (measure) => displayAmount(measure.actual) },
    { id: "expected", label: statement ? "Ledger" : "Expected", numeric: true, render: (measure) => displayAmount(measure.expected) },
    { id: "residual", label: "Residual", numeric: true, render: (measure) => <>
      {displayAmount(retainedResidual(measure), true)}
      {measure.tolerance != null ? <span className="block text-xs font-normal text-muted-foreground">Tolerance {displayAmount(measure.tolerance)}</span> : null}
    </> }
  ];
  if (prior) {
    columns.push({ id: "prior", label: "Prior residual", numeric: true, render: (measure) => {
      const matches = measureUnit(measure.unit) === "Currency unavailable" ? []
        : prior.measures?.filter((entry) => entry.kind === measure.kind && entry.unit === measure.unit) ?? [];
      const comparison = matches.length === 1 ? retainedResidual(matches[0]) : null;
      const current = retainedResidual(measure);
      return <>{displayAmount(comparison, true)}{comparison != null && current != null && comparison !== current ? (
        <span className="block text-xs font-normal text-muted-foreground">Residual changed</span>
      ) : null}</>;
    } });
  }
  return columns;
}

/** The same retained comparison is rendered on the import handoff and in the casework inspector. */
export function ReconciliationCaseEvidence({ item, relatedRows = [], onOpenCase, comparisonBreakId, onComparisonChange }: {
  item: ReconciliationBreakQueueItem;
  relatedRows?: readonly ReconciliationBreakQueueItem[];
  onOpenCase?: (breakId: string) => void;
  comparisonBreakId?: string | null;
  onComparisonChange?: (breakId: string | null) => void;
}) {
  const id = useId();
  const [priorId, setPriorId] = useState("");
  const lineage = item.lineage;
  const statement = item.sourceType === "statement";
  const status = item.lifecycleState ? lifecycleStateLabel(item.lifecycleState)
    : item.status === "Dismissed" ? item.status : lifecycleStateLabel(item.status);
  const sourceRun = item.sourceImportId ?? item.runId;
  const related = lineage ? relatedRows.filter((row) => row.breakId !== item.breakId
    && row.lineage?.lineageId === lineage.lineageId
    && row.tenantId === item.tenantId && row.companyId === item.companyId
    && row.fundAccountId === item.fundAccountId && row.ledgerBookId === item.ledgerBookId
    && row.accountingPeriodId === item.accountingPeriodId) : [];
  const earlier = related.filter((row) => (row.sourceImportId ?? row.runId) !== sourceRun
    && Date.parse(row.detectedAt) < Date.parse(item.detectedAt));
  const requestedBaseline = comparisonBreakId === undefined ? priorId : comparisonBreakId;
  const baseline = earlier.find((row) => row.breakId === requestedBaseline);
  const [comparisonProof, setComparisonProof] = useState<{
    item: ReconciliationBreakQueueItem;
    baseline: ReconciliationBreakQueueItem;
    sameScope: boolean | null;
  } | null>(null);
  const currentScope = originalComparisonScope(item);
  const priorScope = baseline ? originalComparisonScope(baseline) : null;
  const proof = comparisonProof?.item === item && comparisonProof.baseline === baseline ? comparisonProof : null;
  const comparable = currentScope && priorScope ? currentScope === priorScope : proof?.sameScope;
  useEffect(() => {
    if (!baseline || (currentScope && priorScope)) return;
    let active = true;
    Promise.all([
      currentScope ?? getReconciliationBreakAudit(item.breakId).then((events) => originalComparisonScope(item, events)),
      priorScope ?? getReconciliationBreakAudit(baseline.breakId).then((events) => originalComparisonScope(baseline, events))
    ]).then(([current, prior]) => {
      if (active) setComparisonProof({ item, baseline, sameScope: current && prior ? current === prior : null });
    }).catch(() => {
      if (active) setComparisonProof({ item, baseline, sameScope: null });
    });
    return () => { active = false; };
  }, [baseline, currentScope, priorScope, item]);
  const measures = item.measures ?? [];
  const unavailable = [...new Set(measures.map((measure) => measure.unavailableReason).filter(Boolean))];
  const successor = item.supersedingBreakId && relatedRows.find((row) => row.breakId === item.supersedingBreakId);
  const evidence = [...new Set([...(item.evidenceLinks ?? []), ...(item.breakExplanation?.evidenceLinks ?? [])])]
    .filter((reference) => !isStatementHandoffMarker(reference));
  const ledgerEvidence = evidence.filter((reference) => reference.startsWith("journal:") || reference.startsWith("ledger:"));

  return (
    <div className="reconciliation-case-evidence">
      <p className="text-sm text-foreground">{item.reason}</p>
      {lineage ? <p className="text-sm text-muted-foreground"><strong className="text-foreground">{sourceRunChangeLabel(lineage)}</strong> · Occurrence {lineage.occurrenceNumber} · {sourceObservationAge(lineage)}</p> : null}
      <dl className="reconciliation-case-facts">
        <CaseFact label="Case ID">{item.breakId}</CaseFact>
        <CaseFact label="Case version">{item.version}</CaseFact>
        <CaseFact label="Case status">{status}</CaseFact>
        <CaseFact label="Owner">{item.assigneeDisplayName ?? item.assignedTo ?? "Unassigned"}</CaseFact>
        <CaseFact label="Severity">{item.severity}</CaseFact>
        <CaseFact label="SLA">{[item.slaBadgeLabel ?? item.slaState, item.slaDueAt && `Due ${formatDateTimeLabel(item.slaDueAt)}`].filter(Boolean).join(" · ") || null}</CaseFact>
      </dl>

      <section aria-labelledby={`${id}-compare`} className="reconciliation-case-section">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h3 id={`${id}-compare`} className="text-sm font-semibold">{statement ? "Statement and ledger" : "Observed and expected"}</h3>
          {measures.length > 0 ? <p className="text-xs text-muted-foreground">Residual = {statement ? "statement − ledger" : "observed − expected"}</p> : null}
        </div>
        {measures.length > 0 ? <>
          <DenseDataTable columns={compareColumns(statement, comparable ? baseline : null)} rows={measures}
            getRowId={(measure) => `${measure.kind}:${measure.unit}:${measures.indexOf(measure)}`} ariaLabel="Retained residual components"
            emptyText="No retained residual components."
            caption="The retained residual is the observed amount minus the expected amount in the supplied unit." />
          {unavailable.length > 0 ? <TechnicalDetails label="Unavailable observations">
            <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">{unavailable.map((reason) => <li key={reason}>{reason}</li>)}</ul>
          </TechnicalDetails> : null}
        </> : <StatusBanner tone="info" role="status" title="Comparison unavailable" detail="This case has no retained pair of observed and expected amounts." />}
        <dl className="reconciliation-case-facts">
          <CaseFact label="Fund account">{item.fundAccountId}</CaseFact>
          <CaseFact label="Ledger book">{item.ledgerBookId}</CaseFact>
          <CaseFact label="Accounting period">{item.accountingPeriodId}</CaseFact>
          <CaseFact label="As of">{formatAccountingDate(item.asOfDate)}</CaseFact>
          <CaseFact label="Source">{item.sourceSystem}</CaseFact>
          <CaseFact label="Source record">{item.sourceReference}</CaseFact>
          {ledgerEvidence.length > 0 ? <CaseFact label="Ledger evidence">{ledgerEvidence.join(", ")}</CaseFact> : null}
          <CaseFact label={item.sourceImportId ? "Source import" : "Source run"}>{sourceRun}</CaseFact>
          <CaseFact label="Tolerance policy">{item.toleranceProfileId ? `${item.toleranceProfileId} · Version unavailable` : null}</CaseFact>
        </dl>
        {earlier.length > 0 ? <label className="grid gap-1 text-sm">
          Compare with an earlier retained case
          <Select value={baseline?.breakId ?? ""} onChange={(event) => {
            if (onComparisonChange) onComparisonChange(event.target.value || null);
            else setPriorId(event.target.value);
          }} placeholder="Choose a case">
            {earlier.map((row) => <option key={row.breakId} value={row.breakId}>{formatAccountingDate(row.detectedAt)} · {row.sourceImportId ?? row.runId} · {row.breakId}</option>)}
          </Select>
        </label> : null}
        {requestedBaseline && !baseline ? <p role="status" className="text-sm text-muted-foreground">The earlier case in this link is unavailable in the loaded lineage.</p> : null}
        {baseline && comparable === false ? <StatusBanner role="status" tone="warning" title="Comparison scope changed" detail="The selected runs have different retained comparison scopes." /> : null}
        {baseline && comparable == null ? <p role="status" className="text-sm text-muted-foreground">
          {proof ? "The original run comparison scope is unavailable." : "Checking original run comparison scope…"}
        </p> : null}
      </section>

      <section aria-labelledby={`${id}-lineage`} className="reconciliation-case-section">
        <h3 id={`${id}-lineage`} className="text-sm font-semibold">Source lineage</h3>
        {!lineage ? <p className="text-sm text-muted-foreground">No retained source observation.</p> : null}
        {lineage ? <dl className="reconciliation-case-facts">
          <CaseFact label="Last observed run">{lineage.lastObservedRunId}</CaseFact>
          <CaseFact label="Last observed">{formatDateTimeLabel(lineage.lastObservedAt)}</CaseFact>
          {lineage.clearedByRunId ? <CaseFact label="Cleared by run">{lineage.clearedByRunId}</CaseFact> : null}
          {lineage.clearedAt ? <CaseFact label="Source cleared at">{formatDateTimeLabel(lineage.clearedAt)}</CaseFact> : null}
        </dl> : null}
        {item.reopenedAt ? <p className="text-sm">Reopened {formatDateTimeLabel(item.reopenedAt)}{item.reopenReason ? ` · ${item.reopenReason}` : null}</p> : null}
        {item.supersedingBreakId ? <p className="break-words text-sm">Superseded by {item.supersedingBreakId}{item.dispositionReason ? ` · ${item.dispositionReason}` : null}</p> : null}
        {onOpenCase && (baseline || successor) ? <div className="flex flex-wrap gap-2">
          {baseline ? <Button size="sm" variant="outline" onClick={() => onOpenCase(baseline.breakId)}>Open earlier case</Button> : null}
          {successor ? <Button size="sm" variant="outline" onClick={() => onOpenCase(successor.breakId)}>Open successor case</Button> : null}
        </div> : null}
        <TechnicalDetails label="Evidence identifiers">
          <dl className="reconciliation-case-facts">
            <CaseFact label="Lineage ID">{lineage?.lineageId}</CaseFact>
            <CaseFact label="Occurrence ID">{lineage?.occurrenceId}</CaseFact>
            <CaseFact label="Latest lineage scope">{lineage?.comparisonScopeId}</CaseFact>
            <CaseFact label="Source break ID">{item.sourceBreakId}</CaseFact>
            <CaseFact label="Source fingerprint">{item.sourceFingerprint}</CaseFact>
          </dl>
          {evidence.length > 0 ? <ul aria-label="Retained evidence references" className="mt-2 space-y-1 break-all font-mono text-xs">
            {evidence.map((reference) => <li key={reference}>{reference}</li>)}
          </ul> : null}
        </TechnicalDetails>
        <Button asChild size="sm" variant="outline" className="w-fit">
          <Link to={evidenceWorkbenchPath("reconciliation-break", item.breakId)}>Review retained evidence</Link>
        </Button>
      </section>
    </div>
  );
}
