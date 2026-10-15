import { useEffect, useId, useRef, useState, type ChangeEvent } from "react";
import { Link } from "react-router-dom";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import * as api from "@/lib/api/account-oversight.api";
import { describeApiError } from "@/lib/api-errors";
import { evidenceWorkbenchPath } from "@/lib/workspace";
import type {
  AccountCashFlow, AccountCashForecastRequest, AccountCashForecastSnapshot, AccountCashProvenance,
  AccountSettledCashObservation, OversightContext, OversightScope
} from "@/types/account-oversight";

interface AccountCashForecastsPanelProps {
  scope: OversightScope;
  context: OversightContext;
  onDirtyChange?: (dirty: boolean) => void;
}

const money = (amount: string | null | undefined, currency: string) => amount == null ? "Unavailable" : `${amount} ${currency}`;
const date = (value: string | null | undefined) => value || "Missing";
const flowStatus = (flow: AccountCashFlow) => flow.status === 1 ? "Actual receipt" : "Expected";

function failureText(reason: unknown): string {
  const failure = describeApiError(reason, "Cash forecast evidence is unavailable.");
  return [failure.summary, ...failure.details].join(" ");
}

/** Every pending file, request and retained result belongs to exactly one authenticated account/book. */
export function AccountCashForecastsPanel(props: AccountCashForecastsPanelProps) {
  return <ScopedCashForecastsPanel key={`${props.scope.accountId}:${props.scope.ledgerBookId}:${props.scope.baseCurrency}:${props.context.actor}`} {...props} />;
}

