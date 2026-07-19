import { RefreshCw, ShieldAlert } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { PanelSurface } from "@/components/ui/panel-surface";
import { certifyMarginSnapshot, getMarginControlCenter } from "@/lib/api";
import type { MarginControlAccount, MarginControlCenter } from "@/types";

export function MarginControlCenterScreen() {
  const [data, setData] = useState<MarginControlCenter | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      setData(await getMarginControlCenter({ signal }));
    } catch (reason) {
      if (!signal?.aborted) setError(reason instanceof Error ? reason.message : "Margin control data could not be loaded.");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  return (
    <section className="flex flex-col gap-4" aria-label="Margin Control Center">
      <PanelSurface className="flex flex-wrap items-start justify-between gap-3 p-4">
        <div>
          <div className="flex items-center gap-2">
            <ShieldAlert className="h-4 w-4 text-primary" aria-hidden="true" />
            <h1 className="text-base font-semibold text-foreground">Margin Control Center</h1>
          </div>
          <p className="mt-1 max-w-3xl text-xs leading-5 text-muted-foreground">
            Compare broker-reported margin to Meridian shadow estimates across accounts and primes. Provider figures
            remain authoritative; this workspace never liquidates positions or changes orders.
          </p>
        </div>
        <Button size="sm" variant="outline" onClick={() => void load()} busy={loading} busyLabel="Refreshing…">
          <RefreshCw className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
          Refresh
        </Button>
      </PanelSurface>

      {error ? (
        <div className="rounded-[2px] border border-destructive/40 bg-destructive/5 p-3 text-xs text-destructive" role="alert">
          {error}
        </div>
      ) : null}

      <SummaryCards data={data} loading={loading} />

      {data ? (
        <>
          <div className="rounded-[2px] border border-amber-300/70 bg-amber-50 p-3 text-xs leading-5 text-amber-950">
            {data.authorityNote} <span className="font-medium">Next:</span> {data.nextAction}
          </div>
          <PrimeTable data={data} />
          <AlertPanel data={data} />
          <div className="flex flex-col gap-3">
            {data.accounts.map((account) => <AccountMarginCard key={`${account.providerId}:${account.accountId}`} account={account} onCertified={() => void load()} />)}
          </div>
        </>
      ) : null}
    </section>
  );
}

