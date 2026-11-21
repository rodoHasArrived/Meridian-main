import { useEffect, useId, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { AmountPassportTrigger } from "@/components/meridian/amount-passport";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { formatExactMagnitude, parseExactDecimal } from "@/lib/exact-decimal";
import { getReconciliationBreakAudit } from "@/lib/api/statement-connectors.api";
import { evidenceWorkbenchPath } from "@/lib/workspace";
import type { ReconciliationBreakMeasure, ReconciliationBreakQueueItem, RetainedStatementCaseEvidence, StatementMatchAllocation, StatementMatchResidual } from "@/types";
import { formatAccountingDate, formatDateTimeLabel } from "./accounting-screen.formatting";
import { lifecycleStateLabel } from "./reconciliation-break-lifecycle";
import { isStatementHandoffMarker } from "./reconciliation-casework-command";
import { originalComparisonScope, sourceObservationAge, sourceRunChangeLabel } from "./reconciliation-source-observation";
import "@/styles/reconciliation-workbench.css";
import { caseMeasureAmount } from "./reconciliation-case-values";

const MEASURE_LABELS: Record<ReconciliationBreakMeasure["kind"], string> = {
  Value: "Value",
  Quantity: "Quantity",
  CostBasis: "Cost basis"
};

function displayAmount(value: string | number | null, signed = false): string {
  const decimal = parseExactDecimal(value);
  if (!decimal) return "Unavailable";
  const sign = decimal.units === 0n ? "" : decimal.negative ? "−" : signed ? "+" : "";
  return `${sign}${formatExactMagnitude(decimal, decimal.scale).text}`;
}

function retainedResidual(measure: ReconciliationBreakMeasure): string | number | null {
  if (measure.unavailableReason || !parseExactDecimal(caseMeasureAmount(measure, "actual")) || !parseExactDecimal(caseMeasureAmount(measure, "expected"))) return null;
  const residual = caseMeasureAmount(measure, "variance");
  return parseExactDecimal(residual) ? residual : null;
}

function measureUnit(unit: string): string {
  return !unit || unit === "currency" ? "Currency unavailable" : unit;
}

export function caseQueueResidual(item: ReconciliationBreakQueueItem): { amount: string; unit: string | null } {
  if (item.statementEvidence) {
    const { residuals } = item.statementEvidence.match;
    if (residuals.length > 1) return { amount: "Multiple", unit: null };
    if (residuals.length === 1) return { amount: displayAmount(residuals[0].residual, true), unit: residuals[0].unit };
    return { amount: "Unavailable", unit: null };
  }
  const measures = item.measures ?? [];
  const reported = measures.filter((measure) => retainedResidual(measure) != null);
  if (reported.length > 1) return { amount: "Multiple", unit: null };
  if (reported.length === 1) return { amount: displayAmount(caseMeasureAmount(reported[0], "variance"), true), unit: measureUnit(reported[0].unit) };
  return measures.length > 0
    ? { amount: "Unavailable", unit: null }
    : { amount: displayAmount(item.varianceExact ?? item.variance, true), unit: "Unit unavailable" };
}

function CaseFact({ label, children }: { label: string; children?: ReactNode }) {
  return <div><dt>{label}</dt><dd>{children ?? "Unavailable"}</dd></div>;
}

export function ReconciliationMatchCandidate({ item }: { item: ReconciliationBreakQueueItem }) {
  const match = item.statementEvidence?.match;
  if (match?.tier !== "Candidate") {
    return item.sourceType === "statement" ? <p className="text-sm text-muted-foreground">No match candidate retained.</p> : null;
  }
  const historical = item.status === "Resolved" || item.status === "Dismissed" || item.lineage?.observationState === "Cleared";
  return <section aria-label="Match candidate" className="grid gap-2">
    <h3 className="text-sm font-semibold">Match candidate</h3>
    <p className="text-sm text-muted-foreground">{historical ? "Retained from the originating run." : "Review required."} {match.explanation}</p>
    <dl className="reconciliation-case-facts">
      {match.confidence !== null ? <CaseFact label="Rule confidence (0–1)">{match.confidence}</CaseFact> : null}
      <CaseFact label="Ledger evidence">{[...new Set(match.ledgerAllocations.map((record) => record.evidenceReference))].join(", ") || null}</CaseFact>
    </dl>
    {match.ruleIds.length > 0 ? <TechnicalDetails label="Candidate rules">
      <ul className="space-y-1 break-all text-xs">{match.ruleIds.map((rule) => <li key={rule}>{rule}</li>)}</ul>
    </TechnicalDetails> : null}
  </section>;
}

function ResidualAmount({ label, unit, value, asOf, basis }: {
  label: string; unit: string; value: string | number | null; asOf?: string | null; basis?: string;
}) {
  const amount = parseExactDecimal(value);
  if (!amount) return "Unavailable";
  const displayValue = displayAmount(value, true);
  return <AmountPassportTrigger compact selection={{
    label, displayValue, unit, currency: /^[A-Z]{3}$/.test(unit) ? unit : null,
    origin: "calculated", zero: amount.units === 0n, asOf, basis,
    unavailableReason: "The reconciliation case retains this value and its evidence. A separate amount revision is unavailable."
  }}><span className="min-w-0 [overflow-wrap:anywhere]">{displayValue}</span></AmountPassportTrigger>;
}

function exactResidualColumns(asOf: string, prior?: RetainedStatementCaseEvidence | null): DenseDataTableColumn<StatementMatchResidual>[] {
  const columns: DenseDataTableColumn<StatementMatchResidual>[] = [
    { id: "measure", label: "Measure", rowHeader: true, render: (row) => <>{row.measure}<span className="block text-xs text-muted-foreground">{row.unit}</span></> },
    { id: "source", label: "Statement", numeric: true, render: (row) => displayAmount(row.sourceTotal) },
    { id: "ledger", label: "Ledger", numeric: true, render: (row) => displayAmount(row.ledgerTotal) },
    { id: "residual", label: "Residual", numeric: true, render: (row) => <>
      <ResidualAmount label={`${row.measure} residual`} unit={row.unit} value={row.residual} asOf={asOf} basis="Statement minus ledger" />
      {row.tolerance !== null ? <span className="block text-xs text-muted-foreground">Tolerance {displayAmount(row.tolerance)}</span> : null}
    </> }
  ];
  if (prior) columns.push({ id: "prior", label: "Prior residual", numeric: true, render: (row) => {
    const previous = prior.match.residuals.find((measure) => measure.measure === row.measure && measure.unit === row.unit);
    const canonical = (value: string) => value.replace(/(\.\d*?)0+$/, "$1").replace(/\.$/, "");
    return <>{displayAmount(previous?.residual ?? null, true)}{previous && canonical(previous.residual) !== canonical(row.residual)
      ? <span className="block text-xs text-muted-foreground">Residual changed</span> : null}</>;
  } });
  return columns;
}

function StatementAllocations({ heading, allocations, amountMeasure }: {
  heading: string; allocations: readonly StatementMatchAllocation[]; amountMeasure: string | null;
}) {
  return <section aria-label={heading} className="grid min-w-0 content-start gap-2">
    <h4 className="text-xs font-semibold">{heading}</h4>
    {allocations.length === 0 ? <p className="text-sm text-muted-foreground">Observation unavailable</p> : <ul className="space-y-2">
      {allocations.map((allocation) => <li key={allocation.recordId} className="min-w-0 break-all rounded-sm border border-border/60 p-2 text-xs">
        <p className="font-mono font-medium">{allocation.evidenceReference}</p>
        {allocations.some((other) => other !== allocation && other.evidenceReference === allocation.evidenceReference) ?
          <p className="text-muted-foreground">{allocation.recordId}</p> : null}
        {allocation.instrument || allocation.recordDate || allocation.transactionType ? <p className="mt-1 break-words text-muted-foreground">
          {[allocation.instrument, allocation.transactionType, allocation.recordDate ? formatAccountingDate(allocation.recordDate) : null,
            allocation.settlementDate ? `Settlement ${formatAccountingDate(allocation.settlementDate)}` : null].filter(Boolean).join(" · ")}
        </p> : null}
        <dl className="mt-1 flex flex-wrap gap-x-3 gap-y-1 tabular-nums">
          {allocation.externalTransactionId ? <CaseFact label="External transaction ID">{allocation.externalTransactionId}</CaseFact> : null}
          {allocation.quantity !== null ? <CaseFact label="Quantity">{displayAmount(allocation.quantity)} units</CaseFact> : null}
          <CaseFact label={amountMeasure ?? "Amount"}>{allocation.amount !== null ? `${displayAmount(allocation.amount)} ${allocation.currency ?? "Currency unavailable"}` : null}</CaseFact>
          {allocation.unitPrice != null ? <CaseFact label="Unit price">{displayAmount(allocation.unitPrice)} {allocation.currency ?? "Currency unavailable"}</CaseFact> : null}
        </dl>
      </li>)}
    </ul>}
  </section>;
}

function StatementMatchComparison({ evidence, prior, grouped = false }: {
  evidence: RetainedStatementCaseEvidence; prior?: RetainedStatementCaseEvidence | null; grouped?: boolean;
}) {
  const { match } = evidence;
  return <>
    <div className="reconciliation-statement-records">
      <StatementAllocations heading={grouped ? "Statement allocations" : "Statement records"} allocations={match.sourceAllocations} amountMeasure={match.amountMeasure} />
      <StatementAllocations heading={grouped ? "Ledger allocations" : "Ledger records"} allocations={match.ledgerAllocations} amountMeasure={match.amountMeasure} />
    </div>
    {match.residuals.length > 0 ? <DenseDataTable columns={exactResidualColumns(evidence.tolerancePolicy.effectiveDate, prior)} rows={match.residuals}
      getRowId={(row) => `${row.measure}:${row.unit}`} ariaLabel={grouped ? "Confirmed group residual components" : "Exact residual components"}
      emptyText="No exact residual components." caption="Residual equals the retained statement total minus the ledger total in the same unit." />
      : <p className="text-sm text-muted-foreground">An exact residual requires comparable statement and ledger observations.</p>}
  </>;
}

function compareColumns(statement: boolean, prior: ReconciliationBreakQueueItem | null, asOf?: string | null): DenseDataTableColumn<ReconciliationBreakMeasure>[] {
  const columns: DenseDataTableColumn<ReconciliationBreakMeasure>[] = [
    { id: "measure", label: "Measure", rowHeader: true, render: (measure) => <>{MEASURE_LABELS[measure.kind]}<span className="block text-xs font-normal text-muted-foreground">{measureUnit(measure.unit)}</span></> },
    { id: "observed", label: statement ? "Statement" : "Observed", numeric: true, render: (measure) => displayAmount(caseMeasureAmount(measure, "actual")) },
    { id: "expected", label: statement ? "Ledger" : "Expected", numeric: true, render: (measure) => displayAmount(caseMeasureAmount(measure, "expected")) },
    { id: "residual", label: "Residual", numeric: true, render: (measure) => <>
      <ResidualAmount label={`${MEASURE_LABELS[measure.kind]} residual`} unit={measureUnit(measure.unit)} value={retainedResidual(measure)}
        asOf={asOf} basis={statement ? "Statement minus ledger" : "Observed minus expected"} />
      {caseMeasureAmount(measure, "tolerance") != null ? <span className="block text-xs font-normal text-muted-foreground">Tolerance {displayAmount(caseMeasureAmount(measure, "tolerance"))}</span> : null}
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
  const baseline = earlier.find((row) => row.breakId === requestedBaseline) ?? null;
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
  const statementEvidence = item.statementEvidence;
  const policy = statementEvidence?.tolerancePolicy;
  const regrouping = lineage?.regrouping;
  const missingContext = [
    !item.fundAccountId && "fund account",
    !item.ledgerBookId && "ledger book",
    !item.accountingPeriodId && "accounting period",
    !item.asOfDate && "as-of date",
    !item.sourceSystem && "source"
  ].filter(Boolean);

  return (
    <div className="reconciliation-case-evidence">
      <p className="text-sm text-foreground">{item.reason}</p>
      {lineage ? <p className="text-sm text-muted-foreground"><strong className="text-foreground">{sourceRunChangeLabel(lineage)}</strong> · Occurrence {lineage.occurrenceNumber} · {sourceObservationAge(lineage)}</p> : null}
      <dl className="reconciliation-case-facts">
        <CaseFact label="Case version">{item.version}</CaseFact>
        <CaseFact label="Case status">{status}</CaseFact>
        <CaseFact label="Owner">{item.assigneeDisplayName ?? item.assignedTo ?? "Unassigned"}</CaseFact>
        <CaseFact label="Severity">{item.severity}</CaseFact>
        <CaseFact label="SLA">{[item.slaBadgeLabel ?? item.slaState, item.slaDueAt && `Due ${formatDateTimeLabel(item.slaDueAt)}`].filter(Boolean).join(" · ") || null}</CaseFact>
      </dl>

      <section aria-labelledby={`${id}-compare`} className="reconciliation-case-section">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h3 id={`${id}-compare`} className="text-sm font-semibold">{statement ? "Statement and ledger" : "Observed and expected"}</h3>
          {(statementEvidence?.match.residuals.length ?? measures.length) > 0 ? <p className="text-xs text-muted-foreground">Residual = {statement ? "statement − ledger" : "observed − expected"}</p> : null}
        </div>
        {statementEvidence ? <StatementMatchComparison evidence={statementEvidence} prior={comparable ? baseline?.statementEvidence : null} /> : measures.length > 0 ? <>
          <DenseDataTable columns={compareColumns(statement, comparable ? baseline ?? null : null, item.asOfDate)} rows={measures}
            getRowId={(measure) => `${measure.kind}:${measure.unit}:${measures.indexOf(measure)}`} ariaLabel="Retained residual components"
            emptyText="No retained residual components."
            caption="The retained residual is the observed amount minus the expected amount in the supplied unit." />
          {unavailable.length > 0 ? <TechnicalDetails label="Unavailable observations">
            <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">{unavailable.map((reason) => <li key={reason}>{reason}</li>)}</ul>
          </TechnicalDetails> : null}
        </> : <>
          <StatusBanner tone="info" role="status" title="Comparison unavailable" detail="This case has no retained pair of observed and expected amounts." />
          <dl className="reconciliation-case-facts"><CaseFact label="Reported variance">
            <ResidualAmount label="Reported variance" unit="Unit unavailable" value={item.variance} asOf={item.asOfDate} />
          </CaseFact></dl>
        </>}
        <dl className="reconciliation-case-facts">
          {item.asOfDate ? <CaseFact label="As of">{formatAccountingDate(item.asOfDate)}</CaseFact> : null}
          {item.sourceSystem ? <CaseFact label="Source">{item.sourceSystem}</CaseFact> : null}
          {ledgerEvidence.length > 0 ? <CaseFact label="Ledger evidence">{ledgerEvidence.length} linked {ledgerEvidence.length === 1 ? "record" : "records"}</CaseFact> : null}
          {policy ? <CaseFact label="Tolerance policy">Version {policy.version}</CaseFact>
            : item.toleranceProfileId ? <CaseFact label="Tolerance policy">Version unavailable</CaseFact> : null}
          {policy ? <CaseFact label="Policy applied as of">{formatAccountingDate(policy.effectiveDate)}</CaseFact> : null}
        </dl>
        {missingContext.length > 0 ? <p className="text-sm text-muted-foreground">Comparison context unavailable: {missingContext.join(", ")}.</p> : null}
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
        {regrouping ? <section className="grid min-w-0 gap-2" aria-label="Confirmed run match group">
          <h4 className="text-sm font-semibold">Confirmed match group</h4>
          <p className="text-sm text-muted-foreground">{regrouping.group.evidence.match.explanation}</p>
          <dl className="reconciliation-case-facts">
            <CaseFact label="Prior run">{regrouping.priorRunId}</CaseFact>
            <CaseFact label="Match run">{regrouping.group.evidence.runId}</CaseFact>
            <CaseFact label="Match tier">{regrouping.group.evidence.match.tier === "Exact" ? "Exact" : "Within tolerance"}</CaseFact>
            <CaseFact label="Group policy version">{regrouping.group.evidence.tolerancePolicy.version}</CaseFact>
          </dl>
          <StatementMatchComparison evidence={regrouping.group.evidence} grouped />
          {regrouping.priorBreakIds.some((caseId) => caseId !== item.breakId) ? <ul aria-label="Related cases from the prior run" className="space-y-1">
            {regrouping.priorBreakIds.filter((caseId) => caseId !== item.breakId).map((caseId) => <li key={caseId} className="break-all text-sm">
              {onOpenCase ? <Button size="sm" variant="outline" className="h-auto whitespace-normal text-left" onClick={() => onOpenCase(caseId)}>Open case {caseId}</Button> : caseId}
            </li>)}
          </ul> : null}
        </section> : null}
        {item.reopenedAt ? <p className="text-sm">Reopened {formatDateTimeLabel(item.reopenedAt)}{item.reopenReason ? ` · ${item.reopenReason}` : null}</p> : null}
        {item.supersedingBreakId ? <p className="break-words text-sm">Superseded by {item.supersedingBreakId}{item.dispositionReason ? ` · ${item.dispositionReason}` : null}</p> : null}
        {onOpenCase && (baseline || successor) ? <div className="flex flex-wrap gap-2">
          {baseline ? <Button size="sm" variant="outline" onClick={() => onOpenCase(baseline.breakId)}>Open earlier case</Button> : null}
          {successor ? <Button size="sm" variant="outline" onClick={() => onOpenCase(successor.breakId)}>Open successor case</Button> : null}
        </div> : null}
        <TechnicalDetails label="Evidence identifiers">
          <dl className="reconciliation-case-facts">
            <CaseFact label="Case ID">{item.breakId}</CaseFact>
            <CaseFact label="Fund account">{item.fundAccountId}</CaseFact>
            <CaseFact label="Ledger book">{item.ledgerBookId}</CaseFact>
            <CaseFact label="Accounting period">{item.accountingPeriodId}</CaseFact>
            <CaseFact label="Source record">{item.sourceReference}</CaseFact>
            <CaseFact label={item.sourceImportId ? "Source import" : "Source run"}>{sourceRun}</CaseFact>
            <CaseFact label="Tolerance policy">{policy ? `${policy.profileId} · Version ${policy.version}`
              : item.toleranceProfileId ? `${item.toleranceProfileId} · Version unavailable` : null}</CaseFact>
            <CaseFact label="Lineage ID">{lineage?.lineageId}</CaseFact>
            <CaseFact label="Occurrence ID">{lineage?.occurrenceId}</CaseFact>
            <CaseFact label="Latest lineage scope">{lineage?.comparisonScopeId}</CaseFact>
            <CaseFact label="Source break ID">{item.sourceBreakId}</CaseFact>
            <CaseFact label="Source fingerprint">{item.sourceFingerprint}</CaseFact>
            {statementEvidence ? <>
              <CaseFact label="Match artifact SHA-256">{statementEvidence.matchArtifactSha256}</CaseFact>
              <CaseFact label="Executed comparison policy fingerprint">{policy?.comparisonPolicyFingerprint}</CaseFact>
            </> : null}
            {regrouping ? <>
              <CaseFact label="Match group ID">{regrouping.group.matchGroupId}</CaseFact>
              <CaseFact label="Match group artifact SHA-256">{regrouping.group.evidence.matchArtifactSha256}</CaseFact>
            </> : null}
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