function ScopedCashForecastsPanel({ scope, context, onDirtyChange }: AccountCashForecastsPanelProps) {
  const id = useId();
  const { accountId, ledgerBookId, baseCurrency } = scope;
  const [history, setHistory] = useState<AccountCashForecastSnapshot[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [selected, setSelected] = useState<AccountCashForecastSnapshot | null>(null);
  const [pending, setPending] = useState<AccountCashForecastRequest | null>(null);
  const [fileName, setFileName] = useState("");
  const [loading, setLoading] = useState(true);
  const [historyAvailable, setHistoryAvailable] = useState(false);
  const [busy, setBusy] = useState<"import" | "capture" | "detail" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const alive = useRef(true);
  const operation = useRef<AbortController | null>(null);
  const importGeneration = useRef(0);
  const selectedIdRef = useRef("");
  const fileInput = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; importGeneration.current += 1; operation.current?.abort(); };
  }, []);
  useEffect(() => {
    onDirtyChange?.(pending !== null);
    return () => onDirtyChange?.(false);
  }, [pending, onDirtyChange]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setHistoryAvailable(false);
    setError(null);
    api.listCashForecasts({ accountId, ledgerBookId, baseCurrency }, { signal: controller.signal }).then(next => {
      if (controller.signal.aborted) return;
      setHistory(next);
      setHistoryAvailable(true);
      const retained = next.find(item => item.snapshotId === selectedIdRef.current) ?? next.at(-1) ?? null;
      setSelected(retained);
      selectedIdRef.current = retained?.snapshotId ?? "";
      setSelectedId(selectedIdRef.current);
    }).catch(reason => {
      if (!controller.signal.aborted) setError(failureText(reason));
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [accountId, ledgerBookId, baseCurrency, revision]);

  async function inspect(snapshotId: string) {
    if (operation.current || busy || !snapshotId) return;
    const controller = new AbortController();
    operation.current = controller;
    setBusy("detail");
    setError(null);
    try {
      const next = await api.getCashForecast(scope, snapshotId, { signal: controller.signal });
      if (!alive.current || controller.signal.aborted) return;
      setSelected(next);
      selectedIdRef.current = next.snapshotId;
      setSelectedId(next.snapshotId);
    } catch (reason) {
      if (alive.current && !controller.signal.aborted) setError(failureText(reason));
    } finally {
      if (alive.current && !controller.signal.aborted) { operation.current = null; setBusy(null); }
    }
  }

  async function importFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file || busy || operation.current || !context.canWrite) return;
    const generation = ++importGeneration.current;
    setBusy("import"); setError(null); setNotice(null); setPending(null); setFileName("");
    try {
      if (file.size > 1024 * 1024) throw new Error("The handoff file must be at most 1 MiB.");
      const text = await file.text();
      if (!alive.current || generation !== importGeneration.current) return;
      const request = api.parseCashForecastImport(text, scope);
      setPending(request); setFileName(file.name);
      setNotice("Handoff validated for preview. Review the inputs before capturing server evidence.");
    } catch (reason) {
      if (alive.current && generation === importGeneration.current) setError(failureText(reason));
    } finally {
      if (alive.current && generation === importGeneration.current) setBusy(null);
      if (fileInput.current) fileInput.current.value = "";
    }
  }

  // A period's correction must extend its latest retained link, independently of the inspected row.
  const latestForPendingPeriod = pending ? history.filter(item => item.inputs.periodStart === pending.periodStart
    && item.inputs.periodEnd === pending.periodEnd).at(-1) : undefined;
  const correctionError = !pending ? null : latestForPendingPeriod
    ? pending.previousSnapshotId !== latestForPendingPeriod.snapshotId
      ? `Correction must link the latest retained snapshot for this period: ${latestForPendingPeriod.snapshotId}. Update the handoff and import it again.`
      : !pending.correctionRationale?.trim() ? "A linked correction requires a rationale in the handoff." : null
    : pending.previousSnapshotId ? "The correction parent is absent from the authoritative history. Refresh history before capturing." : null;

  async function capture() {
    if (!pending || !context.canWrite || busy || operation.current || loading || !historyAvailable || correctionError || error) return;
    const controller = new AbortController();
    operation.current = controller;
    setBusy("capture"); setError(null); setNotice(null);
    try {
      // Retain the exact imported RunId and recording time on retries. They are immutable identities.
      const next = await api.captureCashForecast(scope, pending, { signal: controller.signal });
      if (!alive.current || controller.signal.aborted) return;
      setHistory(current => [...current.filter(item => item.snapshotId !== next.snapshotId), next]);
      setSelected(next); selectedIdRef.current = next.snapshotId; setSelectedId(next.snapshotId);
      setPending(null); setFileName("");
      setNotice("Cash forecast captured. Earlier predictions and their source revisions remain retained.");
    } catch (reason) {
      if (alive.current && !controller.signal.aborted) setError(failureText(reason));
    } finally {
      if (alive.current && !controller.signal.aborted) { operation.current = null; setBusy(null); }
    }
  }

  return <Card className="panel-surface min-w-0" role="region" aria-label="Account cash forecasts">
    <CardHeader>
      <CardTitle>Account cash and settlement forecast</CardTitle>
      <CardDescription>Reconcile settled opening cash and observed receipts to closing cash. Retained expectations preserve coupon, maturity, principal, unsettled-trade and factor-payment predictions.</CardDescription>
    </CardHeader>
    <CardContent className="space-y-4">
      <p className="text-sm text-muted-foreground">Account {scope.accountId} · Book {context.bookName} · Base {scope.baseCurrency}. Certification reports cash reconciliation. Payment execution uses its separate governed workflow.</p>
      {!context.canWrite ? <StatusBanner tone="info" title={<span className="text-foreground">Read-only cash forecasts</span>} detail="You can inspect retained evidence. Capturing a forecast requires an authenticated session with ledger management permission." /> : null}
      {error ? <StatusBanner tone="danger" role="alert" title={<span className="text-foreground">Cash forecast evidence needs attention</span>} detail={`${error} Retained inputs stay available. Missing or corrupt stores require the documented provisioning or recovery process; retry after service recovery.`} /> : null}
      {notice ? <p className="text-sm" role="status">{notice}</p> : null}
      <div className="flex flex-wrap items-end gap-3">
        <FormRow label="Retained cash forecast" labelFor={`${id}-snapshot`} className="min-w-[16rem] flex-1">
          <Select id={`${id}-snapshot`} value={selectedId} onChange={event => void inspect(event.target.value)} disabled={loading || busy !== null}>
            <option value="">{loading ? "Loading retained forecasts…" : "Select a retained forecast"}</option>
            {history.map(item => <option key={item.snapshotId} value={item.snapshotId}>{item.inputs.periodStart} – {item.inputs.periodEnd} · {item.inputs.runId} · {item.inputs.previousSnapshotId ? "Correction" : "Original"} · {item.isCertified ? "Certified" : "Blocked"}</option>)}
          </Select>
        </FormRow>
        <Button variant="outline" disabled={loading || busy !== null} onClick={() => setRevision(value => value + 1)}>Refresh cash history</Button>
      </div>
      {loading ? <p role="status" className="text-sm">Loading authoritative cash forecast history…</p> : null}
      {!loading && !error && history.length === 0 ? <p role="status" className="text-sm">No cash forecasts retained for this account and book.</p> : null}
      {busy === "detail" ? <p role="status" className="text-sm">Reading the selected immutable forecast…</p> : null}
      {selected ? <ForecastSnapshot snapshot={selected} /> : null}
      {context.canWrite ? <TechnicalDetails label="Capture cash forecast from source handoff" description="Import the supported cash forecast JSON handoff prepared from independently reviewed Evidence Vault source documents. Amounts and FX rates must be decimal strings. The server verifies source bytes, scope and accepted review before retaining any result.">
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-3">
            <Button asChild variant="outline"><a href={`data:application/json;charset=utf-8,${encodeURIComponent(JSON.stringify(handoffTemplate(scope), null, 2))}`} download="account-cash-forecast-handoff-template.json">Download scoped handoff template</a></Button>
            <Link className="text-sm text-primary underline" to={evidenceWorkbenchPath("account", scope.accountId)}>Open source evidence and review</Link>
          </div>
          <p className="text-xs text-muted-foreground">The template contains empty required dates, amounts, settlement status and evidence identities. Populate it from the exact retained source documents. Empty or invented evidence cannot establish certification.</p>
          <FormRow label="Cash forecast handoff file" labelFor={`${id}-file`} hint="Choose a JSON file up to 1 MiB. Importing creates a local structured preview; it does not capture or certify a forecast.">
            <Input id={`${id}-file`} ref={fileInput} type="file" accept="application/json,.json" disabled={busy !== null || loading} onChange={event => void importFile(event)} />
          </FormRow>
          {pending ? <section aria-label="Cash forecast handoff preview" className="space-y-3">
            <h3 className="text-sm font-semibold">Review handoff: {fileName}</h3>
            <p className="text-sm">{pending.periodStart} – {pending.periodEnd} · Run {pending.runId} · Recorded {pending.recordedAtUtc}</p>
            <p className="text-sm text-muted-foreground">Preview has no certification result. Only validated server evidence can reconcile cash.</p>
            <p className="text-sm">{pending.previousSnapshotId ? `Correction of ${pending.previousSnapshotId} · ${pending.correctionRationale || "Rationale missing"}` : "Original forecast for this period"}</p>
            {correctionError ? <StatusBanner role="alert" tone="warning" title={<span className="text-foreground">Correction linkage needs attention</span>} detail={correctionError} /> : null}
            {!historyAvailable ? <p className="text-sm text-warning">Refresh authoritative cash history before capturing. Correction linkage cannot be verified while history is unavailable.</p> : null}
            <CashInputs request={pending} />
            <div className="flex flex-wrap gap-2">
              <Button disabled={busy !== null || loading || !historyAvailable || !!correctionError || !!error} onClick={() => void capture()}>{busy === "capture" ? "Capturing cash forecast…" : "Capture cash forecast"}</Button>
              <Button variant="outline" disabled={busy !== null} onClick={() => { setPending(null); setFileName(""); setNotice(null); setError(null); }}>Discard handoff</Button>
              {error ? <Button variant="outline" disabled={busy !== null || loading} onClick={() => setError(null)}>Keep handoff and retry</Button> : null}
            </div>
          </section> : null}
        </div>
      </TechnicalDetails> : null}
    </CardContent>
  </Card>;
}

