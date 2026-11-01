import { type FormEvent, useEffect, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import {
  getContractualCashFlowRun,
  isContractualCashFlowRun,
  previewContractualCashFlows,
  retainContractualCashFlowRun
} from "@/lib/api/contractual-cash-flow.api";
import { bondDecimalText, DECIMAL_LITERAL, utcInstantFromLocalInput } from "@/lib/bond-decimal-format";
import {
  PREPAYMENT_METHODS,
  RATE_SCENARIOS,
  type CashFlowExerciseEligibility,
  type ContractualCashFlowEvent,
  type ContractualCashFlowRequest,
  type ContractualCashFlowResult,
  type ContractualCashFlowRun,
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
  /** Date the notice was (or would be) given; blank means the exercise date itself. */
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
 * knowledge cutoff, horizon, rate shock, exercise selections and one prepayment assumption; the result
 * ladder with blockers; retained runs under a client-generated identity; and a retained-run browser that
 * verifies replay by comparing the retained output hash with a fresh preview of the retained request.
 * Nothing here changes terms or posts anything, and displayed amounts are never resubmitted.
 */
export function ContractualCashFlowWorkbench({ subjectKind, subjectId, subjectLabel }: ContractualCashFlowWorkbenchProps) {
  const [valuationDate, setValuationDate] = useState(todayUtc);
  const [knowledgeCutoff, setKnowledgeCutoff] = useState(nowLocalMinutes);
  const [horizon, setHorizon] = useState("");
  const [rateScenario, setRateScenario] = useState<RateScenario>("Base");
  const [prepay, setPrepay] = useState({ enabled: false, streamId: "", startDate: "", endDate: "", method: "Cpr" as PrepaymentMethod, value: "", policy: "PreservePayments" as RecastPolicy });
  const [exerciseChoices, setExerciseChoices] = useState<Record<string, ExerciseChoice>>({});
  const [eligibility, setEligibility] = useState<CashFlowExerciseEligibility[]>([]);
  const [result, setResult] = useState<ContractualCashFlowResult | null>(null);
  const [run, setRun] = useState<ContractualCashFlowRun | null>(null);
  const [runId, setRunId] = useState<string>(() => newRunId());
  const [runIdInput, setRunIdInput] = useState("");
  const [loadedRun, setLoadedRun] = useState<ContractualCashFlowRun | null>(null);
  const [replayStatus, setReplayStatus] = useState<string | null>(null);
  const [busy, setBusy] = useState<"preview" | "retain" | "load" | "verify" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const inFlight = useRef<AbortController | null>(null);

  useEffect(() => {
    setResult(null);
    setRun(null);
    setLoadedRun(null);
    setReplayStatus(null);
    setEligibility([]);
    setExerciseChoices({});
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
      if (!choice.exerciseDate || !DECIMAL_LITERAL.test(choice.amount.trim()))
        return `Choose an exercise date and a decimal amount for the ${entry.side.toLowerCase()} on ${entry.streamId}.`;
      const noticeDate = choice.noticeDate || choice.exerciseDate;
      if (noticeDate > choice.exerciseDate)
        return `The notice date for the ${entry.side.toLowerCase()} on ${entry.streamId} cannot follow its exercise date.`;
      exercises.push({
        side: entry.side, entryStartDate: entry.entryStartDate, streamId: entry.streamId,
        exerciseDate: choice.exerciseDate, noticeDate, amount: choice.amount.trim(),
        hypotheticalNotice: !choice.noticeEvidenceId.trim(),
        noticeEvidenceId: choice.noticeEvidenceId.trim() || null
      });
    }
    if (prepay.enabled && (!prepay.streamId.trim() || !prepay.startDate || !prepay.endDate || !DECIMAL_LITERAL.test(prepay.value.trim())))
      return "A prepayment assumption needs a stream, a [start, end) interval and a decimal value.";
    return {
      valuationDate,
      knowledgeCutoff: utcInstantFromLocalInput(knowledgeCutoff),
      horizon: horizon || null,
      rateScenario,
      exercises: exercises.length > 0 ? exercises : null,
      prepayments: prepay.enabled
        ? [{ streamId: prepay.streamId.trim(), startDate: prepay.startDate, endDate: prepay.endDate, method: prepay.method, value: prepay.value.trim(), policy: prepay.policy }]
        : null
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
      if (!controller.signal.aborted) setError(describeApiError(failure, fallback).summary);
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
    }, "Cash-flow scenario could not be previewed.");
  }

  async function retain() {
    const request = buildRequest();
    if (typeof request === "string") { setError(request); return; }
    await guard("retain", async (signal) => {
      const next = await retainContractualCashFlowRun(subjectKind, subjectId, { runId, projection: request }, { signal });
      if (signal.aborted) return;
      if (isContractualCashFlowRun(next)) {
        if (next.subjectId.toLowerCase() !== subjectId.toLowerCase()) throw new Error("The retained run names another subject. Refresh the selected record.");
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
    await guard("load", async (signal) => {
      const next = await getContractualCashFlowRun(subjectKind, subjectId, id, { signal });
      if (signal.aborted) return;
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

  const shown = result;
  const canRetain = shown !== null && shown.isCalculated && run === null && busy === null;

  return (
    <section className="grid gap-3 rounded-lg border border-border/70 bg-background/40 p-4 text-xs" aria-label={`${subjectLabel} cash-flow scenarios`}>
      <div>
        <h3 className="text-sm font-semibold text-foreground">Cash-flow scenarios</h3>
        <p className="text-muted-foreground">
          Hypothetical projections over current terms, calendars and pinned curves. Nothing here changes terms or posts;
          retained runs are complete replay packets under a client-generated identity.
        </p>
      </div>

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

        <fieldset className="grid gap-2 md:col-span-4 md:grid-cols-6">
          <legend className="font-medium text-muted-foreground">Prepayment assumption</legend>
          <Checkbox checked={prepay.enabled} label="Apply" aria-label="Apply a prepayment assumption"
            onCheckedChange={(checked) => edit(() => setPrepay((prev) => ({ ...prev, enabled: checked })))} />
          <Input placeholder="Stream id" value={prepay.streamId} disabled={!prepay.enabled} aria-label="Prepayment stream id"
            onChange={(e) => edit(() => setPrepay((prev) => ({ ...prev, streamId: e.target.value })))} />
          <Input type="date" value={prepay.startDate} disabled={!prepay.enabled} aria-label="Prepayment start date"
            onChange={(e) => edit(() => setPrepay((prev) => ({ ...prev, startDate: e.target.value })))} />
          <Input type="date" value={prepay.endDate} disabled={!prepay.enabled} aria-label="Prepayment end date"
            onChange={(e) => edit(() => setPrepay((prev) => ({ ...prev, endDate: e.target.value })))} />
          <Select value={prepay.method} disabled={!prepay.enabled} aria-label="Prepayment method"
            onChange={(e) => edit(() => setPrepay((prev) => ({ ...prev, method: e.target.value as PrepaymentMethod })))}>
            {PREPAYMENT_METHODS.map((method) => <option key={method} value={method}>{method.toUpperCase()}</option>)}
          </Select>
          <div className="flex gap-2">
            <Input placeholder="Value" value={prepay.value} disabled={!prepay.enabled} aria-label="Prepayment value"
              onChange={(e) => edit(() => setPrepay((prev) => ({ ...prev, value: e.target.value })))} />
            <Select value={prepay.policy} disabled={!prepay.enabled} aria-label="Prepayment recast policy"
              onChange={(e) => edit(() => setPrepay((prev) => ({ ...prev, policy: e.target.value as RecastPolicy })))}>
              <option value="PreservePayments">Preserve payments</option>
              <option value="Recast">Recast</option>
            </Select>
          </div>
        </fieldset>

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
                  <Input type="date" value={choice.noticeDate} disabled={!choice.include} aria-label={`Notice date for ${entry.streamId}`}
                    title="Notice date; blank notices on the exercise date" onChange={(e) => update({ noticeDate: e.target.value })} />
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
            <Input value={runIdInput} aria-label="Retained run identity" onChange={(e) => setRunIdInput(e.target.value)} className="min-w-[20rem]" />
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
        {replayStatus ? <p role="status" aria-label="Replay verification">{replayStatus}</p> : null}
      </div>
    </section>
  );
}
