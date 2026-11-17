import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { PanelReload } from "@/components/ui/panel-reload";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { describeApiError } from "@/lib/api-errors";
import { entityOperationEndpoints as endpoints, getEntityOperation } from "@/lib/api/entity-operations.api";
import { formatTimestamp } from "@/lib/format";
import type { EntityOperationScope, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import type {
  IntercompanyAccrualSchedule, IntercompanyAccrualScheduleDecision, IntercompanyAccrualScheduleRun, IntercompanyAccrualScheduleWindow
} from "@/types/intercompany-accrual-schedules";
import { EntityOperationTable, MutationFeedback, nullable, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

type ProfileLabels = Map<string, IntercompanyAccountingArrangement>;

const uniqueProfiles = (rows: IntercompanyAccountingArrangement[]) => [...new Map(rows.map((row) => [row.arrangementId, row])).values()];
const profileLabel = (labels: ProfileLabels, id?: string | null) => {
  if (!id) return "None";
  const profile = labels.get(id);
  return profile ? `${profile.arrangementReference} · ${id}` : id;
};

/**
 * Opt-in, reviewed schedules that draft one book's governed loan-interest accruals for completed schedule
 * periods. A schedule only drafts: every draft enters ordinary journal review and nothing posts. One person
 * proposes; a different person approves. Active schedules run in the background and on demand.
 */
export function EntityAccrualSchedules({ scope }: { scope: EntityOperationScope }) {
  const bookId = scope.book.ledgerBookId;
  const canManage = scope.capabilities.canManageLedger;
  const schedules = useEntityData<IntercompanyAccrualSchedule[]>(endpoints.accrualScheduleList(bookId));
  // Approved profile revisions that map this book, resolved by the server for today.
  const profiles = useEntityData<IntercompanyAccountingArrangement[]>(canManage
    ? `${endpoints.accountingArrangements}?${new URLSearchParams({ bookId, asOf: today() })}` : null);
  // Retain the last confirmed list across a refresh so an open schedule keeps its feedback.
  const [retained, setRetained] = useState<{ bookId: string; rows: IntercompanyAccrualSchedule[] } | null>(null);
  if (schedules.data && (retained?.bookId !== bookId || retained.rows !== schedules.data)) setRetained({ bookId, rows: schedules.data });
  const rows = schedules.data ?? (retained?.bookId === bookId ? retained.rows : null);
  const [selectedId, setSelectedId] = useState("");
  const selected = rows?.find((row) => row.scheduleId === selectedId) ?? null;
  const labels: ProfileLabels = new Map(uniqueProfiles(profiles.data ?? []).map((row) => [row.arrangementId, row]));

  function retain(next: IntercompanyAccrualSchedule) {
    const current = rows ?? [];
    setRetained({ bookId, rows: current.some((row) => row.scheduleId === next.scheduleId)
      ? current.map((row) => row.scheduleId === next.scheduleId ? next : row) : [...current, next] });
    schedules.refresh();
  }

  return <div className="space-y-4">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h3 className="font-semibold">Intercompany accrual schedules</h3>
      <PanelReload reload={schedules.refresh} failed={Boolean(schedules.error)} busy={schedules.loading} what="accrual schedules" />
    </div>
    <div className="space-y-1 text-sm text-muted-foreground">
      <p>A schedule only drafts. For {scope.book.displayName} it drafts the governed loan-interest accrual of each completed schedule period that ends on or after the date the schedule was approved; earlier periods are never drafted.</p>
      <p>Every draft still needs its own journal review, and nothing posts. A period the server refuses is retried on the next run. Active schedules run every 6 hours, or on demand with Run now.</p>
      <p>The person who proposes a schedule cannot approve it; a different operator with ledger management permission must approve.</p>
    </div>
    <OperationError error={schedules.error} />
    {schedules.loading && !rows ? <p role="status">Loading accrual schedules…</p> : null}
    {rows ? <EntityOperationTable label="Accrual schedules for this book" rows={rows} getRowId={(row) => row.scheduleId} emptyText="No accrual schedules for this book."
      columns={[
        { id: "profile", label: "Loan interest profile", rowHeader: true, render: (row) => <Button type="button" variant="outline" size="sm" aria-pressed={selectedId === row.scheduleId}
          onClick={() => setSelectedId(row.scheduleId)}>{profileLabel(labels, row.profileArrangementId)}</Button> },
        { id: "principal", label: "Loan principal profile", render: (row) => profileLabel(labels, row.principalArrangementId) },
        { id: "status", label: "Status", render: (row) => <Badge variant="outline">{row.status}</Badge> },
        { id: "version", label: "Version", numeric: true, render: (row) => row.version },
        { id: "activeFrom", label: "Active from", render: (row) => row.activeFrom ?? "Not approved" }
      ]} /> : null}
    {selected ? <AccrualScheduleDetail key={selected.scheduleId} scope={scope} schedule={selected} labels={labels} onChanged={retain} /> : null}
    {canManage ? <ProposeAccrualSchedule scope={scope} profiles={profiles} onProposed={(result) => { retain(result); setSelectedId(result.scheduleId); }} />
      : <p className="text-sm text-muted-foreground">Ledger management permission is required to propose, approve, reject, disable or run an accrual schedule. Schedules, their history and due periods remain visible.</p>}
  </div>;
}

function ProposeAccrualSchedule({ scope, profiles, onProposed }: {
  scope: EntityOperationScope; profiles: ReturnType<typeof useEntityData<IntercompanyAccountingArrangement[]>>; onProposed: (schedule: IntercompanyAccrualSchedule) => void;
}) {
  const all = uniqueProfiles(profiles.data ?? []);
  const interestProfiles = all.filter((row) => row.templateKind === "LoanInterest");
  const [interestSelection, setInterestSelection] = useState("");
  const interest = interestProfiles.find((row) => row.arrangementId === interestSelection) ?? interestProfiles[0] ?? null;
  const principalProfiles = interest ? all.filter((row) => row.templateKind === "Loan" && row.canonicalAgreementId === interest.canonicalAgreementId) : [];
  const [principalSelection, setPrincipalSelection] = useState("");
  const principalId = principalProfiles.some((row) => row.arrangementId === principalSelection) ? principalSelection : principalProfiles[0]?.arrangementId ?? "";
  const [reason, setReason] = useState("");
  const mutation = useEntityMutation();

  async function propose(event: FormEvent) {
    event.preventDefault();
    if (mutation.busy || !interest || !principalId || !reason.trim()) return;
    const command = { ledgerBookId: scope.book.ledgerBookId, kind: "LoanInterest" as const, profileArrangementId: interest.arrangementId,
      principalArrangementId: principalId, reason: reason.trim() };
    const result = await mutation.run<IntercompanyAccrualSchedule>(endpoints.accrualSchedules, { requestId: mutation.identity(command), ...command },
      "Accrual schedule proposed. It drafts nothing until a different operator approves it.");
    if (result) { setReason(""); onProposed(result); }
  }

  return <section className="space-y-3 rounded border border-border p-3" aria-label="Propose an accrual schedule">
    <h4 className="text-sm font-semibold">Propose an accrual schedule</h4>
    <OperationError error={profiles.error} />
    {profiles.loading ? <p role="status">Loading approved loan profiles for this book…</p> : null}
    {profiles.data && interestProfiles.length === 0 ? <p role="note" className="text-xs text-warning">No approved loan-interest profile maps {scope.book.displayName}. Approve a LoanInterest accounting profile for this book first.</p> : null}
    {interest && principalProfiles.length === 0 ? <p role="note" className="text-xs text-warning">No approved loan principal profile of the same legal agreement maps this book. Principal is read only from posted journals of a reviewed Loan profile.</p> : null}
    <form className="space-y-3" onSubmit={(event) => void propose(event)}><fieldset className="space-y-3" disabled={mutation.busy}>
      <FormGrid columns={3}>
        <OperationField label="Schedule loan interest profile" value={interest?.arrangementId ?? ""} onChange={setInterestSelection} required>
          {interestProfiles.length === 0 ? <option value="">No approved loan interest profile</option> : null}
          {interestProfiles.map((row) => <option key={row.arrangementId} value={row.arrangementId}>{row.arrangementReference} · {row.status}</option>)}
        </OperationField>
        <OperationField label="Schedule loan principal profile" value={principalId} onChange={setPrincipalSelection} required>
          {principalProfiles.length === 0 ? <option value="">No loan principal profile</option> : null}
          {principalProfiles.map((row) => <option key={row.arrangementId} value={row.arrangementId}>{row.arrangementReference} · {row.status}</option>)}
        </OperationField>
        <OperationField label="Schedule proposal reason" value={reason} onChange={setReason} required />
      </FormGrid>
      <Button type="submit" disabled={!interest || !principalId || !reason.trim()}
        disabledReason={!interest ? "Select an approved loan interest profile." : !principalId ? "Select the loan principal profile." : "Enter a reason for this schedule."}>Propose accrual schedule</Button>
    </fieldset></form>
    <MutationFeedback state={mutation} />
  </section>;
}

function AccrualScheduleDetail({ scope, schedule, labels, onChanged }: {
  scope: EntityOperationScope; schedule: IntercompanyAccrualSchedule; labels: ProfileLabels; onChanged: (schedule: IntercompanyAccrualSchedule) => void;
}) {
  const bookId = scope.book.ledgerBookId;
  const canManage = scope.capabilities.canManageLedger;
  const [reason, setReason] = useState("");
  const [due, setDue] = useState<{ version: number; run: IntercompanyAccrualScheduleRun } | null>(null);
  const [dueLoading, setDueLoading] = useState(false);
  const [dueError, setDueError] = useState<string | null>(null);
  const [ran, setRan] = useState<IntercompanyAccrualScheduleRun | null>(null);
  const active = useRef<AbortController | null>(null);
  const mutation = useEntityMutation();
  useEffect(() => () => active.current?.abort(), []);
  const proposer = schedule.events.find((event) => event.action === "Propose")?.actor;
  const currentDue = due?.version === schedule.version ? due.run : null;

  async function decide(action: IntercompanyAccrualScheduleDecision) {
    if (mutation.busy || (action !== "Approve" && !reason.trim())) return;
    const command = { scheduleId: schedule.scheduleId, action, expectedVersion: schedule.version, reason: nullable(reason) };
    const result = await mutation.run<IntercompanyAccrualSchedule>(endpoints.accrualScheduleDecisions(schedule.scheduleId, bookId),
      { requestId: mutation.identity(command), action, expectedVersion: command.expectedVersion, reason: command.reason },
      action === "Approve" ? "Accrual schedule approved. It drafts completed periods from its approval date; every draft still needs its own review."
        : action === "Reject" ? "Accrual schedule rejected. It will not draft." : "Accrual schedule disabled. Drafts already created are unchanged.");
    if (result) { setReason(""); setRan(null); onChanged(result); }
  }

  async function previewDue() {
    if (dueLoading) return;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setDueLoading(true); setDueError(null);
    try {
      const run = await getEntityOperation<IntercompanyAccrualScheduleRun>(endpoints.accrualScheduleDue(schedule.scheduleId, bookId, today()), { signal: controller.signal });
      if (!controller.signal.aborted) setDue({ version: schedule.version, run });
    } catch (failure) {
      if (!controller.signal.aborted) {
        const display = describeApiError(failure, "Due periods could not be evaluated.");
        setDue(null); setDueError([...new Set([display.summary, ...display.details])].join("\n"));
      }
    } finally {
      if (!controller.signal.aborted) setDueLoading(false);
    }
  }

  async function runNow() {
    if (mutation.busy || schedule.status !== "Active") return;
    const result = await mutation.run<IntercompanyAccrualScheduleRun>(endpoints.accrualScheduleRun(schedule.scheduleId, bookId), undefined,
      "Schedule run finished. Drafts await their own journal review; nothing was posted.");
    if (result) { setRan(result); setDue(null); }
  }

  return <section className="space-y-3 rounded border border-border p-3" aria-label="Selected accrual schedule">
    <h4 className="text-sm font-semibold">{profileLabel(labels, schedule.profileArrangementId)} · {schedule.status} · version {schedule.version}</h4>
    <p className="text-xs text-muted-foreground">{schedule.activeFrom ? `Drafts completed periods ending on or after ${schedule.activeFrom}.` : "Not approved; this schedule drafts nothing."}{proposer ? ` Proposed by ${proposer}.` : ""}</p>
    {canManage && (schedule.status === "Proposed" || schedule.status === "Active") ? <div className="space-y-3">
      <FormGrid columns={2}>
        <OperationField label="Schedule decision reason" value={reason} onChange={setReason} hint={schedule.status === "Proposed" ? "Required to reject; optional to approve." : "Required to disable."} />
      </FormGrid>
      <div className="flex flex-wrap gap-2">
        {schedule.status === "Proposed" ? <>
          <Button type="button" variant="outline" disabled={mutation.busy} disabledReason="Wait for the current schedule change to finish." onClick={() => void decide("Approve")}>Approve schedule</Button>
          <Button type="button" variant="outline" disabled={mutation.busy || !reason.trim()} disabledReason={mutation.busy ? "Wait for the current schedule change to finish." : "Enter a reason to reject this schedule."} onClick={() => void decide("Reject")}>Reject schedule</Button>
        </> : null}
        {schedule.status === "Active" ? <>
          <Button type="button" disabled={mutation.busy} disabledReason="Wait for the current schedule change to finish." onClick={() => void runNow()}>Run now</Button>
          <Button type="button" variant="outline" disabled={mutation.busy || !reason.trim()} disabledReason={mutation.busy ? "Wait for the current schedule change to finish." : "Enter a reason to disable this schedule."} onClick={() => void decide("Disable")}>Disable schedule</Button>
        </> : null}
      </div>
      {schedule.status === "Proposed" ? <p className="text-xs text-muted-foreground">The proposer cannot approve this schedule; the server refuses a self-approval.</p> : null}
      <MutationFeedback state={mutation} />
    </div> : null}
    <Button type="button" variant="outline" disabled={dueLoading} disabledReason="Due periods are being evaluated." onClick={() => void previewDue()}>Preview due periods</Button>
    <OperationError error={dueError} />
    {dueLoading ? <p role="status">Evaluating due periods…</p> : null}
    {currentDue ? <ScheduleWindows label="Due periods preview" run={currentDue} bookId={bookId} /> : null}
    {ran ? <ScheduleWindows label="Schedule run result" run={ran} bookId={bookId} /> : null}
    <TechnicalDetails label="Accrual schedule history"><dl className="space-y-2 break-all text-xs">
      <div><dt>Schedule</dt><dd className="font-mono">{schedule.scheduleId}</dd></div>
      <div><dt>Kind</dt><dd>{schedule.kind}</dd></div>
      <div><dt>Loan interest profile</dt><dd className="font-mono">{schedule.profileArrangementId}</dd></div>
      <div><dt>Loan principal profile</dt><dd className="font-mono">{schedule.principalArrangementId ?? "None"}</dd></div>
      <div><dt>Tenant and company</dt><dd>{schedule.tenantId}{schedule.companyId ? ` · ${schedule.companyId}` : ""}</dd></div>
    </dl>
    <ol className="mt-3 space-y-2 text-xs" aria-label="Accrual schedule events">{schedule.events.map((event) => <li key={event.eventId}>{formatTimestamp(event.recordedAt)} · {event.action} · {event.actor}{event.reason ? ` · ${event.reason}` : ""}</li>)}</ol>
    </TechnicalDetails>
  </section>;
}

function ScheduleWindows({ label, run, bookId }: { label: string; run: IntercompanyAccrualScheduleRun; bookId: string }) {
  return <div className="space-y-2">
    <p className="text-sm">{run.executed ? `Run as of ${run.asOf}: drafts were requested for due periods; each awaits its own review and nothing posted.` : `As of ${run.asOf}: preview only, nothing was drafted.`}</p>
    <EntityOperationTable<IntercompanyAccrualScheduleWindow> label={label} rows={run.windows} getRowId={(row) => `${row.accrualFrom}:${row.accrualTo}`}
      emptyText="No completed schedule period is due."
      columns={[
        { id: "period", label: "Period", rowHeader: true, render: (row) => `${row.accrualFrom} up to ${row.accrualTo}` },
        { id: "outcome", label: "Outcome", render: (row) => <Badge variant="outline">{row.outcome === "AlreadyDrafted" ? "Already drafted" : row.outcome}</Badge> },
        { id: "draft", label: "Draft", render: (row) => row.draftId
          ? <Button asChild variant="ghost" size="sm" className="underline underline-offset-4"><Link to={`/accounting/journal-entries?${new URLSearchParams({ ledgerBookId: bookId, journalEntryId: row.draftId })}`}>Open draft {row.draftId}{row.draftStatus ? ` · ${row.draftStatus}` : ""}</Link></Button>
          : "None" },
        { id: "reason", label: "Reason", render: (row) => row.reason ?? (row.outcome === "Refused" ? "Refused; retried on the next run." : "") }
      ]} />
  </div>;
}