function handoffTemplate(scope: OversightScope) {
  const provenance = {
    sourceDomain: "", sourceEntityId: "", sourceContentHash: "", role: 0,
    evidence: {
      evidenceId: "", evidenceUri: "", contentHashSha256: "", sourceSystem: "", sourceReference: "",
      reviewStatus: "", reviewedBy: "", reviewedAtUtc: "", effectiveDate: "", evidenceVersion: 0,
      retainedAtUtc: "", retainedBy: "", subjectType: "", subjectId: ""
    }
  };
  const observation = {
    accountId: scope.accountId, bookId: scope.ledgerBookId, asOfDate: "", amount: "", currency: scope.baseCurrency,
    isSettled: null, provenance
  };
  return {
    runId: "", scope: { tenantId: "", companyId: "", accountId: scope.accountId, bookId: scope.ledgerBookId, baseCurrency: scope.baseCurrency },
    periodStart: "", periodEnd: "", openingCash: observation, closingCash: observation,
    flows: [{
      flowId: "", versionId: "", accountId: scope.accountId, bookId: scope.ledgerBookId, status: 0, flowType: "", amount: "", currency: scope.baseCurrency,
      economicDate: null, settlementDate: null, cashReceiptDate: null, provenance: { ...provenance, role: 1 },
      securityId: null, projectionRunId: null, projectionEngineVersion: null, termsVersionId: null, termsHash: null, supersedesVersionId: null
    }],
    fxConversions: [], recordedAtUtc: "", previousSnapshotId: null, correctionRationale: null, minimumCashThreshold: "", bucketDays: 1
  };
}

