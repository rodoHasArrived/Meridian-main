import { TableWorkbench } from "@/components/meridian/table-workbench";
import { formatNumber, formatCurrency } from "@/lib/format";
import { formatDateTimeLabel } from "@/screens/accounting-screen.formatting";
import { RefreshCw, ShieldAlert } from "lucide-react";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { PanelSurface } from "@/components/ui/panel-surface";
import { certifyMarginSnapshot, getMarginControlCenter } from "@/lib/api";
import type { MarginControlAccount, MarginControlCenter } from "@/types";
import { MarginComparisonOverview, marginAccountAnchor, marginAccountKey } from "@/screens/margin-comparison-overview";
import { cn } from "@/lib/utils";
import { usesTrackedCaps } from "@/design-system/label-case";

export function MarginControlCenterScreen() {
  const [data, setData] = useState<MarginControlCenter | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const loadGeneration = useRef(0);

  const load = useCallback(async (signal?: AbortSignal) => {
    const generation = ++loadGeneration.current;
    setLoading(true);
    setError(null);
    try {
      const next = await getMarginControlCenter({ signal });
      if (!signal?.aborted && generation === loadGeneration.current) setData(next);
    } catch (reason) {
      if (!signal?.aborted && generation === loadGeneration.current) setError(reason instanceof Error ? reason.message : "Margin control data could not be loaded.");
    } finally {
      if (!signal?.aborted && generation === loadGeneration.current) setLoading(false);
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
            <h2 className="text-base font-semibold text-foreground">Margin Control Center</h2>
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
          {data ? " Last successfully loaded snapshots remain visible; the refresh failed." : ""}
        </div>
      ) : null}

      <SummaryCards data={data} loading={loading} />

      {data ? (
        <>
          <div className="rounded-[2px] border border-amber-300/70 bg-amber-50 p-3 text-xs leading-5 text-amber-950">
            {data.authorityNote} <span className="font-medium">Next:</span> {data.nextAction}
          </div>
          <MarginComparisonOverview accounts={data.accounts} selectedId={selectedId} onSelect={(account) => {
            setSelectedId(marginAccountKey(account));
            const evidence = document.getElementById(marginAccountAnchor(account));
            evidence?.focus();
            evidence?.scrollIntoView?.({ block: "start", behavior: "auto" });
          }} />
          <PrimeTable data={data} />
          <AlertPanel data={data} />
          <div className="flex flex-col gap-3">
            {data.accounts.map((account) => <div id={marginAccountAnchor(account)} key={marginAccountKey(account)} tabIndex={-1} className="focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary" aria-label={`Margin evidence ${account.providerId} ${account.accountId}`}><AccountMarginCard account={account} onCertified={() => void load()} /></div>)}
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
            <p className={cn("text-[0.6875rem] font-medium text-muted-foreground", usesTrackedCaps(label) && "uppercase tracking-wide")}>{label}</p>
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
        <TableWorkbench<(typeof data.primeSummaries)[number]> workbenchId="margin-prime-summary" sourceKey={JSON.stringify(data.primeSummaries)} rows={data.primeSummaries} columns={[{id:"field-0",label:"Provider",exportValue:prime=>prime.providerId,render:prime=><>{prime.providerId}</>},
{id:"field-1",label:"Accounts",exportValue:prime=>prime.accountCount,render:prime=><>{prime.accountCount}</>},
{id:"field-2",label:"Equity",exportValue:prime=>primeCurrency(prime.providerId, prime.accountCount, data) ? prime.totalEquity : null,render:prime=><>{money(prime.totalEquity, primeCurrency(prime.providerId, prime.accountCount, data))}</>},
{id:"field-3",label:"Provider maintenance",exportValue:prime=>primeCurrency(prime.providerId, prime.accountCount, data) ? prime.providerMaintenanceMargin : null,render:prime=><>{money(prime.providerMaintenanceMargin, primeCurrency(prime.providerId, prime.accountCount, data))}</>},
{id:"field-4",label:"Provider excess",exportValue:prime=>primeCurrency(prime.providerId, prime.accountCount, data) ? prime.providerExcessLiquidity : null,render:prime=><>{money(prime.providerExcessLiquidity, primeCurrency(prime.providerId, prime.accountCount, data))}</>},
{id:"field-5",label:"Critical",exportValue:prime=>prime.criticalAccountCount,render:prime=><>{prime.criticalAccountCount}</>},
{id:"currency",label:"Currency",exportValue:prime=>primeCurrency(prime.providerId, prime.accountCount, data),render:prime=>primeCurrency(prime.providerId, prime.accountCount, data) ?? "No single declared currency"}]} getRowId={prime=>prime.providerId} getSearchText={prime=>prime.providerId} completePopulation sourceDescription="Latest retained provider/account evidence; monetary totals require a complete same-currency population."  ariaLabel="Provider margin account summaries" emptyText="No connector account snapshots are retained yet." />
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
            <Button size="sm" onClick={() => void certify()} disabled={!note.trim()} disabledReason="Enter a certification note first." busy={certifying} busyLabel="Certifying…">Certify EOD snapshot</Button>
            {certificationError ? <p className="text-xs text-destructive" role="alert">{certificationError}</p> : null}
          </div>
        ) : account.certificationState === "Certified" ? (
          <div className="rounded-[2px] border border-emerald-300/60 bg-emerald-50 p-3 text-xs text-emerald-950">
            Certified by {account.certifiedBy ?? "operator"} at {account.certifiedAtUtc ? dateTime(account.certifiedAtUtc) : "recorded time"}. {account.certificationNote}
          </div>
        ) : null}
        <div className="overflow-x-auto">
          <TableWorkbench<(typeof account.positionContributions)[number]> workbenchId="margin-account-positions" sourceKey={JSON.stringify(account.positionContributions)} rows={account.positionContributions} columns={[{id:"field-0",label:"Symbol",exportValue:position=>position.symbol,render:position=><>{position.symbol}</>},
{id:"field-1",label:"Security master",exportValue:position=>position.securityId,render:position=><>{position.securityId ?? "Unresolved provider symbol"}</>},
{id:"field-2",label:"Quantity",exportValue:position=>position.quantity,render:position=><>{number(position.quantity)}</>},
{id:"field-3",label:"Market value",exportValue:position=>position.marketValue,render:position=><>{money(position.marketValue, account.currency)}</>},
{id:"field-4",label:"Shadow initial",exportValue:position=>position.shadowInitialMargin,render:position=><>{money(position.shadowInitialMargin, account.currency)}</>},
{id:"field-5",label:"Shadow maintenance",exportValue:position=>position.shadowMaintenanceMargin,render:position=><>{money(position.shadowMaintenanceMargin, account.currency)}</>},
{id:"field-6",label:"Borrow",exportValue:position=>position.borrowStatus,render:position=><>{position.borrowStatus ?? "—"}</>},
{id:"field-7",label:"Rate",exportValue:position=>position.borrowRate,render:position=><>{position.borrowRate == null ? "—" : `${number(position.borrowRate)}%`}</>},
{id:"field-8",label:"Lots / options",exportValue:position=>`${position.taxLotCount}/${position.optionLifecycleEventCount}`,render:position=><>{position.taxLotCount} / {position.optionLifecycleEventCount}</>}]} getRowId={position=>position.symbol} getSearchText={position=>`${position.symbol} ${position.securityId ?? ""}`} completePopulation sourceDescription="Retained position contributions in the selected account currency." exportContext={{currency:account.currency,asOf:account.asOf,account:account.accountId}} ariaLabel="Retained account margin position contributions" emptyText="No canonical position rows were retained for this account." />
        </div>
      </CardContent>
    </Card>
  );
}

