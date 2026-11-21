import { type FormEvent, useEffect, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { PreparerDraftCheckPanel } from "@/components/meridian/preparer-draft-check-panel";
import { describeApiError, isApiError } from "@/lib/api-errors";
import {
  compareContractualCashFlowRuns,
  getContractualCashFlowRun,
  isContractualCashFlowRun,
  previewContractualCashFlows,
  retainContractualCashFlowRun
} from "@/lib/api/contractual-cash-flow.api";
import { ExactCurveInputs } from "@/components/meridian/exact-curve-inputs";
import type { ExactCurvePin } from "@/types/security-curves.types";
import { buildScenarioPrepayments, type ScenarioPrepaymentDraft } from "./scenario-prepayment-inputs";
import { bondDecimalText, DECIMAL_LITERAL, utcInstantFromLocalInput } from "@/lib/bond-decimal-format";
import {
  PREPAYMENT_METHODS,
  RATE_SCENARIOS,
  type CashFlowExerciseEligibility,
  type ContractualCashFlowEvent,
  type ContractualCashFlowRequest,
  type ContractualCashFlowResult,
  type ContractualCashFlowRun,
  type ContractualCashFlowRunComparison,
  type ContractualCashFlowSubjectKind,
  type PrepaymentMethod,
  type RateScenario,
  type RecastPolicy,
  type SelectedCashFlowExercise
} from "@/types/contractual-cash-flow.types";

export interface ContractualCashFlowWorkbenchProps {
  subjectKind: ContractualCashFlowSubjectKind;
  subjectId: string;
  subjectLabel: string;
}

interface ExerciseChoice {
  include: boolean;
  exerciseDate: string;
  /** Explicit date the notice was (or would be) given. */
  noticeDate: string;
  amount: string;
  /** Identity of an approved exercise notice; when set the exercise is retained rather than hypothetical. */
  noticeEvidenceId: string;
}

const newRunId = () =>
  typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : `run-${Date.now().toString(36)}`;

const todayUtc = () => new Date().toISOString().slice(0, 10);
const nowLocalMinutes = () => {
  const now = new Date();
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`;
};

const eventColumns: DenseDataTableColumn<ContractualCashFlowEvent>[] = [
  { id: "date", label: "Date", render: (row) => row.date },
  { id: "stream", label: "Stream", render: (row) => row.streamId },
  { id: "opening", label: "Opening balance", align: "right", render: (row) => bondDecimalText(row.openingBalance) },
  { id: "interest", label: "Cash interest", align: "right", render: (row) => bondDecimalText(row.cashInterest) },
  { id: "principal", label: "Scheduled principal", align: "right", render: (row) => bondDecimalText(row.scheduledPrincipal) },
  { id: "prepay", label: "Forecast prepayment", align: "right", render: (row) => bondDecimalText(row.forecastPrepayment) },
  { id: "redeemed", label: "Redeemed principal", align: "right", render: (row) => bondDecimalText(row.redeemedPrincipal) },
  { id: "premium", label: "Premium / penalty", align: "right", render: (row) => `${bondDecimalText(row.redemptionPremium)} / ${bondDecimalText(row.prepaymentPenalty)}` },
  { id: "closing", label: "Closing balance", align: "right", render: (row) => bondDecimalText(row.closingBalance) },
  { id: "direction", label: "Direction", render: (row) => row.direction }
];

/**
 * Cash-flow scenario workbench over the shared contractual cash-flow routes: hypothetical valuation date,
 * knowledge cutoff, horizon, rate shock, explicit notice dates, immutable curve pins and multiple dated prepayment assumptions; the result
 * ladder with blockers; retained runs under a client-generated identity; and a retained-run browser that
 * verifies replay by comparing the retained output hash with a fresh preview of the retained request.
 * Nothing here changes terms or posts anything, and displayed amounts are never resubmitted.
 */
export function ContractualCashFlowWorkbench({ subjectKind, subjectId, subjectLabel }: ContractualCashFlowWorkbenchProps) {
  const [valuationDate, setValuationDate] = useState(todayUtc);
  const [knowledgeCutoff, setKnowledgeCutoff] = useState(nowLocalMinutes);
  const [horizon, setHorizon] = useState("");
  const [rateScenario, setRateScenario] = useState<RateScenario>("Base");
  const [prepayments, setPrepayments] = useState<ScenarioPrepaymentDraft[]>([]);
  const [streams, setStreams] = useState<Array<{ streamId: string; prepaymentAllowed: boolean }>>([]);
  const [curvePins, setCurvePins] = useState<ExactCurvePin[]>([]);
  const [permissionDenied, setPermissionDenied] = useState(false);
  const [exerciseChoices, setExerciseChoices] = useState<Record<string, ExerciseChoice>>({});
  const [eligibility, setEligibility] = useState<CashFlowExerciseEligibility[]>([]);
  const [result, setResult] = useState<ContractualCashFlowResult | null>(null);
  const [run, setRun] = useState<ContractualCashFlowRun | null>(null);
  const [runId, setRunId] = useState<string>(() => newRunId());
  const [runIdInput, setRunIdInput] = useState("");
  const [loadedRun, setLoadedRun] = useState<ContractualCashFlowRun | null>(null);
  const [replayStatus, setReplayStatus] = useState<string | null>(null);
  const [compareRunId, setCompareRunId] = useState("");
  const [comparison, setComparison] = useState<ContractualCashFlowRunComparison | null>(null);
  const [busy, setBusy] = useState<"preview" | "retain" | "load" | "verify" | "compare" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const inFlight = useRef<AbortController | null>(null);

  useEffect(() => {
    inFlight.current?.abort();
    setBusy(null);
    setPrepayments([]);
    setCurvePins([]);
    setPermissionDenied(false);
    setRunIdInput("");
    setResult(null);
    setRun(null);
    setLoadedRun(null);
    setReplayStatus(null);
    setComparison(null);
    setCompareRunId("");
    setEligibility([]);
    setExerciseChoices({});
    setStreams([]);
    setError(null);
    setRunId(newRunId());
    return () => inFlight.current?.abort();
  }, [subjectKind, subjectId]);

  const eligibilityKey = (entry: CashFlowExerciseEligibility) => `${entry.side}:${entry.streamId}:${entry.entryStartDate}`;

  /** Any edit invalidates the previewed result and the run identity bound to the previous request. */
  function edit(change: () => void) {
    inFlight.current?.abort();
    setBusy(null);
    setResult(null);
    setRun(null);
    setError(null);
    setRunId(newRunId());
    change();
  }

  function buildRequest(): ContractualCashFlowRequest | string {
    if (!valuationDate || !knowledgeCutoff) return "Enter a valuation date and a knowledge cutoff.";
    const exercises: SelectedCashFlowExercise[] = [];
    for (const entry of eligibility) {
      const choice = exerciseChoices[eligibilityKey(entry)];
      if (!choice?.include) continue;
      if (!choice.exerciseDate || !choice.noticeDate || !DECIMAL_LITERAL.test(choice.amount.trim()))
        return `Choose an exercise date, a notice date and a decimal amount for the ${entry.side.toLowerCase()} on ${entry.streamId}.`;
      if (choice.noticeDate > choice.exerciseDate)
        return `The notice date for the ${entry.side.toLowerCase()} on ${entry.streamId} cannot follow its exercise date.`;
      const noticeEvidenceId = choice.noticeEvidenceId.trim();
      if (!noticeEvidenceId && choice.noticeDate < valuationDate)
        return "A hypothetical notice date must be on/after valuation. Contractual notice windows are checked by the service.";
      exercises.push({
        side: entry.side, entryStartDate: entry.entryStartDate, streamId: entry.streamId,
        exerciseDate: choice.exerciseDate, noticeDate: choice.noticeDate, amount: choice.amount.trim(),
        hypotheticalNotice: !noticeEvidenceId,
        noticeEvidenceId: noticeEvidenceId || null
      });
    }
    if (horizon && horizon < valuationDate) return "The scenario horizon cannot precede the valuation date.";
    const assumptions = buildScenarioPrepayments(prepayments, valuationDate);
    if (typeof assumptions === "string") return assumptions;
    if (curvePins.some((pin) => !pin.role.trim() || !pin.curveId || !pin.curveVersionId))
      return "Each curve pin needs a benchmark key, a curve and an exact retained version.";
    if (new Set(curvePins.map((pin) => pin.role.trim())).size !== curvePins.length)
      return "Curve benchmark keys must be unique.";
    return {
      valuationDate,
      knowledgeCutoff: utcInstantFromLocalInput(knowledgeCutoff),
      horizon: horizon || null,
      rateScenario,
      exercises: exercises.length > 0 ? exercises : null,
      prepayments: assumptions.length > 0 ? assumptions : null,
      curvePins: curvePins.length > 0 ? curvePins.map((pin) => ({ benchmarkKey: pin.role.trim(), curveId: pin.curveId, curveVersionId: pin.curveVersionId })) : null
    };
  }

  async function guard<T>(action: NonNullable<typeof busy>, work: (signal: AbortSignal) => Promise<T>, fallback: string): Promise<T | undefined> {
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    setBusy(action);
    setError(null);
    try {
      return await work(controller.signal);
    } catch (failure) {
      if (!controller.signal.aborted) {
        if (isApiError(failure) && (failure.status === 401 || failure.status === 403)) {
          if (action === "retain") setPermissionDenied(true);
          setError(`${describeApiError(failure, fallback).summary} ${action === "retain" ? retentionPermission : viewPermission} is required. Refresh after your access changes.`);
        } else if (isApiError(failure) && (failure.status === 404 || failure.status === 503)) {
          setError("The selected record or cash-flow calculation service is unavailable. Check the record and service configuration, then retry.");
        } else setError(describeApiError(failure, fallback).summary);
      }
      return undefined;
    } finally {
      if (!controller.signal.aborted) setBusy(null);
    }
  }

  async function preview(event: FormEvent) {
    event.preventDefault();
    const request = buildRequest();
    if (typeof request === "string") { setError(request); return; }
    setResult(null);
    setRun(null);
    await guard("preview", async (signal) => {
      const next = await previewContractualCashFlows(subjectKind, subjectId, request, { signal });
      if (signal.aborted) return;
      setResult(next);
      setEligibility(next.exerciseEligibility);
      setStreams(next.resolvedContract?.streams ?? [...new Set(next.events.map((event) => event.streamId))].map((streamId) => ({ streamId, prepaymentAllowed: true })));
    }, "Cash-flow scenario could not be previewed.");
  }

  async function retain() {
    if (permissionDenied || !result?.isCalculated || busy !== null) return;
    const request = buildRequest();
    if (typeof request === "string") { setError(request); return; }
    await guard("retain", async (signal) => {
      const next = await retainContractualCashFlowRun(subjectKind, subjectId, { runId, projection: request }, { signal });
      if (signal.aborted) return;
      if (isContractualCashFlowRun(next)) {
        if (next.subjectKind !== subjectKind || next.subjectId.toLowerCase() !== subjectId.toLowerCase()) throw new Error("The retained run names another subject. Refresh the selected record.");
        setRun(next);
        setResult(next.result);
      } else {
        setResult(next);
        setError("The projection became blocked before it could be retained; review the blockers and preview again.");
      }
    }, "Cash-flow run could not be retained.");
  }

  async function loadRun() {
    const id = runIdInput.trim();
    if (!id) { setError("Enter a retained run identity."); return; }
    setLoadedRun(null);
    setReplayStatus(null);
    setComparison(null);
    await guard("load", async (signal) => {
      const next = await getContractualCashFlowRun(subjectKind, subjectId, id, { signal });
      if (signal.aborted) return;
      if (next.subjectKind !== subjectKind || next.subjectId.toLowerCase() !== subjectId.toLowerCase() || next.runId.toLowerCase() !== id.toLowerCase()) throw new Error("The returned replay packet does not match the selected subject and run identity.");
      setLoadedRun(next);
    }, "Retained cash-flow run could not be loaded.");
  }

  async function verifyReplay() {
    if (!loadedRun) return;
    await guard("verify", async (signal) => {
      const fresh = await previewContractualCashFlows(subjectKind, subjectId, loadedRun.request, { signal });
      if (signal.aborted) return;
      const retained = loadedRun.result.evidence;
      setReplayStatus(!fresh.isCalculated
        ? `Replay is blocked now (${fresh.blockers.map((b) => b.code).join(", ")}); the retained packet stands on its own evidence.`
        : fresh.evidence.outputHash === retained.outputHash
          ? `Replay matches: output hash ${retained.outputHash} reproduced from current terms, calendars and curves.`
          : `Replay differs: retained output ${retained.outputHash}, current ${fresh.evidence.outputHash}. Source terms ${retained.sourceTermsHash === fresh.evidence.sourceTermsHash ? "unchanged" : "changed"}.`);
    }, "Replay verification could not run.");
  }

  async function compare() {
    if (!loadedRun) return;
    if (!compareRunId.trim()) { setError("Enter the other retained run identity to compare."); return; }
    setComparison(null);
    await guard("compare", async (signal) => {
      const next = await compareContractualCashFlowRuns(subjectKind, subjectId, loadedRun.runId, compareRunId, { signal });
      if (signal.aborted) return;
      setComparison(next);
    }, "Retained runs could not be compared.");
  }

  const shown = result;
  const retentionPermission = subjectKind === "Security" ? "Modify Security Master permission" : "Manage Direct Lending permission and an authenticated tenant";
  const viewPermission = subjectKind === "Security" ? "View or Modify Security Master permission" : "View or Manage Direct Lending permission";
  const canRetain = shown !== null && shown.isCalculated && run === null && busy === null && !permissionDenied;

  return (
    <section className="grid gap-3 rounded-lg border border-border/70 bg-background/40 p-4 text-xs" aria-label={`${subjectLabel} cash-flow scenarios`}>
      <div>
        <h3 className="text-sm font-semibold text-foreground">Cash-flow scenarios</h3>
        <p className="text-muted-foreground">
          Hypothetical projections over current terms, calendars and pinned curves. Nothing here changes terms or posts;
          retained runs are complete replay packets under a client-generated identity.
        </p>
      </div>

      <p className="text-muted-foreground">Preview and replay require {viewPermission}. Retaining a run requires {retentionPermission}; the server checks access for each request.</p>
      {permissionDenied ? <StatusBanner tone="warning" title="Run retention unavailable" detail={`Your session cannot retain runs. ${retentionPermission} is required.`} /> : null}

      <form onSubmit={(event) => void preview(event)} aria-label={`${subjectLabel} scenario inputs`} className="grid gap-2 md:grid-cols-4">
        <label className="grid gap-1 font-medium text-muted-foreground">Valuation date (UTC)
          <Input type="date" value={valuationDate} aria-label="Scenario valuation date" onChange={(e) => edit(() => setValuationDate(e.target.value))} />
        </label>
        <label className="grid gap-1 font-medium text-muted-foreground">Knowledge cutoff
          <Input type="datetime-local" step={1} value={knowledgeCutoff} aria-label="Scenario knowledge cutoff" onChange={(e) => edit(() => setKnowledgeCutoff(e.target.value))} />
        </label>
        <label className="grid gap-1 font-medium text-muted-foreground">Horizon (optional)
          <Input type="date" value={horizon} aria-label="Scenario horizon" onChange={(e) => edit(() => setHorizon(e.target.value))} />
        </label>
        <label className="grid gap-1 font-medium text-muted-foreground">Rate scenario
          <Select value={rateScenario} aria-label="Scenario rate shock" onChange={(e) => edit(() => setRateScenario(e.target.value as RateScenario))}>
            {RATE_SCENARIOS.map((scenario) => <option key={scenario} value={scenario}>{scenario}</option>)}
          </Select>
        </label>

        <fieldset className="grid gap-3 md:col-span-4">
          <legend className="font-medium text-muted-foreground">Prepayment assumptions</legend>
          <p className="text-muted-foreground">Intervals include the start and exclude the end. Intervals on one stream must not overlap.
            CPR/SMM use fractions (0.06 = 6%); PSA uses multiples (1 = 100% PSA). Amount is cash on the start date.
            Preview first to discover contractual streams; the service checks whether prepayment is supported.</p>
          {prepayments.map((assumption, index) => {
            const update = (next: Partial<ScenarioPrepaymentDraft>) => edit(() => setPrepayments((previous) => previous.map((item) => item.id === assumption.id ? { ...item, ...next } : item)));
            return (
              <fieldset key={assumption.id} className="grid gap-2 rounded-md border border-border/60 p-3 md:grid-cols-4">
                <legend className="px-1 font-medium">Prepayment {index + 1}</legend>
                <label className="grid gap-1">Contractual stream
                  <Select value={assumption.streamId} aria-label={`Prepayment ${index + 1} stream`} onChange={(event) => update({ streamId: event.target.value })}>
                    <option value="">Choose a stream</option>
                    {streams.map((stream) => <option key={stream.streamId} value={stream.streamId} disabled={!stream.prepaymentAllowed}>{stream.streamId}{!stream.prepaymentAllowed ? " · prepayment unavailable" : ""}</option>)}
                  </Select>
                </label>
                <label className="grid gap-1">Start (inclusive)
                  <Input type="date" value={assumption.startDate} aria-label={`Prepayment ${index + 1} start date`} onChange={(event) => update({ startDate: event.target.value })} />
                </label>
                <label className="grid gap-1">End (exclusive)
                  <Input type="date" value={assumption.endDate} aria-label={`Prepayment ${index + 1} end date`} onChange={(event) => update({ endDate: event.target.value })} />
                </label>
                <label className="grid gap-1">Method
                  <Select value={assumption.method} aria-label={`Prepayment ${index + 1} method`} onChange={(event) => update({ method: event.target.value as PrepaymentMethod })}>
                    {PREPAYMENT_METHODS.map((method) => <option key={method} value={method}>{method.toUpperCase()}</option>)}
                  </Select>
                </label>
                <label className="grid gap-1">Value ({assumption.method === "Psa" ? "multiple" : assumption.method === "Amount" ? "cash amount" : "fraction"})
                  <Input value={assumption.value} aria-label={`Prepayment ${index + 1} value`} onChange={(event) => update({ value: event.target.value })} />
                </label>
                {assumption.method === "Psa" ? (
                  <label className="grid gap-1">Seasoning origin
                    <Input type="date" value={assumption.seasoningOrigin} aria-label={`Prepayment ${index + 1} seasoning origin`} onChange={(event) => update({ seasoningOrigin: event.target.value })} />
                  </label>
                ) : null}
                <label className="grid gap-1">Recast policy
                  <Select value={assumption.policy} aria-label={`Prepayment ${index + 1} recast policy`} onChange={(event) => update({ policy: event.target.value as RecastPolicy })}>
                    <option value="PreservePayments">Preserve payments</option>
                    <option value="Recast">Recast</option>
                  </Select>
                </label>
                <Button type="button" size="sm" variant="outline" className="self-end" aria-label={`Remove prepayment ${index + 1}`} onClick={() => edit(() => setPrepayments((previous) => previous.filter((item) => item.id !== assumption.id)))}>Remove assumption</Button>
              </fieldset>
            );
          })}
          {streams.length > 0 && !streams.some((stream) => stream.prepaymentAllowed) ? <p className="text-muted-foreground" role="status">The resolved contractual streams do not support prepayments.</p> : null}
          <Button type="button" size="sm" variant="outline" className="justify-self-start" disabled={!streams.some((stream) => stream.prepaymentAllowed)}
            onClick={() => edit(() => setPrepayments((previous) => [...previous, { id: newRunId(), streamId: "", startDate: valuationDate, endDate: "", method: "Cpr", value: "", seasoningOrigin: "", policy: "PreservePayments" }]))}>Add prepayment assumption</Button>
        </fieldset>
        <div className="md:col-span-4">
          <p className="mb-2 text-muted-foreground">Some scenarios require benchmark pins. Use the benchmark key declared by the contract (for example SOFR); missing economic inputs appear as calculation blockers.</p>
          <ExactCurveInputs roleLabel="Benchmark key" pins={curvePins} onChange={(pins) => edit(() => setCurvePins(pins))} />
        </div>

        {eligibility.length > 0 ? (
          <fieldset className="grid gap-2 md:col-span-4">
            <legend className="font-medium text-muted-foreground">Exercise selections (hypothetical unless an approved notice is named)</legend>
            {eligibility.map((entry) => {
              const key = eligibilityKey(entry);
              const choice = exerciseChoices[key] ?? { include: false, exerciseDate: entry.dates[0] ?? "", noticeDate: "", amount: "", noticeEvidenceId: "" };
              const update = (next: Partial<ExerciseChoice>) => edit(() => setExerciseChoices((prev) => ({ ...prev, [key]: { ...choice, ...next } })));
              return (
                <div key={key} className="grid items-center gap-2 md:grid-cols-6">
                  <Checkbox checked={choice.include} label={`${entry.side} · ${entry.streamId} · ${entry.style} until ${entry.windowEnd}`}
                    aria-label={`Include ${entry.side.toLowerCase()} exercise on ${entry.streamId}`} onCheckedChange={(checked) => update({ include: checked })} />
                  {entry.dates.length > 0 ? (
                    <Select value={choice.exerciseDate} disabled={!choice.include} aria-label={`Exercise date for ${entry.streamId}`}
                      onChange={(e) => update({ exerciseDate: e.target.value })}>
                      {entry.dates.map((date) => <option key={date} value={date}>{date}</option>)}
                    </Select>
                  ) : (
                    <Input type="date" value={choice.exerciseDate} disabled={!choice.include} aria-label={`Exercise date for ${entry.streamId}`}
                      onChange={(e) => update({ exerciseDate: e.target.value })} />
                  )}
                  <label className="grid gap-1">Notice date
                    <Input type="date" value={choice.noticeDate} disabled={!choice.include} aria-label={`Notice date for ${entry.streamId}`}
                      onChange={(e) => update({ noticeDate: e.target.value })} />
                  </label>
                  <Input placeholder="Amount" value={choice.amount} disabled={!choice.include} aria-label={`Exercise amount for ${entry.streamId}`}
                    onChange={(e) => update({ amount: e.target.value })} />
                  <Input placeholder="Approved notice id (optional)" value={choice.noticeEvidenceId} disabled={!choice.include} aria-label={`Notice evidence for ${entry.streamId}`}
                    onChange={(e) => update({ noticeEvidenceId: e.target.value })} />
                  <span className="text-muted-foreground">{entry.businessDayCalendarId ? `Notice calendar ${entry.businessDayCalendarId}` : "No notice calendar declared"}</span>
                </div>
              );
            })}
          </fieldset>
        ) : null}

        <div className="flex flex-wrap gap-2 md:col-span-4">
          <Button type="submit" size="sm" disabled={busy !== null} aria-busy={busy === "preview"}>{busy === "preview" ? "Previewing…" : "Preview scenario"}</Button>
          <Button type="button" size="sm" variant="outline" disabled={!canRetain} aria-busy={busy === "retain"} onClick={() => void retain()}>
            {busy === "retain" ? "Retaining…" : "Retain run"}
          </Button>
        </div>
      </form>

      {error ? <StatusBanner tone="danger" role="alert" title="Scenario request refused" detail={error} /> : null}

      {shown ? (
        <div className="grid gap-2">
          {shown.blockers.length > 0 ? (
            <ul className="grid gap-1 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2" aria-label="Calculation blockers">
              {shown.blockers.map((blocker) => (
                <li key={`${blocker.code}:${blocker.streamId ?? ""}:${blocker.message}`}><span className="font-mono">{blocker.code}</span>{blocker.streamId ? ` (${blocker.streamId})` : ""}: {blocker.message}</li>
              ))}
            </ul>
          ) : null}
          <DenseDataTable rows={shown.events} getRowId={(row) => row.eventId} columns={eventColumns}
            ariaLabel={`${subjectLabel} projected cash flows`} caption={null} emptyText="No cash-flow events in this scenario." />
          {shown.exerciseCalculations.length > 0 ? (
            <ul className="grid gap-1" aria-label="Exercise calculations">
              {shown.exerciseCalculations.map((calc) => (
                <li key={`${calc.selection.streamId}:${calc.selection.exerciseDate}`} className="font-mono">
                  {calc.selection.side} {calc.selection.streamId} on {calc.selection.exerciseDate}: dirty consideration {bondDecimalText(calc.dirtyConsideration)} · premium {bondDecimalText(calc.premium)} · accrued {bondDecimalText(calc.accruedInterest)} · clean floor {bondDecimalText(calc.cleanFloor)}
                  {calc.noticeWindow.earliest || calc.noticeWindow.latest ? ` · notice ${calc.noticeWindow.earliest ?? "?"} to ${calc.noticeWindow.latest ?? "?"}` : ""}
                </li>
              ))}
            </ul>
          ) : null}
          <p className="break-all font-mono text-muted-foreground" aria-label="Scenario evidence">
            {shown.evidence.engineVersion} · input {shown.evidence.inputHash} · output {shown.evidence.outputHash} · source terms {shown.evidence.sourceTermsHash} (v{shown.evidence.sourceVersion})
            {shown.evidence.warnings.length > 0 ? ` · warnings: ${shown.evidence.warnings.join("; ")}` : ""}
          </p>
          {run ? (
            <p className="break-all font-mono" aria-label="Retained run">
              Run {run.runId} retained {run.recordedAt} by {run.actor}. Replay needs no live terms, calendars or curves.
            </p>
          ) : null}
        </div>
      ) : null}

      <div className="grid gap-2 border-t border-border/60 pt-3">
        <div className="flex flex-wrap items-end gap-2">
          <label className="grid gap-1 font-medium text-muted-foreground">Retained run identity
            <Input value={runIdInput} aria-label="Retained run identity" onChange={(e) => { inFlight.current?.abort(); setBusy(null); setLoadedRun(null); setReplayStatus(null); setComparison(null); setCompareRunId(""); setRunIdInput(e.target.value); }} className="min-w-[20rem]" />
          </label>
          <Button type="button" size="sm" variant="outline" disabled={busy !== null} aria-busy={busy === "load"} onClick={() => void loadRun()}>
            {busy === "load" ? "Loading…" : "Load retained run"}
          </Button>
          <Button type="button" size="sm" variant="outline" disabled={!loadedRun || busy !== null} aria-busy={busy === "verify"} onClick={() => void verifyReplay()}>
            {busy === "verify" ? "Verifying…" : "Verify replay"}
          </Button>
        </div>
        {loadedRun ? (
          <p className="break-all font-mono" aria-label="Loaded retained run">
            Run {loadedRun.runId} · {loadedRun.subjectKind} · recorded {loadedRun.recordedAt} by {loadedRun.actor} · valuation {loadedRun.request.valuationDate} · {loadedRun.request.rateScenario ?? "Base"} · {loadedRun.result.events.length} events · output {loadedRun.result.evidence.outputHash}
          </p>
        ) : null}
        {loadedRun ? <details className="rounded border border-border/60 p-2"><summary>Retained calculation inputs</summary><pre className="overflow-auto whitespace-pre-wrap break-all">{JSON.stringify(loadedRun.request, null, 2)}</pre></details> : null}
        {replayStatus ? <p role="status" aria-label="Replay verification">{replayStatus}</p> : null}
        {loadedRun ? (
          <div className="flex flex-wrap items-end gap-2">
            <label className="grid gap-1 font-medium text-muted-foreground">Compare with retained run
              <Input value={compareRunId} aria-label="Comparison run identity" onChange={(e) => { inFlight.current?.abort(); setBusy(null); setComparison(null); setCompareRunId(e.target.value); }} className="min-w-[20rem]" />
            </label>
            <Button type="button" size="sm" variant="outline" disabled={busy !== null} aria-busy={busy === "compare"} onClick={() => void compare()}>
              {busy === "compare" ? "Comparing…" : "Compare runs"}
            </Button>
          </div>
        ) : null}
        {comparison ? (
          <div className="grid gap-2" aria-label="Retained run comparison">
            <p className="break-all font-mono text-muted-foreground">
              {comparison.leftRunId} ({comparison.leftReplayVerified ? "replay verified" : "REPLAY FAILED"}) vs {comparison.rightRunId} ({comparison.rightReplayVerified ? "replay verified" : "REPLAY FAILED"}) · {comparison.sameRequest ? "same request" : "different requests"} · total net cash delta {bondDecimalText(comparison.totalDelta)}
            </p>
            {comparison.blockerDeltas.length > 0 ? (
              <ul className="grid gap-1" aria-label="Blocker differences">
                {comparison.blockerDeltas.map((delta) => <li key={`${delta.side}:${delta.code}:${delta.streamId ?? ""}`}>{delta.side === "LeftOnly" ? "Only in first" : "Only in second"}: <span className="font-mono">{delta.code}</span>{delta.streamId ? ` (${delta.streamId})` : ""}: {delta.message}</li>)}
              </ul>
            ) : null}
            <DenseDataTable rows={comparison.cashDeltas} getRowId={(row) => `${row.date}:${row.streamId}`} ariaLabel="Net cash deltas by date" caption={null}
              emptyText="No net cash differences between the two retained runs."
              columns={[
                { id: "date", label: "Date", render: (row) => row.date },
                { id: "stream", label: "Stream", render: (row) => row.streamId },
                { id: "left", label: "First run", align: "right", render: (row) => bondDecimalText(row.leftNetCash) },
                { id: "right", label: "Second run", align: "right", render: (row) => bondDecimalText(row.rightNetCash) },
                { id: "delta", label: "Delta", align: "right", render: (row) => bondDecimalText(row.delta) }
              ]} />
          </div>
        ) : null}
      </div>
      {subjectKind === "Security" && (loadedRun ?? run)?.subjectKind === "Security" && (loadedRun ?? run)?.result.isPostable === false ? (
        <PreparerDraftCheckPanel securityId={(loadedRun ?? run)!.subjectId} runId={(loadedRun ?? run)!.runId} />
      ) : null}
    </section>
  );
}