function ForecastSnapshot({ snapshot }: { snapshot: AccountCashForecastSnapshot }) {
  const currency = snapshot.inputs.scope.baseCurrency;
  return <section aria-label="Retained cash forecast detail" className="space-y-4">
    <StatusBanner tone={snapshot.isCertified ? "success" : "warning"} title={<span className="text-foreground">{snapshot.isCertified ? "Cash reconciliation certified" : "Cash reconciliation blocked"}</span>} detail={`Retained ${snapshot.inputs.recordedAtUtc}. Financial projections remain subject to their retained source assumptions.`} />
    <FormGrid columns={3}>
      {([
        ["Opening settled cash", snapshot.openingBaseCash], ["Validated actual net receipts", snapshot.actualNetBaseFlow],
        ["Reconciled closing cash", snapshot.reconciledClosingBaseCash], ["Observed closing cash", snapshot.observedClosingBaseCash],
        ["Closing difference", snapshot.closingVariance]
      ] as const).map(([label, amount]) => <div key={label}><p className="text-xs text-muted-foreground">{label}</p><p className="font-mono text-sm tabular-nums">{money(amount, currency)}</p></div>)}
    </FormGrid>
    {snapshot.blockers.length ? <ul className="list-disc space-y-1 pl-5 text-sm" aria-label="Cash certification blockers">{snapshot.blockers.map((issue, index) => <li key={`${issue.code}:${index}`}>{issue.message}{issue.inputId ? ` · Input ${issue.inputId}` : ""} <span className="font-mono text-xs">({issue.code})</span></li>)}</ul> : null}
    {snapshot.inputs.previousSnapshotId ? <p className="text-sm">Linked correction of {snapshot.inputs.previousSnapshotId}: {snapshot.inputs.correctionRationale}</p> : null}
    <CashInputs request={snapshot.inputs} />
    <section aria-label="Expected cash ladder" className="space-y-2">
      <h3 className="text-sm font-semibold">Dated expected cash</h3>
      <p className="text-xs text-muted-foreground">Server-selected expectations and actual receipts, converted with the retained dated declarations. Expected cash does not establish an observed closing balance.</p>
      <DenseDataTable columns={ladderColumns} rows={snapshot.forecastLadder.buckets} getRowId={row => row.bucketStart} ariaLabel="Dated cash forecast buckets" emptyText="No dated forecast buckets retained." />
      {snapshot.forecastLadder.blockingReasons.length ? <ul aria-label="Forecast blockers" className="list-disc pl-5 text-sm">{snapshot.forecastLadder.blockingReasons.map((reason, index) => <li key={index}>{reason}</li>)}</ul> : null}
    </section>
    <TechnicalDetails label="Forecast provenance and earlier predictions">
      <dl className="grid gap-2 break-all text-xs"><div><dt className="font-semibold">Snapshot</dt><dd>{snapshot.snapshotId}</dd></div><div><dt className="font-semibold">Run</dt><dd>{snapshot.inputs.runId}</dd></div><div><dt className="font-semibold">Evidence hash</dt><dd className="font-mono">{snapshot.evidenceHash}</dd></div><div><dt className="font-semibold">Engine</dt><dd>{snapshot.engineVersion}</dd></div></dl>
      <p className="my-3 text-xs text-muted-foreground">Select an earlier retained run to inspect its original predictions. Corrections append a linked version and preserve the earlier inputs.</p>
      {[...snapshot.forecastLadder.assumptions, ...snapshot.forecastLadder.warnings].length ? <ul className="list-disc pl-5 text-xs">{[...snapshot.forecastLadder.assumptions, ...snapshot.forecastLadder.warnings].map((item, index) => <li key={index}>{item}</li>)}</ul> : null}
      <DenseDataTable columns={contributionColumns} rows={snapshot.forecastLadder.contributions} getRowId={row => `${row.securityId}:${row.sourceEntityId}:${row.dueDate}:${row.flowType}`} ariaLabel="Retained cash projection contributions" emptyText="No retained contribution amounts available." />
    </TechnicalDetails>
  </section>;
}

