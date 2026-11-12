import { useEffect, useState } from "react";
import { ChartCard } from "@/components/charts/ChartCard";
import { EquityCurve } from "@/components/charts/EquityCurve";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { Button } from "@/components/ui/button";
import { getAccountBalanceHistory, type AccountBalanceHistory } from "@/lib/api/account-balance-history.api";
import { formatNumber } from "@/lib/format";

export function AccountBalanceHistoryChart({ accountId }: { accountId: string | null | undefined }) {
  const [result, setResult] = useState<{ accountId: string; history?: AccountBalanceHistory; error?: string } | null>(null);
  const [basis, setBasis] = useState("");
  const [retry, setRetry] = useState(0);
  const canonicalId = accountId && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(accountId) ? accountId : null;
  const active = result?.accountId === canonicalId ? result : null;
  useEffect(() => {
    if (!canonicalId) return;
    const controller = new AbortController();
    setResult(null);
    void getAccountBalanceHistory(canonicalId, { signal: controller.signal }).then(
      (history) => { if (!controller.signal.aborted) setResult({ accountId: canonicalId, history }); },
      () => { if (!controller.signal.aborted) setResult({ accountId: canonicalId, error: "Retained account balance history is unavailable or access is not permitted." }); }
    );
    return () => controller.abort();
  }, [canonicalId, retry]);
  const points = active?.history?.points ?? [];
  const bases = [...new Set(points.map((point) => JSON.stringify([point.currency, point.source])))];
  const selectedBasis = bases.includes(basis) ? basis : bases[0];
  const selected = points.filter((point) => JSON.stringify([point.currency, point.source]) === selectedBasis);
  const duplicateDates = new Set(selected.map((point) => point.asOfDate)).size !== selected.length;
  const chartable = selected.length >= 2 && !duplicateDates && selected.every((point) => Number.isFinite(Number(point.cashBalance)));
  const currency = selected[0]?.currency;
  const completeSecurities = selected.length > 0 && selected.every((point) => point.securitiesMarketValue !== null && Number.isFinite(Number(point.securitiesMarketValue)));
  return <section aria-label="Retained account balance history" className="space-y-3">
    <h3 className="text-sm font-semibold">Account balance history</h3>
    {!canonicalId ? <p className="text-xs text-muted-foreground">Select an account with a retained account identity to inspect dated balances. Run equity is shown separately.</p>
      : active?.error ? <div role="status" className="text-sm"><p>{active.error}</p><Button size="sm" variant="outline" onClick={() => setRetry((value) => value + 1)}>Retry account history</Button></div>
      : !active?.history ? <p role="status" className="text-xs text-muted-foreground">Loading retained account balances…</p>
      : points.length === 0 ? <p role="status" className="text-xs text-muted-foreground">No dated account balances have been retained.</p>
      : <>
        <label className="block text-xs">Currency and source <select aria-label="Account history basis" value={selectedBasis} onChange={(event) => setBasis(event.target.value)} className="ml-2 border border-border bg-background p-2">
          {bases.map((value) => { const [unit, source] = JSON.parse(value) as string[]; return <option key={value} value={value}>{unit} · {source}</option>; })}
        </select></label>
        {chartable ? <ChartCard title={`Account balances · ${currency}`} subtitle={`${selected[0].source} · ${selected[0].asOfDate} to ${selected.at(-1)?.asOfDate}`} height={240}>
          <EquityCurve labels={selected.map((point) => point.asOfDate)} valueFmt={(value) => formatNumber(value)} series={[
            { label: `Cash · ${currency}`, color: "var(--chart-equity, #2E7655)", points: selected.map((point) => Number(point.cashBalance)) },
            ...(completeSecurities ? [{ label: `Securities market value · ${currency}`, color: "var(--chart-primary, #29323A)", points: selected.map((point) => Number(point.securitiesMarketValue)), area: false }] : [])
          ]} />
        </ChartCard> : <p role="status" className="text-xs text-muted-foreground">{duplicateDates ? "Multiple retained observations share a date for this source. Inspect the records before choosing a valuation authority." : "At least two usable dated observations are needed for a trend."}</p>}
        <p className="text-xs text-muted-foreground">Cash and securities remain separate retained components. No net-equity total or cross-currency sum is inferred. {!completeSecurities ? "Some securities values are unavailable; no securities trend is drawn." : ""} {active.history.isTruncated ? "Only the latest 500 snapshots are shown." : ""}</p>
        <details className="workspace-task-disclosure"><summary>View exact retained balance records</summary>
          <TableWorkbench workbenchId="account-balance-history" sourceKey={`${canonicalId}:${selectedBasis}`} sourceDescription="Exact retained balances for the selected account, currency and source; cash and securities remain separate." completePopulation={!active.history.isTruncated} tableId="account-balance-history" ariaLabel="Account balance history records" rows={selected} getRowId={(point) => point.snapshotId}
            columns={[{ id: "date", label: "As of", render: (point) => point.asOfDate, exportValue: (point) => point.asOfDate }, { id: "cash", label: `Cash · ${currency}`, render: (point) => point.cashBalance, exportValue: (point) => point.cashBalance },
              { id: "securities", label: `Securities · ${currency}`, render: (point) => point.securitiesMarketValue ?? "Unavailable", exportValue: (point) => point.securitiesMarketValue }, { id: "recorded", label: "Recorded at UTC", render: (point) => point.recordedAtUtc, exportValue: (point) => point.recordedAtUtc }]}
            emptyText="No retained balances for this basis." />
        </details>
      </>}
  </section>;
}
