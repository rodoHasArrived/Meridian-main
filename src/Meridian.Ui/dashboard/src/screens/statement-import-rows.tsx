import { useEffect, useRef, useState } from "react";
import { apiGetJson } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { FinancialValue } from "@/components/meridian/financial-value";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import type { StatementImportCommitResult, StatementImportOutcomePage, StatementImportPreview, StatementImportRowDisposition, StatementImportRowOutcome, StatementImportRowPage, StatementImportReviewRow } from "@/types";

import { VALUE_STATE_LABELS } from "@/lib/format";
const PAGE_SIZE = 100;
function pagePath(route: string, offset: number) {
  // Retained row links must stay on the authenticated statement-review API.
  if (!/^\/api\/workstation\/reconciliation\/statement-imports\/reviews\/[A-Za-z0-9_-]+\/(rows|outcomes)(?:\?|$)/.test(route)) throw new Error("Invalid retained statement row route.");
  const url = new URL(route, "http://meridian.local");
  url.searchParams.set("offset", String(offset)); url.searchParams.set("limit", String(PAGE_SIZE));
  return `${url.pathname}${url.search}`;
}

export function StatementImportRows({ preview, decisions, onChange, disabled }: {
  preview: StatementImportPreview;
  decisions: Record<string, StatementImportRowDisposition>;
  onChange: (decision: StatementImportRowDisposition | null, rowId: string) => void;
  disabled: boolean;
}) {
  const [page, setPage] = useState<StatementImportRowPage>({ rows: preview.rows ?? [], totalRows: preview.totalRows ?? preview.recordCount, offset: 0, limit: PAGE_SIZE });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const revision = useRef(0);
  useEffect(() => () => { revision.current++; }, []);
  const load = async (offset: number) => {
    if (!preview.rowsRoute || busy) return;
    const ticket = ++revision.current; setBusy(true); setError(null);
    try {
      const next = await apiGetJson<StatementImportRowPage>(pagePath(preview.rowsRoute, offset), { allowDevelopmentFallback: false });
      if (revision.current === ticket) setPage(next);
    } catch (cause) { if (revision.current === ticket) setError(describeApiError(cause, "Retained rows could not be loaded.").summary); }
    finally { if (revision.current === ticket) setBusy(false); }
  };
  return <section className="rounded border border-border p-3" aria-labelledby="statement-row-review-heading">
    <h3 id="statement-row-review-heading" className="font-semibold">Review record rows</h3>
    <p className="mt-1 text-xs text-muted-foreground">Row decisions belong to this retained preview. Correct mappings above; exclusions require a reason. Original source evidence is retained. A normalized record index is not a physical source line.</p>
    {!preview.reviewId ? <p role="status">Retained row review is unavailable. Refresh the preview.</p> : null}
    {preview.reviewId && page.rows.length === 0 ? <p role="status">No normalized record rows are available for review. Source parsing issues remain blocking.</p> : null}
    <div className="mt-3 overflow-x-auto" role="region" tabIndex={0} aria-label="Statement record decisions">
      <DenseDataTable<StatementImportReviewRow> ariaLabel="Statement record decisions" rows={page.rows} getRowId={(row) => row.rowId} maxVisibleRows={null} emptyText="No normalized record rows." columns={[
        { id: "record", label: "Record / source line", render: (row) => <>{row.normalizedIndex + 1}<span className="block text-xs text-muted-foreground">{row.sourceRowNumber == null ? "Source line unavailable" : `Source line ${row.sourceRowNumber}`}</span></> },
        { id: "account", label: "Account / symbol", render: (row) => <>{row.record.account}<span className="block">{row.record.symbol}</span>{row.issues.map((issue, index) => <p key={`${issue.code}-${index}`} className="text-xs">{issue.message}</p>)}</> },
        { id: "quantity", label: "Quantity", numeric: true, render: (row) => <FinancialValue value={row.record.quantityExact ?? row.record.quantity} unit="units" condition="provisional" reason="Parsed source value; import has not committed." decimals={6} /> },
        { id: "amount", label: "Cash amount", numeric: true, render: (row) => <FinancialValue value={row.record.cashAmountExact ?? row.record.cashAmount} currency={row.record.currency ?? undefined} condition="provisional" reason="Parsed source amount; import has not committed." /> },
        { id: "decision", label: "Decision", className: "min-w-44", render: (row) => {
          const decision = decisions[row.rowId];
          return <><Select aria-label={`Decision for record ${row.normalizedIndex + 1}`} disabled={disabled || busy || !preview.reviewId} value={decision?.disposition ?? "Import"} onChange={(event) => onChange(event.target.value === "Import" ? null : { rowId: row.rowId, disposition: event.target.value as StatementImportRowDisposition["disposition"], reason: decision?.reason ?? "" }, row.rowId)}>
              <option value="Import">Import normally</option><option value="Exclude" disabled={!row.canExclude}>Exclude with reason</option><option value="AcceptAsBreak" disabled={!row.canAcceptAsBreak}>Accept as a break</option>
            </Select>{!row.canExclude && row.exclusionDisabledReason ? <p className="text-xs">{row.exclusionDisabledReason}</p> : null}{!row.canAcceptAsBreak ? <p className="mt-1 text-xs text-muted-foreground">Accept as a break requires server-authorized row casework.</p> : null}</>;
        } },
        { id: "reason", label: "Reason", className: "min-w-44", render: (row) => {
          const decision = decisions[row.rowId];
          return decision ? <textarea aria-label={`Reason for record ${row.normalizedIndex + 1}`} required disabled={disabled || busy} className="w-full rounded border border-border bg-background p-1" value={decision.reason} onChange={(event) => onChange({ ...decision, reason: event.target.value }, row.rowId)} /> : VALUE_STATE_LABELS.unavailable;
        } }
      ]} />
    </div>
    {error ? <p role="alert">{error}</p> : null}
    <RowPaging offset={page.offset} count={page.rows.length} total={page.totalRows} busy={busy} available={Boolean(preview.rowsRoute)} onPage={load} />
    <p className="mt-2 text-xs">{Object.keys(decisions).length} row decisions across all pages.</p>
  </section>;
}

