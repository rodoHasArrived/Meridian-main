import { useEffect, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { getJson } from "@/lib/api/http-client";

export interface RateLimitHistoryResponse {
  provider: string; periodHours: number; timestamp: string; isAvailable: boolean; message: string;
  history: { timestamp: string; requestsUsed: number; usageRatio: number; wasRateLimited: boolean; resetAt: string | null; eventType: string; nextProvider: string | null; httpStatus: number | null }[];
}
type HistoryRow = RateLimitHistoryResponse["history"][number] & { id: string };
const historyColumns: DenseDataTableColumn<HistoryRow>[] = [
  { id: "timestamp", label: "Time (UTC)", className: "whitespace-nowrap font-mono text-xs", render: (row) => new Date(row.timestamp).toISOString() },
  { id: "event", label: "Event", render: (row) => `${row.eventType === "rotated" ? "Rotated" : "Rate limited"}${row.httpStatus ? ` (HTTP ${row.httpStatus})` : ""}` },
  { id: "requests", label: "Requests", render: (row) => row.requestsUsed },
  { id: "usage", label: "Usage", render: (row) => `${Math.round(row.usageRatio * 100)}%` },
  { id: "reset", label: "Resets (UTC)", className: "whitespace-nowrap font-mono text-xs", render: (row) => row.resetAt ? new Date(row.resetAt).toISOString() : "Unknown" },
  { id: "next", label: "Next attempted provider", render: (row) => row.nextProvider ?? "Not yet observed" }
];
const readHistory = (provider: string, hours: number, signal?: AbortSignal) => getJson<RateLimitHistoryResponse>(
  `/api/providers/${encodeURIComponent(provider)}/rate-limit-history?hours=${hours}`, { signal, allowDevelopmentFallback: false });
export function RateLimitHistoryPanel({ fetchHistory = readHistory }: { fetchHistory?: typeof readHistory }) {
  const [provider, setProvider] = useState("polygon");
  const [hours, setHours] = useState(24);
  const [snapshot, setSnapshot] = useState<RateLimitHistoryResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const operation = useRef(0);
  const activeRequest = useRef<AbortController | null>(null);
  useEffect(() => () => { operation.current++; activeRequest.current?.abort(); }, []);
  async function load(event: FormEvent) {
    event.preventDefault(); activeRequest.current?.abort(); const controller = new AbortController(); activeRequest.current = controller; const version = ++operation.current;
    setBusy(true); setError(null); setSnapshot(null);
    try { const result = await fetchHistory(provider.trim(), hours, controller.signal); if (version === operation.current) setSnapshot(result); }
    catch (failure) { if (version === operation.current && !controller.signal.aborted) setError(failure instanceof Error ? failure.message : "Unable to load quota history."); }
    finally { if (version === operation.current) setBusy(false); }
  }
  return <details className="rounded-lg border bg-card p-4">
    <summary className="cursor-pointer font-semibold focus-visible:outline focus-visible:outline-2">Rate-limit and quota history</summary>
    <div className="mt-4 space-y-3">
      <p className="text-sm text-muted-foreground">Inspect throttling and the next provider actually attempted. History is bounded, held in memory and cleared on host restart.</p>
      <form onSubmit={(event) => void load(event)} className="flex flex-wrap items-end gap-2">
        <label className="text-sm">History provider ID<Input value={provider} onChange={(event) => { setProvider(event.target.value); setSnapshot(null); }} required disabled={busy} /></label>
        <label className="text-sm">Lookback (hours)<Input type="number" min={1} max={168} value={hours} onChange={(event) => { setHours(Number(event.target.value)); setSnapshot(null); }} required disabled={busy} /></label>
        <Button type="submit" variant="outline" disabled={busy}>{busy ? "Loading…" : "Load quota history"}</Button>
      </form>
      {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
      {snapshot ? <><p className="text-xs text-muted-foreground">{snapshot.message}</p><p className="text-sm font-medium">{snapshot.provider} · last {snapshot.periodHours} hours</p>
        {!snapshot.isAvailable ? <p role="status">Quota history is unavailable.</p> : snapshot.history.length === 0 ? <p role="status">No retained rate-limit events for this period. Older events may have been evicted.</p> : <DenseDataTable columns={historyColumns} rows={snapshot.history.map((entry, index) => ({ ...entry, id: `${entry.timestamp}-${index}` }))} getRowId={(row) => row.id} ariaLabel={`Retained quota events for ${snapshot.provider}`} emptyText="No retained events." />}
      </> : null}
    </div>
  </details>;
}
