import { CategoryBars, type CategoryBar } from "@/components/charts/CategoryBars";
import { ChartCard } from "@/components/charts/ChartCard";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { formatNumber } from "@/lib/format";
import type { CloseTask, OperationsCloseChecklistTask, ProviderConnectionRow, ReconciliationBreakQueueItem } from "@/types";

function EvidenceBars({ title, basis, rows, emptyText, note }: {
  title: string; basis: string; rows: CategoryBar[]; emptyText: string; note?: string;
}) {
  return <section aria-label={title} className="space-y-2">
    {rows.length > 0 ? <ChartCard title={title} subtitle={basis} height={Math.min(300, Math.max(110, rows.length * 40))}>
      <CategoryBars rows={rows} label={title} />
    </ChartCard> : <div className="border border-border p-4"><h3 className="text-sm font-medium">{title}</h3><p role="status" className="mt-2 text-sm text-muted-foreground">{emptyText}</p></div>}
    {note ? <p className="text-xs text-muted-foreground">{note}</p> : null}
    {rows.length > 0 ? <details className="workspace-task-disclosure">
      <summary>View {title.toLowerCase()} as a table</summary>
      <TableWorkbench workbenchId={`${title.toLowerCase().replaceAll(" ", "-")}-data`} sourceKey={`${basis}:${rows.map((row) => `${row.id}:${row.value}`).join("|")}`} sourceDescription={`${basis}. ${note ?? ""}`} completePopulation tableId={`${title.toLowerCase().replaceAll(" ", "-")}-data`} rows={rows} getRowId={(row) => row.id}
        columns={[{ id: "label", label: "Category", render: (row) => row.label, exportValue: (row) => row.label }, { id: "value", label: "Value", render: (row) => row.valueLabel, exportValue: (row) => row.value }]}
        ariaLabel={`${title} data`} emptyText={emptyText} />
    </details> : null}
  </section>;
}

export function buildProviderFreshnessRows(providers: ProviderConnectionRow[], nowMs: number) {
  const rows: CategoryBar[] = [];
  for (const provider of providers) {
    const timestamp = Date.parse(provider.lastSuccessfulAt ?? "");
    if (!Number.isFinite(timestamp) || timestamp > nowMs) continue;
    const hours = (nowMs - timestamp) / 3_600_000;
    rows.push({ id: provider.providerId, label: provider.displayName, value: hours, valueLabel: `${formatNumber(hours, { maximumFractionDigits: 1 })}h` });
  }
  return { rows, unavailable: providers.length - rows.length };
}

export function ProviderFreshnessChart({ providers, nowMs = Date.now() }: { providers: ProviderConnectionRow[]; nowMs?: number }) {
  const model = buildProviderFreshnessRows(providers, nowMs);
  return <EvidenceBars title="Provider evidence age" basis="Hours since last successful evidence · current loaded connections"
    rows={model.rows} emptyText="Timestamped provider evidence is unavailable. Connection state does not establish freshness."
    note={`${model.unavailable} connection(s) have no usable timestamp. This snapshot does not show historical uptime or error rate.`} />;
}

export function buildBreakAgingRows(breaks: ReconciliationBreakQueueItem[], nowMs: number) {
  const groups = [
    { id: "under4", label: "Under 4 hours", value: 0, valueLabel: "" },
    { id: "4to24", label: "4–24 hours", value: 0, valueLabel: "" },
    { id: "24to72", label: "24–72 hours", value: 0, valueLabel: "" },
    { id: "over72", label: "72+ hours", value: 0, valueLabel: "" }
  ];
  let unavailable = 0;
  for (const item of breaks) {
    const timestamp = Date.parse(item.lineage?.occurrenceFirstObservedAt ?? "");
    if (!Number.isFinite(timestamp) || timestamp > nowMs) { unavailable += 1; continue; }
    const hours = (nowMs - timestamp) / 3_600_000;
    groups[hours < 4 ? 0 : hours < 24 ? 1 : hours < 72 ? 2 : 3].value += 1;
  }
  return { rows: groups.map((group) => ({ ...group, valueLabel: String(group.value) })), unavailable, tracked: breaks.length - unavailable };
}

export function ReconciliationAgingChart({ breaks, nowMs = Date.now() }: { breaks: ReconciliationBreakQueueItem[]; nowMs?: number }) {
  const model = buildBreakAgingRows(breaks, nowMs);
  return <EvidenceBars title="Break occurrence age" basis="Loaded queue · elapsed hours since retained occurrence first observed"
    rows={model.tracked > 0 ? model.rows : []} emptyText="No retained occurrence timestamps are available for aging."
    note={`${model.tracked} tracked occurrence(s); ${model.unavailable} missing or invalid timestamp(s). Source clearing and case resolution remain separate states.`} />;
}

export function CloseTaskProgressChart({ tasks }: { tasks: CloseTask[] }) {
  const states: CloseTask["status"][] = ["NotStarted", "WaitingOnDependency", "InProgress", "ReadyForSignOff", "SignedOff", "Blocked"];
  const labels = ["Not started", "Waiting on dependency", "In progress", "Ready for sign-off", "Signed off", "Blocked"];
  const rows = tasks.length === 0 ? [] : states.map((state, index) => {
    const count = tasks.filter((task) => task.status === state).length;
    return { id: state, label: labels[index], value: count, valueLabel: String(count) };
  });
  return <EvidenceBars title="Close task progress" basis="Current loaded close plan · task counts by retained state" rows={rows}
    emptyText="No close plan tasks are loaded." note="Historical task snapshots are unavailable; this view shows current progress rather than a burndown history." />;
}

export function CloseChecklistProgressChart({ tasks }: { tasks: OperationsCloseChecklistTask[] }) {
  const counts = new Map<string, number>();
  for (const task of tasks) counts.set(task.status || "Status unavailable", (counts.get(task.status || "Status unavailable") ?? 0) + 1);
  const rows = Array.from(counts, ([status, count]) => ({ id: status, label: status, value: count, valueLabel: String(count) }));
  return <EvidenceBars title="Close checklist progress" basis="Current loaded workflow · retained checklist states" rows={rows}
    emptyText="No close checklist is loaded." note="Current task states are shown here. Historical changes require the retained workflow timeline." />;
}