export function StatementImportOutcomes({ result }: { result: StatementImportCommitResult }) {
  const [page, setPage] = useState<StatementImportOutcomePage>({ rowOutcomes: result.rowOutcomes ?? [], totalRows: result.totalRows ?? result.rowOutcomes?.length ?? 0, offset: 0, limit: PAGE_SIZE });
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string | null>(null);
  const revision = useRef(0); useEffect(() => () => { revision.current++; }, []);
  const load = async (offset: number) => {
    if (!result.outcomesRoute || busy) return;
    const ticket = ++revision.current; setBusy(true); setError(null);
    try { const next = await apiGetJson<StatementImportOutcomePage>(pagePath(result.outcomesRoute, offset), { allowDevelopmentFallback: false }); if (ticket === revision.current) setPage(next); }
    catch (cause) { if (ticket === revision.current) setError(describeApiError(cause, "Row outcomes could not be loaded.").summary); }
    finally { if (ticket === revision.current) setBusy(false); }
  };
  const ordered = [...page.rowOutcomes].sort((a, b) => Number(/fail|reject/i.test(b.status)) - Number(/fail|reject/i.test(a.status)) || a.normalizedIndex - b.normalizedIndex);
  return <section aria-labelledby="statement-row-outcomes-heading"><h3 id="statement-row-outcomes-heading" className="font-semibold">Row outcomes</h3>
    <DenseDataTable<StatementImportRowOutcome> ariaLabel="Statement row outcomes" rows={ordered} getRowId={(row) => row.rowId} maxVisibleRows={null} emptyText="No retained row outcomes." columns={[
      { id: "record", label: "Record", render: (row) => row.normalizedIndex + 1 },
      { id: "outcome", label: "Outcome", render: (row) => row.status },
      { id: "decision", label: "Decision / reason", render: (row) => <>{row.disposition}{row.reason ? ` · ${row.reason}` : ""}</> },
      { id: "cases", label: "Case references", render: (row) => row.caseIds?.join(", ") || "No row-linked case reference" }
    ]} />
    {!page.totalRows ? <p>Retained row outcomes are unavailable for this legacy import.</p> : null}{error ? <p role="alert">{error}</p> : null}
    <RowPaging offset={page.offset} count={page.rowOutcomes.length} total={page.totalRows} busy={busy} available={Boolean(result.outcomesRoute)} onPage={load} />
  </section>;
}

function RowPaging({ offset, count, total, busy, available, onPage }: { offset: number; count: number; total: number; busy: boolean; available: boolean; onPage: (offset: number) => void }) {
  return <div className="mt-2 flex flex-wrap items-center gap-2 text-xs"><span role="status" aria-live="polite">{total ? `${offset + 1}–${offset + count} of ${total}` : "0 records"}</span><Button size="sm" variant="outline" disabled={busy || !available || offset === 0} onClick={() => onPage(Math.max(0, offset - PAGE_SIZE))}>Previous records</Button><Button size="sm" variant="outline" disabled={busy || !available || offset + count >= total} onClick={() => onPage(offset + PAGE_SIZE)}>Next records</Button></div>;
}
