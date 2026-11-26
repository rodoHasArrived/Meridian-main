import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TimeText } from "@/components/ui/time-text";
import { ApiError, describeApiError } from "@/lib/api-errors";
import { formatTimestamp } from "@/lib/format";
import { assertFrozenRateRepairUnchanged, assertRateRepairProposal, commandRateRepairBatch, createRateRepairBatch, discoverRateRepairs, getRateRepairBatch, listRateRepairBatches,
  type CreateRateRepairBatch, type RateRepairBatch, type RateRepairCommand, type RateRepairCommandName,
  type RateRepairDiscovery } from "@/lib/api/security-rate-repair.api";
import { RateRepairSelection } from "./security-rate-repair-selection";
import { RateRepairBatchReview } from "./security-rate-repair-batch-review";
import { mergeRateRepairPage } from "./security-rate-repair-discovery";

export function SecurityRateRepairQueueRegion() {
  const location = useLocation();
  const navigate = useNavigate();
  const params = new URLSearchParams(location.search);
  const open = params.get("rateRepair") === "queue";
  function changeQueue(openQueue: boolean, batchId?: string) {
    const next = new URLSearchParams(location.search);
    if (openQueue) next.set("rateRepair", "queue"); else next.delete("rateRepair");
    if (batchId) next.set("rateRepairBatch", batchId); else next.delete("rateRepairBatch");
    navigate({ pathname: location.pathname, search: `?${next}`, hash: "#security-rate-repairs" }, { preventScrollReset: true });
  }
  return <section id="security-rate-repairs" className="scroll-mt-28 rounded border border-border p-4" aria-label="Governed rate-unit repairs">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h2 className="text-base font-semibold">Rate-unit repair queue</h2><p className="mt-1 text-sm text-muted-foreground">Declare evidenced units through separate drafts, independent approval and explicit publication.</p></div>
      <Button variant="outline" size="sm" aria-expanded={open} aria-controls="security-rate-repair-content" onClick={() => changeQueue(!open)}>
        {open ? "Close rate-unit repair queue" : "Open rate-unit repair queue"}
      </Button>
    </div>
    <div id="security-rate-repair-content" className="mt-4" hidden={!open}>{open ? <RateRepairQueue
      initialSearch={params.get("securityId") ?? ""} selectedBatchId={params.get("rateRepairBatch")?.toLowerCase() ?? null}
      onSelectBatch={batchId => changeQueue(true, batchId)} /> : null}</div>
  </section>;
}

