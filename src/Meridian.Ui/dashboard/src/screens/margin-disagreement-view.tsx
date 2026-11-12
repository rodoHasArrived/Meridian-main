import { useState } from "react";
import type { MarginControlAccount } from "@/types";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { formatNumber } from "@/lib/format";

const finite = (value: number | null): value is number => value !== null && Number.isFinite(value);
export function marginCurrencyGroups(accounts: MarginControlAccount[]) {
  const groups = new Map<string, MarginControlAccount[]>();
  for (const account of accounts) {
    const currency = account.currency?.trim() || "Unspecified currency";
    groups.set(currency, [...(groups.get(currency) ?? []), account]);
  }
  return [...groups].sort(([a], [b]) => a.localeCompare(b));
}

export function marginAccountAnchor(account: MarginControlAccount) {
  return `margin-account-${encodeURIComponent(`${account.providerId}:${account.accountId}`)}`;
}

export function MarginDisagreementView({ accounts }: { accounts: MarginControlAccount[] }) {
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const selected = accounts.find((account) => key(account) === selectedKey);
  return <section aria-label="Margin disagreement and headroom" className="space-y-4 rounded-[2px] border border-border p-4">
    <h3 className="text-sm font-semibold">Margin disagreement and headroom</h3>
    <p className="text-xs text-muted-foreground">Provider figures are authoritative. Meridian shadow estimates are diagnostic. Each account uses its own retained snapshot date; currency groups have separate scales and are never added together. Zero excess is the deficit boundary.</p>
    {accounts.length === 0 ? <p>No retained account evidence is available.</p> : null}
    {marginCurrencyGroups(accounts).map(([currency, rows]) => {
      const maintenanceValues = rows.flatMap((row) => [row.providerMaintenanceMargin, row.shadowMaintenanceMargin]).filter(finite);
      const maintenanceMin = Math.min(0, ...maintenanceValues);
      const maintenanceMax = Math.max(1, ...maintenanceValues);
      const maintenanceX = (value: number) => 220 + (value - maintenanceMin) / (maintenanceMax - maintenanceMin) * 500;
      const excessMax = Math.max(1, ...rows.map((row) => row.providerExcessLiquidity).filter(finite).map(Math.abs));
      const height = rows.length * 48 + 75;
      return <div key={currency} className="space-y-2">
        <h4 className="font-semibold text-xs">{currency} accounts</h4>
        <div className="grid gap-3 2xl:grid-cols-2">
          <div><p className="text-xs">Maintenance margin · ● provider · ◆ Meridian estimate</p><svg viewBox={`0 0 760 ${height}`} className="w-full" aria-hidden="true">
            <line x1="220" x2="720" y1="28" y2="28" stroke="currentColor" opacity=".3" /><text x="220" y="18" fontSize="12">{number(maintenanceMin)}</text><text x="720" y="18" fontSize="12" textAnchor="end">{number(maintenanceMax)} {currency}</text>
            {rows.map((row, index) => { const y = 56 + index * 48; const provider = row.providerMaintenanceMargin; const shadow = row.shadowMaintenanceMargin; const px = finite(provider) ? maintenanceX(provider) : null; const sx = finite(shadow) ? maintenanceX(shadow) : null;
              return <g key={key(row)}><ChartAccountLabel account={row} y={y} />{px !== null && sx !== null ? <line x1={px} x2={sx} y1={y} y2={y} stroke="currentColor" opacity=".5" /> : null}{px !== null ? <circle cx={px} cy={y} r="5" fill="currentColor" /> : null}{sx !== null ? <path d={`M ${sx} ${y - 7} l 7 7 l -7 7 l -7 -7 Z`} fill="none" stroke="var(--chart-series-2)" strokeWidth="2" /> : null}{px === null || sx === null ? <text x="220" y={y + 21} fontSize="12">{px === null ? "Provider not reported. " : ""}{sx === null ? "Estimate unavailable." : ""}</text> : null}</g>;
            })}
          </svg></div>
          <div><p className="text-xs">Provider excess liquidity · left of zero = deficit</p><svg viewBox={`0 0 760 ${height}`} className="w-full" aria-hidden="true">
            <line x1="470" x2="470" y1="28" y2={height - 10} stroke="currentColor" /><text x="470" y="18" textAnchor="middle" fontSize="12">0</text><text x="720" y="18" textAnchor="end" fontSize="12">{number(excessMax)} {currency}</text>
            {rows.map((row, index) => { const y = 56 + index * 48; const value = row.providerExcessLiquidity; const width = finite(value) ? Math.abs(value) / excessMax * 240 : 0;
              return <g key={key(row)}><ChartAccountLabel account={row} y={y} />{finite(value) ? <><rect x={value < 0 ? 470 - width : 470} y={y - 7} width={width} height="14" fill={value < 0 ? "var(--chart-dn)" : "var(--chart-series-2)"} /><text x="225" y={y + 24} fontSize="12">{value < 0 ? "Deficit" : "Excess"}: {number(value)}</text></> : <text x="225" y={y + 24} fontSize="12">Not reported</text>}</g>;
            })}
          </svg></div>
        </div>
        <DenseDataTable rows={rows} getRowId={key} ariaLabel={`${currency} margin comparison`} tableClassName="min-w-[720px]" maxVisibleRows={null}
          caption="Select an account to inspect model, retained evidence and position contributions." emptyText="No retained account evidence in this currency."
          columns={[
            { id: "account", label: "Account", render: (row) => <button type="button" className="text-primary underline underline-offset-2 focus-visible:ring-2 focus-visible:ring-primary" aria-pressed={key(row) === selectedKey} onClick={() => setSelectedKey(key(row))}>{row.providerId} · {row.accountId}</button> },
            { id: "as-of", label: "As of / phase", render: (row) => <>{row.asOf}<br />{row.snapshotPhase}</> },
            { id: "provider", label: "Provider maintenance", render: (row) => amount(row.providerMaintenanceMargin, currency) },
            { id: "shadow", label: "Meridian maintenance", render: (row) => amount(row.shadowMaintenanceMargin, currency) },
            { id: "excess", label: "Provider excess", render: (row) => amount(row.providerExcessLiquidity, currency) },
            { id: "state", label: "State", render: (row) => <>{row.riskLevel} · {row.certificationState}</> }
          ]} />
      </div>;
    })}
    {selected ? <div className="space-y-2 border-t border-border pt-3 text-xs" aria-live="polite" aria-label="Selected margin account">
      <h4 className="font-semibold">{selected.providerId} · {selected.accountId}</h4><p>Model: {selected.shadowModelName} · {selected.marginRegime}</p><p>Source: retained connector evidence {selected.evidencePath} · as of {selected.asOf}</p><p>Provider minus Meridian maintenance: {amount(selected.maintenanceVariance, selected.currency)}. Provider excess: {amount(selected.providerExcessLiquidity, selected.currency)}. Shadow excess: {amount(selected.shadowExcessLiquidity, selected.currency)}.</p><p>{selected.snapshotPhase} · {selected.certificationState} · activity complete: {selected.activityComplete === null ? "not reported" : selected.activityComplete ? "yes" : "no"}</p><p>{selected.restrictions.join("; ") || "No retained restrictions reported."}</p><a className="text-primary underline" href={`#${marginAccountAnchor(selected)}`}>Inspect position contributions and certification evidence</a>
    </div> : <p className="text-xs text-muted-foreground">Select an account in the comparison table for evidence.</p>}
  </section>;
}

function key(account: MarginControlAccount) { return `${account.providerId}\u001f${account.accountId}`; }
function number(value: number) { return formatNumber(value, { maximumFractionDigits: 2 }); }
function amount(value: number | null, currency: string) { return finite(value) ? `${number(value)} ${currency}` : "Not reported"; }

function ChartAccountLabel({ account, y }: { account: MarginControlAccount; y: number }) {
  const short = (value: string) => Array.from(value).length > 26 ? `${Array.from(value).slice(0, 23).join("")}…` : value;
  return <text x="5" y={y - 4} fontSize="12"><title>{account.providerId} · {account.accountId}</title><tspan x="5">{short(account.providerId)}</tspan><tspan x="5" dy="16">{short(account.accountId)}</tspan></text>;
}