function Comparison({ label, initial, maintenance, excess, currency }: { label: string; initial: number | null; maintenance: number | null; excess: number | null; currency: string }) {
  return <div className="rounded-[2px] border border-border p-3"><p className="text-xs font-semibold">{label}</p><dl className="mt-2 grid grid-cols-3 gap-2 text-xs"><div><dt className="text-muted-foreground">Initial</dt><dd className="mt-1 font-mono">{money(initial, currency)}</dd></div><div><dt className="text-muted-foreground">Maintenance</dt><dd className="mt-1 font-mono">{money(maintenance, currency)}</dd></div><div><dt className="text-muted-foreground">Excess</dt><dd className="mt-1 font-mono">{money(excess, currency)}</dd></div></dl></div>;
}

function Metric({ label, value }: { label: string; value: string }) { return <div><p className={cn("text-[0.6875rem] text-muted-foreground", usesTrackedCaps(label) && "uppercase tracking-wide")}>{label}</p><p className="mt-1 font-mono text-sm font-semibold tabular-nums">{value}</p></div>; }
function number(value: number) { return formatNumber(value, { maximumFractionDigits: 4 }); }
function money(value: number | null | undefined, currency?: string) { return value == null ? "—" : currency ? formatCurrency(value, { currency, maximumFractionDigits: 2 }) : "Currency unavailable"; }
function dateTime(value: string) { return formatDateTimeLabel(value); }

function primeCurrency(providerId: string, expectedCount: number, data: MarginControlCenter): string | undefined {
  const accounts = data.accounts.filter(account => account.providerId === providerId);
  const currencies = new Set(accounts.map(account => account.currency?.trim().toUpperCase()).filter(Boolean));
  return accounts.length === expectedCount && currencies.size === 1 ? [...currencies][0] : undefined;
}