function RateRepairQueue({ initialSearch, selectedBatchId, onSelectBatch }: {
  initialSearch: string; selectedBatchId: string | null; onSelectBatch: (batchId?: string) => void;
}) {
  const [discovery, setDiscovery] = useState<RateRepairDiscovery | null>(null);
  const [batches, setBatches] = useState<RateRepairBatch[]>([]);
  const [batch, setBatch] = useState<RateRepairBatch | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [loading, setLoading] = useState(true);
  const [batchLoading, setBatchLoading] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [batchRefreshKey, setBatchRefreshKey] = useState(0);
  const [needsRefresh, setNeedsRefresh] = useState(false);
  const [pendingCreate, setPendingCreate] = useState<CreateRateRepairBatch | null>(null);
  const [pageLoading, setPageLoading] = useState(false);
  const [selectionGeneration, setSelectionGeneration] = useState(0);
  const [scanNotice, setScanNotice] = useState("");
  const [pageCount, setPageCount] = useState(0);
  const [lastPageAt, setLastPageAt] = useState("");
  const mutation = useRef<AbortController | null>(null);
  const mutationBatchId = useRef<string | null>(null);
  const pendingRef = useRef<CreateRateRepairBatch | null>(null);
  const pageRequest = useRef<AbortController | null>(null);
  const currentSelection = useRef(selectedBatchId);
  // A history navigation must invalidate old acknowledgements before the new batch is painted.
  useLayoutEffect(() => { currentSelection.current = selectedBatchId; }, [selectedBatchId]);
  function selectContext(batchId?: string) { currentSelection.current = batchId ?? null; onSelectBatch(batchId); }
  useEffect(() => {
    const controller = new AbortController();
    pageRequest.current?.abort(); pageRequest.current = null; setPageLoading(false);
    setLoading(true); setError(""); setDiscovery(null); setScanNotice(""); setPageCount(0);
    setSelectionGeneration(current => current + 1);
    Promise.allSettled([discoverRateRepairs({ signal: controller.signal }), listRateRepairBatches({ signal: controller.signal })]).then(results => {
      if (controller.signal.aborted) return;
      const [candidateResult, batchResult] = results;
      if (candidateResult.status === "fulfilled") {
        setDiscovery(candidateResult.value); setPageCount(1); setLastPageAt(candidateResult.value.asOfUtc);
      }
      if (batchResult.status === "fulfilled") setBatches(batchResult.value);
      const failures = results.filter((result): result is PromiseRejectedResult => result.status === "rejected");
      if (failures.length) setError(failures.map(result => describeApiError(result.reason, "Rate repair data is unavailable.").summary).join(" "));
      setLoading(false);
    });
    return () => controller.abort();
  }, [refreshKey]);
  useEffect(() => {
    const controller = new AbortController();
    if (mutation.current && mutationBatchId.current !== selectedBatchId) {
      mutation.current.abort(); mutation.current = null; mutationBatchId.current = null; setBusy(null);
    }
    if (pendingRef.current && pendingRef.current.requestId !== selectedBatchId) {
      pendingRef.current = null; setPendingCreate(null);
    }
    setBatch(null); setBatchLoading(false); setNeedsRefresh(false); setNotice(""); setError("");
    if (!selectedBatchId) return () => controller.abort();
    if (mutation.current && pendingRef.current?.requestId === selectedBatchId) {
      setBatchLoading(false); return () => controller.abort();
    }
    setBatchLoading(true);
    getRateRepairBatch(selectedBatchId, { signal: controller.signal }).then(result => {
      if (!controller.signal.aborted && currentSelection.current === selectedBatchId) {
        if (pendingRef.current?.requestId === result.batchId) assertRateRepairProposal(result, pendingRef.current);
        setBatch(result); setNeedsRefresh(false);
        if (pendingRef.current?.requestId === result.batchId) { pendingRef.current = null; setPendingCreate(null); }
      }
    }).catch(cause => { if (!controller.signal.aborted && currentSelection.current === selectedBatchId) { setNeedsRefresh(true); setError(describeApiError(cause, "The retained repair batch is unavailable.").summary); } })
      .finally(() => { if (!controller.signal.aborted && currentSelection.current === selectedBatchId) setBatchLoading(false); });
    return () => controller.abort();
  }, [selectedBatchId, batchRefreshKey]);
  useEffect(() => () => { mutation.current?.abort(); pageRequest.current?.abort(); }, []);

  function retainResult(result: RateRepairBatch) {
    setBatch(result); setBatches(current => [result, ...current.filter(row => row.batchId !== result.batchId)]);
    setNeedsRefresh(false);
  }
  async function freeze(input: CreateRateRepairBatch) {
    if (mutation.current || loading || pageLoading) return;
    const frozen = pendingRef.current ?? input;
    pendingRef.current = frozen; setPendingCreate(frozen);
    const controller = new AbortController(); mutation.current = controller;
    mutationBatchId.current = frozen.requestId;
    setBusy("create"); setError(""); setNotice("");
    // Keep the recoverable identity in the URL before dispatch; a reload can resolve a committed
    // result even when the save acknowledgement never reached this browser.
    selectContext(frozen.requestId);
    try {
      const result = await createRateRepairBatch(frozen, { signal: controller.signal });
      if (controller.signal.aborted || currentSelection.current !== frozen.requestId) return;
      retainResult(result); pendingRef.current = null; setPendingCreate(null);
      selectContext(result.batchId); setNotice("Batch retained with a frozen record roster. Stage the per-record drafts to continue.");
    } catch (cause) {
      if (controller.signal.aborted || currentSelection.current !== frozen.requestId) return;
      setError(describeApiError(cause, "The batch save was not confirmed.").summary);
      if (cause instanceof ApiError && [400, 401, 403, 404, 409, 422].includes(cause.status)) {
        pendingRef.current = null; setPendingCreate(null);
        setDiscovery(null); selectContext(); setNotice("The proposal was refused. Refresh candidates and correct the evidenced scope before creating a new batch.");
      } else setNotice("Save outcome is unconfirmed. Keep the same frozen request and retry it, or reload its retained batch. Do not create a second proposal.");
    } finally { if (mutation.current === controller) { mutation.current = null; mutationBatchId.current = null; setBusy(null); } }
  }
  async function command(name: RateRepairCommandName, input: RateRepairCommand) {
    if (!batch || mutation.current || needsRefresh || currentSelection.current !== batch.batchId) return;
    const controller = new AbortController(); mutation.current = controller;
    mutationBatchId.current = batch.batchId;
    setBusy(name); setError(""); setNotice("");
    try {
      const result = await commandRateRepairBatch(batch.batchId, name, input, { signal: controller.signal });
      if (controller.signal.aborted || currentSelection.current !== batch.batchId) return;
      retainResult(assertFrozenRateRepairUnchanged(batch, result)); setNotice(`${name[0].toUpperCase()}${name.slice(1)} completed. Inspect each record's retained outcome; any conflict or failure remains explicit.`);
    } catch (cause) {
      if (!controller.signal.aborted && currentSelection.current === batch.batchId) {
        setNeedsRefresh(true); setError(describeApiError(cause, "The batch command was not confirmed.").summary);
        setNotice("Refresh the retained batch before another command. Partial outcomes and completed receipts must be recovered before retrying.");
      }
    } finally { if (mutation.current === controller) { mutation.current = null; mutationBatchId.current = null; setBusy(null); } }
  }
  async function loadMore() {
    if (!discovery || discovery.nextSkip === null || pageRequest.current || loading || mutation.current) return;
    const controller = new AbortController(); pageRequest.current = controller; setPageLoading(true); setError("");
    try {
      const page = await discoverRateRepairs({ signal: controller.signal }, { skip: discovery.nextSkip, pageSize: discovery.pageSize });
      if (controller.signal.aborted) return;
      const merged = mergeRateRepairPage(discovery, page);
      setDiscovery(merged.discovery); setPageCount(current => current + 1); setLastPageAt(page.asOfUtc);
      if (merged.changedRecordIds.length) {
        setSelectionGeneration(current => current + 1);
        setScanNotice("Records changed between pages. The prior selection was cleared; affected rows remain blocked until a fresh source scan.");
      }
    } catch (cause) {
      if (!controller.signal.aborted) {
        const message = describeApiError(cause, "The next source page is unavailable.").summary;
        setError(message); setDiscovery({ ...discovery, isComplete: false, nextSkip: null,
          actions: [{ command: "Create", enabled: false, disabledReason: "Refresh the incomplete source scan before freezing records." }],
          issues: [...discovery.issues, { securityId: null, code: "SourcePageUnavailable", message }] });
      }
    } finally { if (pageRequest.current === controller) { pageRequest.current = null; setPageLoading(false); } }
  }
  const locked = busy !== null || loading || batchLoading || pageLoading;
  return <div className="space-y-4">
    {loading ? <p role="status">Loading authoritative repair candidates and retained batches…</p> : null}
    {error ? <StatusBanner tone="danger" title="Rate repair needs attention" detail={error} role="alert" /> : null}
    {notice ? <p role="status" className="text-sm">{notice}</p> : null}
    <div className="flex flex-wrap items-end gap-3">
      <label className="grid min-w-64 flex-1 gap-1 text-sm">Recent repair batch (up to 200)
        <Select value={selectedBatchId ?? ""} disabled={locked || Boolean(pendingCreate)} onChange={event => selectContext(event.target.value || undefined)}>
          <option value="">New repair proposal</option>
          {selectedBatchId && !batches.some(row => row.batchId === selectedBatchId) ? <option value={selectedBatchId}>{selectedBatchId}</option> : null}
          {batches.map(row => <option key={row.batchId} value={row.batchId}>{row.cohort.sourceSystem} · {row.items.length} records · {formatTimestamp(row.createdAtUtc)} · {row.batchId}</option>)}
        </Select>
      </label>
      <Button variant="outline" disabled={locked || Boolean(pendingCreate)} onClick={() => setRefreshKey(current => current + 1)}>Refresh candidates and batches</Button>
      {selectedBatchId ? <Button variant="outline" disabled={busy !== null || batchLoading} onClick={() => { setError(""); setBatchRefreshKey(current => current + 1); }}>Refresh retained batch</Button> : null}
    </div>
    <p className="text-xs text-muted-foreground">The recent list is bounded. Older batches remain available through their retained rate-repair links.</p>
    {pendingCreate ? <StatusBanner tone="warning" title="Frozen save awaiting confirmation" detail={<div className="space-y-2"><p>{pendingCreate.items.length} records · request {pendingCreate.requestId}. Inputs are locked until this exact proposal is recovered.</p>
      <Button disabled={busy !== null} onClick={() => void freeze(pendingCreate)}>Retry same frozen batch</Button>
      <Button variant="outline" disabled={busy !== null} onClick={() => { selectContext(pendingCreate.requestId); setBatchRefreshKey(current => current + 1); }}>Load retained result</Button></div>} /> : null}
    {batchLoading ? <p role="status">Loading the retained batch and its per-record outcomes…</p> : null}
    {selectedBatchId ? batch && batch.batchId === selectedBatchId ? <RateRepairBatchReview batch={batch} busy={busy} needsRefresh={needsRefresh} onCommand={(name, input) => void command(name, input)} /> : null
      : discovery ? <>
        <p className="text-sm">Source scope: {discovery.scopeLabel}. {discovery.candidates.length} distinct returned candidates from {discovery.scannedCount} source rows examined across {pageCount} {pageCount === 1 ? "page" : "pages"}; overlapping pages may repeat source rows.</p>
        <p className="text-xs text-muted-foreground">Live observations fetched from <TimeText value={discovery.asOfUtc} /> through <TimeText value={lastPageAt} />. This paged scan is not an inventory snapshot.</p>
        {discovery.nextSkip !== null ? <p role="status" className="text-sm text-warning">More source rows are available. These counts cover only the pages fetched.</p>
          : discovery.isComplete ? <p role="status" className="text-sm">The current source scan reached its end.</p> : <p role="status" className="text-sm text-warning">Source coverage is incomplete. Missing or unresolved projections remain unavailable, not zero.</p>}
        {scanNotice ? <StatusBanner tone="warning" title="Discovery changed" detail={scanNotice} role="alert" /> : null}
        {discovery.issues.length ? <StatusBanner tone="warning" title="Source gaps remain" detail={<ul>{discovery.issues.map((issue, index) => <li key={issue.code + ":" + issue.securityId + ":" + index}>{issue.code}: {issue.message}{issue.securityId ? " · " + issue.securityId : ""}</li>)}</ul>} /> : null}
        <RateRepairSelection key={discovery.asOfUtc + ":" + selectionGeneration} candidates={discovery.candidates} action={discovery.actions.find(action => action.command.toLowerCase() === "create")}
          disabled={locked || Boolean(pendingCreate)} initialSearch={initialSearch} onCreate={input => void freeze(input)} />
        {discovery.nextSkip !== null ? <Button variant="outline" disabled={locked || Boolean(pendingCreate)} busy={pageLoading} onClick={() => void loadMore()}>Load more source records</Button> : null}
      </> : null}
  </div>;
}