function SummaryCards({ data, loading }: { data: MarginControlCenter | null; loading: boolean }) {
  const items = [
    ["Providers / primes", data?.providerCount],
    ["Accounts", data?.accountCount],
    ["Intraday provisional", data?.provisionalAccountCount],
    ["EOD certification candidates", data?.endOfDayCertificationCandidateCount]
  ] as const;
  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" aria-label="Margin control summary">
      {items.map(([label, value]) => (
        <Card key={label}>
          <CardContent className="p-4">
            <p className="text-[0.6875rem] font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
            <p className="mt-2 font-mono text-xl font-semibold tabular-nums">{loading ? "—" : value ?? 0}</p>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

function PrimeTable({ data }: { data: MarginControlCenter }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Multi-prime rollup</CardTitle>
        <CardDescription>Account totals use the most recent retained connector evidence for each provider/account pair.</CardDescription>
      </CardHeader>
      <CardContent className="overflow-x-auto p-0">
        <table className="w-full min-w-[720px] text-xs">
          <thead className="border-b border-border bg-muted/30 text-left text-muted-foreground">
            <tr><Th>Provider</Th><Th>Accounts</Th><Th>Equity</Th><Th>Provider maintenance</Th><Th>Provider excess</Th><Th>Critical</Th></tr>
          </thead>
          <tbody>
            {data.primeSummaries.map((prime) => (
              <tr key={prime.providerId} className="border-b border-border/60 last:border-0">
                <Td className="font-medium">{prime.providerId}</Td>
                <Td>{prime.accountCount}</Td>
                <Td>{money(prime.totalEquity)}</Td>
                <Td>{money(prime.providerMaintenanceMargin)}</Td>
                <Td>{money(prime.providerExcessLiquidity)}</Td>
                <Td>{prime.criticalAccountCount}</Td>
              </tr>
            ))}
            {data.primeSummaries.length === 0 ? <tr><Td colSpan={6}>No connector account snapshots are retained yet.</Td></tr> : null}
          </tbody>
        </table>
      </CardContent>
    </Card>
  );
}

function AlertPanel({ data }: { data: MarginControlCenter }) {
  if (data.alerts.length === 0) return null;
  return (
    <Card>
      <CardHeader><CardTitle>Attention queue</CardTitle><CardDescription>Provider deficits and restrictions rank ahead of diagnostic variance.</CardDescription></CardHeader>
      <CardContent className="flex flex-col gap-2 p-3">
        {data.alerts.map((alert, index) => (
          <div key={`${alert.providerId}:${alert.accountId}:${alert.code}:${index}`} className="rounded-[2px] border border-border p-3 text-xs">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={alert.severity === "Critical" ? "danger" : alert.severity === "Warning" ? "warning" : "outline"}>{alert.severity}</Badge>
              <span className="font-mono text-[0.6875rem] text-muted-foreground">{alert.code}</span>
              <span className="font-medium">{alert.providerId} · {alert.accountId}</span>
            </div>
            <p className="mt-2">{alert.message}</p>
            <p className="mt-1 text-muted-foreground">{alert.suggestedAction}</p>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

function AccountMarginCard({ account, onCertified }: { account: MarginControlAccount; onCertified: () => void }) {
  const [note, setNote] = useState("");
  const [certifying, setCertifying] = useState(false);
  const [certificationError, setCertificationError] = useState<string | null>(null);
  const certify = async () => {
    setCertifying(true);
    setCertificationError(null);
    try {
      await certifyMarginSnapshot({ providerId: account.providerId, accountId: account.accountId, asOf: account.asOf, evidencePath: account.evidencePath, note });
      setNote("");
      onCertified();
    } catch (reason) {
      setCertificationError(reason instanceof Error ? reason.message : "Certification failed.");
    } finally {
      setCertifying(false);
    }
  };
  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <CardTitle>{account.providerId} · {account.accountId}</CardTitle>
            <CardDescription>{account.marginRegime} · as of {dateTime(account.asOf)} · evidence {account.evidencePath}</CardDescription>
          </div>
          <div className="flex flex-wrap gap-1.5">
            <Badge variant={account.riskLevel === "Critical" ? "danger" : account.riskLevel === "Warning" ? "warning" : "success"}>{account.riskLevel}</Badge>
            <Badge variant="outline">{account.snapshotPhase}</Badge>
            <Badge variant="outline">{account.certificationState}</Badge>
          </div>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 p-4">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Metric label="Equity" value={money(account.equity, account.currency)} />
          <Metric label="Buying power" value={money(account.buyingPower, account.currency)} />
          <Metric label="Provider maintenance" value={money(account.providerMaintenanceMargin, account.currency)} />
          <Metric label="Provider excess" value={money(account.providerExcessLiquidity, account.currency)} />
        </div>
        <div className="grid gap-3 lg:grid-cols-2">
          <Comparison label="Provider reported" initial={account.providerInitialMargin} maintenance={account.providerMaintenanceMargin} excess={account.providerExcessLiquidity} currency={account.currency} />
          <Comparison label={account.shadowModelName} initial={account.shadowInitialMargin} maintenance={account.shadowMaintenanceMargin} excess={account.shadowExcessLiquidity} currency={account.currency} />
        </div>
        <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
          <span>Maintenance variance: <strong className="text-foreground">{money(account.maintenanceVariance, account.currency)}</strong></span>
          <span>Options lifecycle: <strong className="text-foreground">{account.optionLifecycleEventCount}</strong></span>
          <span>Borrow positions: <strong className="text-foreground">{account.borrowPositionCount}</strong></span>
          <span>Tax lots: <strong className="text-foreground">{account.taxLotCount}</strong></span>
          <span>Activity complete: <strong className="text-foreground">{account.activityComplete == null ? "Not reported" : account.activityComplete ? "Yes" : "No"}</strong></span>
        </div>
        {account.restrictions.length ? <div className="rounded-[2px] border border-destructive/30 bg-destructive/5 p-2 text-xs">{account.restrictions.join("; ")}</div> : null}
        {account.certificationState === "AwaitingOperatorCertification" ? (
          <div className="flex flex-col gap-2 rounded-[2px] border border-border bg-muted/15 p-3 sm:flex-row sm:items-end">
            <label className="min-w-0 flex-1 text-xs font-medium">
              Certification note
              <Input className="mt-1" value={note} onChange={(event) => setNote(event.target.value)} placeholder="Explain the EOD evidence reviewed" />
            </label>
            <Button size="sm" onClick={() => void certify()} disabled={!note.trim()} busy={certifying} busyLabel="Certifying…">Certify EOD snapshot</Button>
            {certificationError ? <p className="text-xs text-destructive" role="alert">{certificationError}</p> : null}
          </div>
        ) : account.certificationState === "Certified" ? (
          <div className="rounded-[2px] border border-emerald-300/60 bg-emerald-50 p-3 text-xs text-emerald-950">
            Certified by {account.certifiedBy ?? "operator"} at {account.certifiedAtUtc ? dateTime(account.certifiedAtUtc) : "recorded time"}. {account.certificationNote}
          </div>
        ) : null}
        <div className="overflow-x-auto">
          <table className="w-full min-w-[760px] text-xs">
            <thead className="border-y border-border bg-muted/30 text-left text-muted-foreground"><tr><Th>Symbol</Th><Th>Security master</Th><Th>Quantity</Th><Th>Market value</Th><Th>Shadow initial</Th><Th>Shadow maintenance</Th><Th>Borrow</Th><Th>Rate</Th><Th>Lots / options</Th></tr></thead>
            <tbody>
              {account.positionContributions.map((position) => (
                <tr key={position.symbol} className="border-b border-border/60">
                  <Td className="font-medium">{position.symbol}</Td><Td title={position.securityMasterSource ?? "ProviderStatementSymbolUnresolved"}>{position.securityId ?? "Unresolved provider symbol"}</Td><Td>{number(position.quantity)}</Td><Td>{money(position.marketValue, account.currency)}</Td>
                  <Td>{money(position.shadowInitialMargin, account.currency)}</Td><Td>{money(position.shadowMaintenanceMargin, account.currency)}</Td>
                  <Td>{position.borrowStatus ?? "—"}</Td><Td>{position.borrowRate == null ? "—" : `${number(position.borrowRate)}%`}</Td><Td>{position.taxLotCount} / {position.optionLifecycleEventCount}</Td>
                </tr>
              ))}
              {account.positionContributions.length === 0 ? <tr><Td colSpan={9}>No canonical position rows were retained for this account.</Td></tr> : null}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}

function Comparison({ label, initial, maintenance, excess, currency }: { label: string; initial: number | null; maintenance: number | null; excess: number | null; currency: string }) {
  return <div className="rounded-[2px] border border-border p-3"><p className="text-xs font-semibold">{label}</p><dl className="mt-2 grid grid-cols-3 gap-2 text-xs"><div><dt className="text-muted-foreground">Initial</dt><dd className="mt-1 font-mono">{money(initial, currency)}</dd></div><div><dt className="text-muted-foreground">Maintenance</dt><dd className="mt-1 font-mono">{money(maintenance, currency)}</dd></div><div><dt className="text-muted-foreground">Excess</dt><dd className="mt-1 font-mono">{money(excess, currency)}</dd></div></dl></div>;
}

function Metric({ label, value }: { label: string; value: string }) { return <div><p className="text-[0.6875rem] uppercase tracking-wide text-muted-foreground">{label}</p><p className="mt-1 font-mono text-sm font-semibold tabular-nums">{value}</p></div>; }
function Th(props: React.ThHTMLAttributes<HTMLTableCellElement>) { return <th className="px-3 py-2 font-medium" {...props} />; }
function Td({ className = "", ...props }: React.TdHTMLAttributes<HTMLTableCellElement>) { return <td className={`px-3 py-2 ${className}`} {...props} />; }
function number(value: number) { return new Intl.NumberFormat(undefined, { maximumFractionDigits: 4 }).format(value); }
function money(value: number | null | undefined, currency = "USD") { return value == null ? "—" : new Intl.NumberFormat(undefined, { style: "currency", currency, maximumFractionDigits: 2 }).format(value); }
function dateTime(value: string) { const parsed = new Date(value); return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString(); }