function CashInputs({ request }: { request: AccountCashForecastRequest }) {
  return <div className="space-y-3">
    <FormGrid columns={2}>
      <CashObservation label="Opening settled observation" observation={request.openingCash} />
      <CashObservation label="Closing settled observation" observation={request.closingCash} />
    </FormGrid>
    <DenseDataTable columns={flowColumns} rows={request.flows} getRowId={row => row.versionId} ariaLabel="Expected and actual cash flows" emptyText="No expected or actual cash flows supplied." />
    <TechnicalDetails label={`Declared dated FX (${request.fxConversions.length})`} description="Each rate states base units per one native currency unit for the exact conversion date. An inverse pair or another date cannot substitute.">
      <DenseDataTable columns={fxColumns} rows={request.fxConversions} getRowId={row => `${row.currency}:${row.baseCurrency}:${row.conversionDate}`} ariaLabel="Declared dated FX conversions" emptyText="No FX conversions retained. Foreign currency inputs need an explicit dated declaration before certification." />
    </TechnicalDetails>
  </div>;
}

function CashObservation({ label, observation }: { label: string; observation: AccountSettledCashObservation | null }) {
  return <div className="min-w-0 rounded-[2px] border border-border p-3">
    <h4 className="text-xs font-semibold">{label}</h4>
    {observation ? <><p className="my-1 font-mono text-sm">{money(observation.amount, observation.currency)} · {observation.asOfDate}</p><p className="mb-2 text-xs">{observation.isSettled ? "Settled" : "Unsettled — certification blocked"}</p><Provenance provenance={observation.provenance} /></> : <p className="mt-1 text-sm text-warning">Missing — certification blocked</p>}
  </div>;
}

function Provenance({ provenance }: { provenance: AccountCashProvenance }) {
  const evidence = provenance.evidence;
  return <TechnicalDetails label="Source and accepted review">
    <dl className="space-y-1 break-all text-xs">
      <div><dt className="font-semibold">Source</dt><dd>{provenance.sourceDomain} · {provenance.sourceEntityId}</dd></div>
      <div><dt className="font-semibold">Source content hash</dt><dd className="font-mono">{provenance.sourceContentHash}</dd></div>
      <div><dt className="font-semibold">Role</dt><dd>{["Observation", "Projection", "Declared FX"][provenance.role]}</dd></div>
      <div><dt className="font-semibold">Review</dt><dd>{evidence.reviewStatus} · {evidence.reviewedBy} · {evidence.reviewedAtUtc}</dd></div>
      <div><dt className="font-semibold">Evidence version</dt><dd>{evidence.evidenceVersion} · {evidence.evidenceId}</dd></div>
      <div><dt className="font-semibold">Retained</dt><dd>{evidence.retainedAtUtc} · {evidence.retainedBy}</dd></div>
      <div><dt className="font-semibold">Evidence content hash</dt><dd className="font-mono">{evidence.contentHashSha256}</dd></div>
      <div><dt className="font-semibold">Source reference</dt><dd>{evidence.sourceSystem} · {evidence.sourceReference}</dd></div>
    </dl>
    <Link className="mt-2 inline-block text-xs text-primary underline" to={evidenceWorkbenchPath(evidence.subjectType, evidence.subjectId)}>Inspect evidence and review</Link>
  </TechnicalDetails>;
}

