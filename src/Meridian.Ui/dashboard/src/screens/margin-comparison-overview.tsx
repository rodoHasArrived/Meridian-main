import { useState } from "react";
import { Select } from "@/components/ui/select";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { formatCurrency } from "@/lib/format";
import type { MarginControlAccount } from "@/types";

export function marginAccountKey(account: MarginControlAccount): string {
  return `${account.providerId}:${account.accountId}`;
}
export function marginAccountAnchor(account: MarginControlAccount): string {
  return `margin-evidence-${encodeURIComponent(marginAccountKey(account))}`;
}
function reported(value: number | null | undefined): value is number {
  return typeof value === "number" && Number.isFinite(value);
}
function amount(value: number | null | undefined, currency: string): string {
  return formatCurrency(value, { currency, fallback: "Not reported" });
}

export function MarginComparisonOverview({ accounts, selectedId, onSelect }: {
  accounts: MarginControlAccount[];
  selectedId: string | null;
  onSelect: (account: MarginControlAccount) => void;
}) {
  const [metric, setMetric] = useState<"maintenance" | "initial">("maintenance");
  const currencies = [...new Set(accounts.map((account) => account.currency || "Unknown currency"))];
  return <section className="space-y-3 rounded-[2px] border border-border p-4" aria-label="Margin disagreement and liquidity headroom">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <h2 className="text-sm font-semibold">Margin disagreement and headroom</h2>
      <label className="flex items-center gap-2 text-xs">Requirement
        <Select value={metric} aria-label="Margin comparison requirement" onChange={(event) => setMetric(event.target.value as "maintenance" | "initial")}>
          <option value="maintenance">Maintenance margin</option><option value="initial">Initial margin</option>
        </Select>
      </label>
    </div>
    <p className="text-xs text-muted-foreground">● Broker-reported requirement · ◇ Meridian diagnostic estimate. Broker figures remain authoritative. Excess-liquidity bars show broker-reported headroom against a zero deficit boundary. Select an account for evidence.</p>
    {currencies.map((currency) => {
      const rows = accounts.filter((account) => (account.currency || "Unknown currency") === currency);
      const value = (account: MarginControlAccount, source: "broker" | "meridian") => metric === "maintenance"
        ? source === "broker" ? account.providerMaintenanceMargin : account.shadowMaintenanceMargin
        : source === "broker" ? account.providerInitialMargin : account.shadowInitialMargin;
      const knownCurrency = /^[A-Z]{3}$/.test(currency);
      const requirements = rows.flatMap((account) => [value(account, "broker"), value(account, "meridian")]).filter(reported);
      const minMargin = Math.min(0, ...requirements);
      const maxMargin = Math.max(1, ...requirements);
      const position = (requirement: number) => 12 + (requirement - minMargin) / (maxMargin - minMargin) * 276;
      const maxExcess = Math.max(1, ...rows.map((account) => account.providerExcessLiquidity).filter(reported).map(Math.abs));
      const columns: DenseDataTableColumn<MarginControlAccount>[] = [
        { id: "account", label: "Account / source state", className: "min-w-[240px]", render: (account) =>
          <button type="button" className="text-left underline decoration-border underline-offset-4 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary" aria-pressed={selectedId === marginAccountKey(account)} onClick={() => onSelect(account)}>
            <span className="font-semibold">{account.providerId} · {account.accountId}</span><span className="block text-muted-foreground">{account.snapshotPhase} · {account.certificationState} · {account.riskLevel}</span>
            <span className="block font-mono text-xs text-muted-foreground">As of {account.asOf}</span>
          </button>
        },
        { id: "requirement", label: `${metric === "maintenance" ? "Maintenance" : "Initial"} requirement`, className: "w-[35%] min-w-[240px]", render: (account) => {
          const broker = knownCurrency ? value(account, "broker") : null;
          const estimate = knownCurrency ? value(account, "meridian") : null;
          return <div>
            <svg viewBox="0 0 300 30" className="h-8 w-full" role="img" aria-label={`Broker ${amount(broker, currency)}; Meridian ${amount(estimate, currency)}`}>
              <line x1="12" x2="288" y1="15" y2="15" stroke="var(--chart-grid, #E2DED5)" />
              {reported(broker) && reported(estimate) ? <line x1={position(broker)} x2={position(estimate)} y1="15" y2="15" stroke="var(--chart-axis, #606870)" /> : null}
              {reported(broker) ? <circle cx={position(broker)} cy="15" r="5" fill="hsl(var(--primary, 208 17% 19%))" /> : null}
              {reported(estimate) ? <path d={`M${position(estimate)},8l7,7l-7,7l-7,-7z`} fill="none" stroke="var(--chart-axis, #606870)" strokeWidth="2" /> : null}
            </svg>
            <span className="block font-mono">Broker {amount(broker, currency)}</span><span className="block font-mono text-muted-foreground">Meridian {amount(estimate, currency)}</span>
          </div>;
        } },
        { id: "excess", label: "Broker excess liquidity · zero boundary", className: "w-[30%] min-w-[220px]", render: (account) => {
          const excess = knownCurrency ? account.providerExcessLiquidity : null;
          return <div>
            <svg viewBox="0 0 300 30" className="h-8 w-full" role="img" aria-label={`Broker excess liquidity ${amount(excess, currency)}${reported(excess) && excess < 0 ? ", deficit" : ""}; zero at center`}>
              <line x1="12" x2="288" y1="15" y2="15" stroke="var(--chart-grid, #E2DED5)" />
              {reported(excess) ? <rect x={excess < 0 ? 150 - Math.abs(excess) / maxExcess * 138 : 150} y="8" width={Math.abs(excess) / maxExcess * 138} height="14" fill={excess < 0 ? "var(--chart-drawdown, #B54B42)" : "var(--chart-equity, #2E7655)"} /> : null}
              <line x1="150" x2="150" y1="3" y2="27" stroke="currentColor" strokeWidth="1.5" />
            </svg>
            <span className="block font-mono">{amount(excess, currency)}{reported(excess) && excess < 0 ? " · Deficit" : ""}</span>
          </div>;
        } }
      ];
      return <div key={currency} className="space-y-2" role="group" aria-label={`Margin comparisons in ${currency}`}>
        <h3 className="text-xs font-semibold">{currency} · independent requirement and headroom scales</h3>
        <DenseDataTable columns={columns} rows={rows} getRowId={marginAccountKey} ariaLabel={`Margin requirements and liquidity in ${currency}`} emptyText="No retained account snapshots in this currency." tableClassName="min-w-[800px]" />
      </div>;
    })}
    {accounts.length === 0 ? <p className="text-xs text-muted-foreground">No retained account snapshots are available for comparison.</p> : null}
  </section>;
}
