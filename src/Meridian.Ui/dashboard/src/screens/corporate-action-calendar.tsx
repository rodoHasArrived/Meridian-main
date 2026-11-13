import { useEffect, useId, useRef, useState } from "react";
import { useInRouterContext, useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { getCorporateActionCalendar } from "@/lib/api/corporate-action-calendar.api";
import { CorporateActionDeadlinePanel } from "@/screens/corporate-action-deadline-panel";
import { addCalendarDays, corporateActionCalendarRange, CORPORATE_ACTION_DATE_KINDS, updateCorporateActionCalendarSearch } from "@/screens/corporate-action-calendar.view-state";
import type { CorporateActionCalendarEvent, CorporateActionCalendarPage } from "@/types/corporate-action-calendar.types";
import type { CorporateActionInboxRowModel } from "@/screens/data-screen.corporate-action-inbox.view-model";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";

interface CalendarProps { scopeKey: string; selectedCaseId?: string | null; onCaseSelected: (caseId: string, proposalId: string) => void; onChanged: () => void }
function localDate() { const now = new Date(); return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`; }

/** Supports isolated inbox embeds; the workstation route uses the same state in its URL. */
export function CorporateActionCalendarPanel(props: CalendarProps) {
  return useInRouterContext() ? <RoutedCalendar {...props} /> : <LocalCalendar {...props} />;
}
function RoutedCalendar(props: CalendarProps) {
  const location = useLocation(); const navigate = useNavigate();
  return <Calendar {...props} search={location.search} setSearch={(search, replace = false) => navigate({ pathname: location.pathname, search, hash: location.hash }, { replace, preventScrollReset: true })} />;
}
function LocalCalendar(props: CalendarProps) {
  const [search, setSearch] = useState("");
  return <Calendar {...props} search={search} setSearch={setSearch} />;
}
function Calendar({ search, setSearch, ...props }: CalendarProps & { search: string; setSearch: (value: string, replace?: boolean) => void }) {
  const { selectedCaseId: queueCaseId, onCaseSelected, scopeKey } = props;
  const [today] = useState(localDate);
  const query = corporateActionCalendarRange(search, today);
  const [revision, setRevision] = useState(0);
  const [read, setRead] = useState<{ key: string; result?: CorporateActionCalendarPage; error?: string } | null>(null);
  const key = JSON.stringify([props.scopeKey, query.from, query.to, query.financialAccountId, query.custodyAccountId, revision]);
  const previousQueueCase = useRef(props.selectedCaseId);
  const previousScope = useRef(scopeKey);
  const updateSearch = (values: Record<string, string | null>, replace = false) => setSearch(updateCorporateActionCalendarSearch(search, values), replace);
  useEffect(() => {
    if (previousScope.current !== scopeKey) {
      previousScope.current = scopeKey; previousQueueCase.current = queueCaseId;
      if (query.caseId && query.caseId !== queueCaseId) onCaseSelected(query.caseId, "");
      return;
    }
    if (previousQueueCase.current !== queueCaseId) {
      const previous = previousQueueCase.current;
      previousQueueCase.current = queueCaseId;
      if (previous == null && query.caseId) { onCaseSelected(query.caseId, ""); return; }
      if (query.caseId === queueCaseId) return;
      setSearch(updateCorporateActionCalendarSearch(search, { caCase: queueCaseId ?? null, caEvent: null }));
      return;
    }
    if (query.caseId && query.caseId !== queueCaseId) onCaseSelected(query.caseId, "");
  }, [queueCaseId, query.caseId, search, setSearch, onCaseSelected, scopeKey]);
  useEffect(() => {
    if (query.error) return;
    const controller = new AbortController(); let active = true;
    getCorporateActionCalendar({ from: query.from, to: query.to, financialAccountId: query.financialAccountId, custodyAccountId: query.custodyAccountId }, { signal: controller.signal })
      .then((result) => { if (active) setRead({ key, result }); })
      .catch((error: unknown) => { if (active) setRead({ key, error: error instanceof Error ? error.message : "The retained calendar could not be loaded." }); });
    return () => { active = false; controller.abort(); };
  }, [key, query.error, query.from, query.to, query.financialAccountId, query.custodyAccountId]);
  const current = read?.key === key ? read : null;
  const result = current?.result;
  const selectedCaseId = query.caseId ?? props.selectedCaseId;
  const selectedEvent = result?.items.find((event) => event.eventId === query.eventId);
  const selectCase = (caseId: string, proposalId: string, eventId?: string) => {
    updateSearch({ caCase: caseId, ...(eventId ? { caEvent: eventId } : {}) }); props.onCaseSelected(caseId, proposalId);
  };
  const selectEvent = (event: CorporateActionCalendarEvent) => {
    if (event.cases.length === 1) selectCase(event.cases[0].caseId, event.proposalId, event.eventId);
    else updateSearch({ caEvent: event.eventId });
  };
  const refresh = () => setRevision((value) => value + 1);
  const changed = () => { refresh(); props.onChanged(); };
  const days: string[] = [];
  if (!query.error) for (let date = query.from; date <= query.to && days.length <= 367; date = addCalendarDays(date, 1)) days.push(date);
  const securities = result ? [...new Map(result.items.map((event) => [event.securityId, { id: event.securityId, ticker: event.ticker }])).values()] : [];
  const cells = new Map<string, CorporateActionCalendarEvent[]>();
  for (const event of result?.items ?? []) { const cell = `${event.securityId}:${event.date}`; const group = cells.get(cell); if (group) group.push(event); else cells.set(cell, [event]); }
  const eventButton = (event: CorporateActionCalendarEvent) => {
    const kind = CORPORATE_ACTION_DATE_KINDS[event.dateKind] ?? { symbol: "?", label: event.dateKind };
    return <button type="button" key={event.eventId} aria-pressed={event.eventId === query.eventId} onClick={() => selectEvent(event)} className={`my-1 block w-full rounded-sm border px-2 py-1 text-left text-xs focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary ${event.eventId === query.eventId ? "border-primary bg-primary/10" : "border-border bg-background"}`} aria-label={`${kind.label} for ${event.ticker} on ${event.date}${event.at ? ` at ${event.at}` : ""}`}><span className="font-semibold">{kind.symbol} {kind.label}</span><span className="block">{event.actionType}{event.at ? ` · ${event.at}` : ""}</span>{event.deadline ? <span className="block">{event.deadline.deadline.sourceParty} · operator-recorded</span> : event.sourceCutoff ? <span className="block">{event.sourceCutoff.label} · source-reported</span> : null}</button>;
  };
  return <section className="min-w-0 max-w-full space-y-3 rounded-sm border border-border bg-background p-3" style={{ contain: "inline-size" }} aria-label="Corporate action dates calendar">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><h3 className="text-sm font-semibold">Corporate action dates</h3><p className="mt-1 text-xs text-muted-foreground">Ex-date, record date, payment date and actual election deadlines have different meanings. Source calendar dates are retained without timezone conversion.</p></div><Button size="sm" variant="outline" onClick={refresh}>Refresh dates calendar</Button></div>
    <div className="flex flex-wrap gap-3"><label className="grid gap-1 text-xs">Calendar from<Input type="date" value={query.from} onChange={(event) => updateSearch({ caFrom: event.target.value, caEvent: null })} /></label><label className="grid gap-1 text-xs">Calendar through<Input type="date" value={query.to} onChange={(event) => updateSearch({ caTo: event.target.value, caEvent: null })} /></label></div>
    <p className="text-xs">{Object.values(CORPORATE_ACTION_DATE_KINDS).map((kind) => `${kind.symbol} ${kind.label}`).join(" · ")}</p>
    {query.error ? <p role="status">{query.error}</p> : !current ? <p role="status">Loading all retained calendar pages…</p> : null}
    {current?.error ? <p role="status">Calendar unavailable: {current.error} The range has not been represented as complete.</p> : null}
    {result ? <>
      <p className="break-words text-xs text-muted-foreground">{result.items.length} retained date events loaded · generated {result.generatedAtUtc}. {result.coverage} All pages in this retained revision are loaded.</p>
      {result.items.length === 0 ? <p className="text-sm">No retained date events in this range. This does not establish that no market events exist.</p> : days.length <= 31 ? <DenseDataTable ariaLabel="Security by date corporate action calendar" rows={securities} getRowId={(security) => security.id} emptyText="No retained security dates." maxVisibleRows={null} tableClassName="min-w-max" columns={[
        { id: "security", label: "Security", rowHeader: true, className: "sticky left-0 z-10 min-w-28 bg-background", render: (security) => <span title={security.id}>{security.ticker}</span> },
        ...days.map((date) => ({ id: date, label: date, className: "min-w-36 align-top", render: (security: { id: string; ticker: string }) => (cells.get(`${security.id}:${date}`) ?? []).map(eventButton) }))
      ]} /> : <div><p className="mb-2 text-xs">The selected range spans more than 31 days; the complete retained events are shown as a date agenda.</p><ul>{result.items.map((event) => <li key={event.eventId}><time dateTime={event.date} className="font-mono text-xs">{event.date} · {event.ticker}</time>{eventButton(event)}</li>)}</ul></div>}
      {query.eventId && !selectedEvent ? <p role="status">The explicitly selected date event is unavailable in this range. No substitute event was selected.</p> : null}
      {selectedEvent ? <div className="space-y-2 rounded-sm border border-border p-3 text-xs"><h4 className="font-semibold">Selected date evidence · {selectedEvent.ticker}</h4><p>{CORPORATE_ACTION_DATE_KINDS[selectedEvent.dateKind]?.label ?? selectedEvent.dateKind} · {selectedEvent.date}{selectedEvent.at ? ` · ${selectedEvent.at}` : ""} · proposal {selectedEvent.proposalState}</p><p className="break-all">Provider {selectedEvent.source.providerId} · event {selectedEvent.source.sourceEventId} · version {selectedEvent.source.sourceEventVersion} · observed {selectedEvent.source.observedAtUtc} · source reference {selectedEvent.source.evidenceReference ?? "unreported"}</p>{selectedEvent.deadline ? <p className="whitespace-pre-wrap">Original source text: {selectedEvent.deadline.deadline.sourceText ?? "not retained"} · evidence {selectedEvent.deadline.evidenceReference}</p> : null}{selectedEvent.sourceCutoff ? <p className="whitespace-pre-wrap break-words">Source-reported cutoff: {selectedEvent.sourceCutoff.label} · {selectedEvent.sourceCutoff.deadlineAt} (supplied offset) · {selectedEvent.sourceCutoff.source} · evidence {selectedEvent.sourceCutoff.evidenceReference}. Intermediary role and account applicability are not reported.</p> : null}<div className="flex flex-wrap gap-2">{selectedEvent.cases.map((item) => <Button key={item.caseId} size="sm" variant="outline" onClick={() => selectCase(item.caseId, selectedEvent.proposalId, selectedEvent.eventId)}>Inspect case {item.caseId} · {item.state}</Button>)}</div>{selectedEvent.cases.length === 0 ? <p>This retained source event has no processing case in the authorized scope. Use the source proposal queue for its governed acceptance workflow.</p> : null}</div> : null}
    </> : null}
    {selectedCaseId && result && !result.items.some((event) => event.cases.some((item) => item.caseId === selectedCaseId)) ? <p className="text-xs">The selected case has no retained dates in this calendar range or account filter. Its case history remains selected.</p> : null}
    {selectedCaseId ? <CorporateActionDeadlinePanel key={`${props.scopeKey}:${query.financialAccountId ?? ""}:${query.custodyAccountId ?? ""}:${selectedCaseId}`} caseId={selectedCaseId} onChanged={changed} /> : <p className="text-xs">Select a processing case from the queue or a calendar marker to inspect sourced election deadlines.</p>}
  </section>;
}

type CalendarEvent = { id: string; rowId: string; day: string; kind: "Ex-date" | "Record date" | "Payment date" | "Election cutoff"; symbol: string; detail: string };
const DAY = 86_400_000;

export function isCorporateCalendarDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(`${value}T00:00:00Z`))
    && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
}

export function corporateCalendarEvents(rows: readonly CorporateActionInboxRowModel[]): CalendarEvent[] {
  return rows.flatMap((row) => {
    const events: CalendarEvent[] = [];
    const dates = [["Ex-date", "X", row.exDateLabel], ["Record date", "R", row.recordDateLabel], ["Payment date", "P", row.payableDateLabel]] as const;
    for (const [kind, symbol, day] of dates) {
      if (isCorporateCalendarDate(day)) events.push({ id: `${row.rowId}:${kind}`, rowId: row.rowId, day, kind, symbol, detail: `${day} · Date-only source fact` });
    }
    if (row.electionCalendar?.evidenceState === "Available") {
      for (const cutoff of row.electionCalendar.cutoffs) {
        // A time without an offset is not sufficient to place an actual election cutoff.
        if (!/T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(cutoff.deadlineAt)
          || !Number.isFinite(Date.parse(cutoff.deadlineAt)) || !cutoff.source || !cutoff.evidenceReference) continue;
        events.push({ id: `${row.rowId}:election:${cutoff.id}`, rowId: row.rowId,
          day: cutoff.deadlineAt.slice(0, 10), kind: "Election cutoff", symbol: "E",
          detail: `${cutoff.label} · ${cutoff.deadlineAt} (source offset) · ${cutoff.source} · ${cutoff.evidenceReference}` });
      }
    }
    return events;
  });
}

function shiftDay(day: string, days: number) { return new Date(Date.parse(`${day}T00:00:00Z`) + days * DAY).toISOString().slice(0, 10); }
function currentWeek() {
  const today = localDate();
  const weekday = new Date(`${today}T00:00:00Z`).getUTCDay();
  return shiftDay(today, -((weekday + 6) % 7));
}

export function CorporateActionCalendar({ rows, selectedRowId, onSelect, stale = false, caseWorkspaceId }: {
  rows: readonly CorporateActionInboxRowModel[]; selectedRowId: string | null;
  onSelect: (rowId: string) => void; stale?: boolean; caseWorkspaceId?: string;
}) {
  const id = useId();
  const [start, setStart] = useState(currentWeek);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const validStart = isCorporateCalendarDate(start);
  const days = validStart ? Array.from({ length: 7 }, (_, index) => shiftDay(start, index)) : [];
  const events = corporateCalendarEvents(rows);
  const selected = events.find((event) => event.id === selectedId && event.rowId === selectedRowId);
  const missingCutoffs = rows.filter((row) => row.electionCalendar?.evidenceState !== "Available");
  const missingDates = rows.reduce((count, row) => count + [row.exDateLabel, row.recordDateLabel, row.payableDateLabel].filter((date) => !isCorporateCalendarDate(date)).length, 0);
  const visible = events.filter((event) => days.includes(event.day));
  const columns: DenseDataTableColumn<CorporateActionInboxRowModel>[] = [
    { id: "security", label: "Security / action", render: (row) => <span className="block max-w-52 text-left font-normal"><span className="font-semibold">{row.ticker}</span><br />{row.actionType}<br /><span className="text-muted-foreground">{row.statusLabel}</span><br /><span className="block max-w-52 truncate font-mono text-xs" title={row.caseIdLabel}>Case: {row.caseIdLabel}</span></span> },
    ...days.map((day): DenseDataTableColumn<CorporateActionInboxRowModel> => ({
      id: day, label: day, align: "right", className: "whitespace-nowrap font-mono",
      render: (row) => {
        const cell = events.filter((event) => event.rowId === row.rowId && event.day === day);
        return cell.length ? <div className="flex flex-wrap justify-end gap-1">{cell.map((event) => <button type="button" key={event.id}
          className="min-h-8 min-w-8 rounded-sm border border-border px-2 font-mono font-semibold hover:bg-secondary focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary"
          aria-label={`${event.kind} for ${row.ticker}, ${day}, ${row.statusLabel}, case ${row.caseIdLabel}`} aria-controls={caseWorkspaceId} aria-pressed={selected?.id === event.id}
          onClick={() => { setSelectedId(event.id); onSelect(row.rowId); }}>{event.symbol}</button>)}</div> : <span aria-label="No supplied event">—</span>;
      }
    }))
  ];
  return (
    <details open className="rounded-sm border border-border p-3">
      <summary className="cursor-pointer text-sm font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">Corporate-action dates</summary>
      <div className="mt-3 space-y-3">
        <p className="text-xs text-muted-foreground">X Ex-date · R Record date · P Payment date · E Actual election cutoff. Corporate dates are date-only; election cutoffs retain their supplied civil day and source offset. Select an event to open its case.</p>
        <div className="flex flex-wrap items-end gap-2">
          <label htmlFor={`${id}-start`} className="grid gap-1 text-xs">Week starting
            <Input id={`${id}-start`} type="date" value={start} onChange={(event) => setStart(event.target.value)} />
          </label>
          <Button size="sm" variant="outline" disabled={!validStart} onClick={() => setStart(shiftDay(start, -7))}>Previous week</Button>
          <Button size="sm" variant="outline" onClick={() => setStart(currentWeek())}>This week</Button>
          <Button size="sm" variant="outline" disabled={!validStart} onClick={() => setStart(shiftDay(start, 7))}>Next week</Button>
        </div>
        {!validStart ? <p role="alert" className="text-sm text-danger">Choose a valid calendar date.</p> : (
          <DenseDataTable rows={[...rows]} getRowId={(row) => row.rowId} selectedRowId={selectedRowId} columns={columns}
            tableClassName="min-w-[740px]" ariaLabel="Corporate-action date calendar"
            emptyText="No cases in the current queue filters."
            caption={`Corporate-action date calendar, ${start} through ${days[6]}; events open their existing case inspector.`} />
        )}
        <p role="status" className="text-xs text-muted-foreground">{visible.length} events in this week · {events.length - visible.length} outside this week · {rows.length} cases in current queue filters{stale ? " · Retained queue; refresh has not succeeded" : ""}.</p>
        {missingCutoffs.length ? <p className="text-xs text-warning">Actual election cutoff evidence unavailable for {missingCutoffs.length} cases. Ex-dates and offer-expiry dates do not establish election deadlines.</p> : null}
        {missingDates ? <p className="text-xs text-muted-foreground">{missingDates} corporate dates are unavailable or not applicable; they are not placed on the calendar.</p> : null}
        {rows.flatMap((row) => (row.electionCalendar?.messages ?? []).map((message, index) => <p key={`${row.rowId}:${index}`} className="text-xs text-muted-foreground">{row.ticker}: {message}</p>))}
        {selected ? <p role="status" className="break-words rounded-sm border border-border p-2 text-xs"><strong>{selected.kind}</strong> · {selected.detail}</p> : null}
      </div>
    </details>
  );
}