const flowColumns: DenseDataTableColumn<AccountCashFlow>[] = [
  { id: "flow", label: "Flow / status", render: row => <span>{row.flowType}<br /><span className="text-xs text-muted-foreground">{row.flowId} · {flowStatus(row)}</span></span> },
  { id: "amount", label: "Native amount", align: "right", render: row => <span className="font-mono tabular-nums">{money(row.amount, row.currency)}</span> },
  { id: "economic", label: "Economic date", render: row => date(row.economicDate) },
  { id: "settlement", label: "Settlement date", render: row => date(row.settlementDate) },
  { id: "receipt", label: "Cash receipt date", render: row => date(row.cashReceiptDate) },
  { id: "version", label: "Version and evidence", render: row => <TechnicalDetails label={`Version ${row.versionId}`}>
    <p className="mb-2 break-all text-xs">{row.supersedesVersionId ? `Supersedes ${row.supersedesVersionId}` : "Original source revision"}</p>
    <dl className="mb-2 space-y-1 break-all text-xs"><div><dt>Security</dt><dd>{row.securityId || "Not supplied"}</dd></div><div><dt>Projection run / engine</dt><dd>{row.projectionRunId || "Not supplied"} · {row.projectionEngineVersion || "Not supplied"}</dd></div><div><dt>Terms version / hash</dt><dd>{row.termsVersionId || "Not supplied"} · {row.termsHash || "Not supplied"}</dd></div></dl>
    <Provenance provenance={row.provenance} />
  </TechnicalDetails> }
];

const fxColumns: DenseDataTableColumn<AccountCashForecastRequest["fxConversions"][number]>[] = [
  { id: "pair", label: "Native → base", render: row => `${row.currency} → ${row.baseCurrency}` },
  { id: "date", label: "Conversion date", render: row => row.conversionDate },
  { id: "rate", label: "Base units / native unit", align: "right", render: row => <span className="font-mono tabular-nums">{row.baseUnitsPerCurrencyUnit}</span> },
  { id: "source", label: "Declared source", render: row => <Provenance provenance={row.provenance} /> }
];

const ladderColumns: DenseDataTableColumn<AccountCashForecastSnapshot["forecastLadder"]["buckets"][number]>[] = [
  { id: "period", label: "Cash receipt window", render: row => `${row.bucketStart} – ${row.bucketEnd}` },
  { id: "inflows", label: "Base inflows", align: "right", render: row => <span className="font-mono tabular-nums">{row.inflows}</span> },
  { id: "outflows", label: "Base outflows", align: "right", render: row => <span className="font-mono tabular-nums">{row.outflows}</span> },
  { id: "net", label: "Base net flow", align: "right", render: row => <span className="font-mono tabular-nums">{row.netFlow}</span> },
  { id: "balance", label: "Expected closing base cash", align: "right", render: row => <span className="font-mono tabular-nums">{row.cumulativeCash}{row.breachesMinimumBalance ? " · Below retained minimum" : ""}</span> }
];

const contributionColumns: DenseDataTableColumn<AccountCashForecastSnapshot["forecastLadder"]["contributions"][number]>[] = [
  { id: "flow", label: "Contribution", render: row => `${row.displayName} · ${row.flowType} · ${row.sourceLane}` },
  { id: "date", label: "Cash receipt date", render: row => row.dueDate },
  { id: "native", label: "Native amount", align: "right", render: row => <span className="font-mono tabular-nums">{money(row.amount, row.currency)}</span> },
  { id: "base", label: "Converted base amount", align: "right", render: row => <span className="font-mono tabular-nums">{row.baseAmount}</span> },
  { id: "terms", label: "Projection and factor terms", render: row => <span className="break-all text-xs">Run {row.projectionRunId || "Unavailable"}<br />Engine {row.projectionEngineVersion || "Unavailable"}<br />Terms {row.termsVersionId || "Unavailable"}<br />{row.termsHash || "Hash unavailable"}</span> }
];
