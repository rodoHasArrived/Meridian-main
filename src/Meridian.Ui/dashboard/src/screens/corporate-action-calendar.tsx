import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import type { CorporateActionInboxRowModel } from "@/screens/data-screen.corporate-action-inbox.view-model";

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
          day: new Date(cutoff.deadlineAt).toISOString().slice(0, 10), kind: "Election cutoff", symbol: "E",
          detail: `${cutoff.label} · ${cutoff.deadlineAt} (source offset) · ${cutoff.source} · ${cutoff.evidenceReference}` });
      }
    }
    return events;
  });
}

function shiftDay(day: string, days: number) { return new Date(Date.parse(`${day}T00:00:00Z`) + days * DAY).toISOString().slice(0, 10); }
function currentWeek() {
  const today = new Date().toISOString().slice(0, 10);
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
        <p className="text-xs text-muted-foreground">X Ex-date · R Record date · P Payment date · E Actual election cutoff. Corporate dates are date-only; election cutoffs occupy their UTC calendar day and retain their source offset. Select an event to open its case.</p>
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
