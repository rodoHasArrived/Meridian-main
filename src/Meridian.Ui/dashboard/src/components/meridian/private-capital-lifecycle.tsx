import { useId, useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { DetailTechnicalFields } from "@/components/meridian/detail-pane";
import type { CapitalActivity, CapitalCommitment } from "@/types/family-office.types";
import { formatNumber } from "@/lib/format";
import { groupPrivateCapital, hasPrivateCapitalBalances, PRIVATE_CAPITAL_METRICS, privateCapitalRowKey } from "./private-capital-lifecycle.view-model";

export interface PrivateCapitalLifecycleProps {
  commitments: CapitalCommitment[];
  activities?: CapitalActivity[];
  asOfDate: string;
  entities?: ReadonlyArray<{ entityId: string; displayName: string }>;
}

const money = (amount: number, currency: string) => `${formatNumber(amount, { maximumFractionDigits: 2 })} ${currency}`;

/** Read-only retained lifecycle balances; distributions and remaining NAV are independent measures. */
export function PrivateCapitalLifecycle({ commitments, activities = [], asOfDate, entities = [] }: PrivateCapitalLifecycleProps) {
  const id = useId();
  const [grouping, setGrouping] = useState<"vintage" | "strategy">("vintage");
  const [currencyChoice, setCurrencyChoice] = useState("");
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const currencies = [...new Set(commitments.map((row) => row.currency))].sort();
  const currency = currencies.includes(currencyChoice) ? currencyChoice : currencies[0] ?? "";
  const view = groupPrivateCapital(commitments, grouping, currency);
  const rows = view.groups.flatMap((group) => group.rows);
  const selected = rows.find((row) => privateCapitalRowKey(row) === selectedKey) ?? rows[0];
  const selectedActivity = selected ? activities.filter((row) => row.commitmentId === selected.commitmentId
    && row.entityId === selected.entityId && row.currency === selected.currency) : [];
  const entityName = (entityId: string) => entities.find((entity) => entity.entityId === entityId)?.displayName ?? "Entity name unavailable";

  return <Card>
    <CardHeader>
      <CardTitle>Private-capital lifecycle</CardTitle>
      <CardDescription>Commitment, called capital, unfunded commitment, distributions and remaining NAV. Source cut {asOfDate}; each vehicle retains its own balance and valuation dates.</CardDescription>
    </CardHeader>
    <CardContent className="space-y-4">
      <div className="flex flex-wrap items-end gap-4">
        <label htmlFor={`${id}-group`} className="space-y-1 text-sm"><span className="block">Group vehicles by</span>
          <select id={`${id}-group`} aria-label="Group vehicles by" className="rounded-sm border border-border bg-background p-2" value={grouping}
            onChange={(event) => setGrouping(event.target.value as "vintage" | "strategy")}>
            <option value="vintage">Vintage</option><option value="strategy">Strategy</option>
          </select>
        </label>
        <label htmlFor={`${id}-currency`} className="space-y-1 text-sm"><span className="block">Lifecycle currency</span>
          <select id={`${id}-currency`} aria-label="Lifecycle currency" className="rounded-sm border border-border bg-background p-2" value={currency}
            disabled={currencies.length === 0} onChange={(event) => setCurrencyChoice(event.target.value)}>
            {currencies.length === 0 ? <option value="">Unavailable</option> : currencies.map((item) => <option key={item}>{item}</option>)}
          </select>
        </label>
      </div>
      <p className="text-xs text-muted-foreground">Bars share a scale within the selected currency. The five measures are separate balances; distributions and NAV are not added to called capital. Unfunded uses the supplied balance, including any recallable-capital adjustments.</p>
      {rows.length === 0 ? <p role="status">No retained private-capital vehicles are available. Configure a retained lifecycle source; missing balances are not zero.</p> : <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <caption className="sr-only">Private-capital balances in {currency}. Select a vehicle to inspect its retained source and capital activity.</caption>
          <thead><tr className="border-b border-border text-left"><th scope="col" className="p-2">Vehicle</th>{PRIVATE_CAPITAL_METRICS.map(({ key, label }) => <th key={key} scope="col" className="min-w-32 p-2 text-right">{label}</th>)}<th scope="col" className="p-2">Balance status</th></tr></thead>
          {view.groups.map((group) => <tbody key={group.label}>
            <tr><th scope="rowgroup" colSpan={7} className="bg-secondary/40 px-2 py-2 text-left text-xs font-semibold">{group.label}</th></tr>
            {group.rows.map((row) => {
              const available = hasPrivateCapitalBalances(row);
              const key = privateCapitalRowKey(row);
              return <tr key={key} className={`border-b border-border ${selected && privateCapitalRowKey(selected) === key ? "bg-secondary/40" : ""}`}>
                <th scope="row" className="p-2 text-left font-normal"><button type="button" className="text-left font-semibold underline decoration-border underline-offset-4 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary"
                  aria-pressed={selected && privateCapitalRowKey(selected) === key} onClick={() => setSelectedKey(key)}>{row.vehicleName}</button>
                  <span className="block text-xs text-muted-foreground">{row.strategy} · {entityName(row.entityId)}</span></th>
                {PRIVATE_CAPITAL_METRICS.map(({ key: metric, label }) => <td key={metric} className="p-2 text-right font-mono text-xs">
                  <span>{available ? money(row[metric], row.currency) : "Unavailable"}</span>
                  <svg aria-hidden="true" viewBox="0 0 100 8" className="mt-1 h-2 w-full" preserveAspectRatio="none"><rect width="100" height="8" fill="var(--chart-grid, #E4E3DE)" />
                    {available ? <rect x={view.hasNegative ? 50 + Math.min(0, row[metric]) / (view.maximum || 1) * 50 : 0}
                      width={Math.abs(row[metric]) / (view.maximum || 1) * (view.hasNegative ? 50 : 100)} height="8" fill={label === "Distributions" ? "var(--chart-equity)" : "var(--chart-axis)"} /> : null}
                    {view.hasNegative ? <line x1="50" x2="50" y1="0" y2="8" stroke="var(--chart-border, #AFABA1)" strokeWidth="0.5" /> : null}</svg>
                </td>)}
                <td className="p-2 text-xs">{available ? row.balanceStatus ?? "Retained" : "Unavailable"}<span className="block text-muted-foreground">As of {row.asOfDate}</span></td>
              </tr>;
            })}
          </tbody>)}
        </table>
      </div>}
      {selected ? <section aria-label="Selected private-capital vehicle" className="space-y-3 rounded-sm border border-border p-3" aria-live="polite">
        <h3 className="font-semibold">{selected.vehicleName} · retained evidence</h3>
        {!hasPrivateCapitalBalances(selected) ? <p role="status">Balances are unavailable for this assignment. Its numeric placeholders do not represent zero commitments or zero NAV.</p> : null}
        <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-xs lg:grid-cols-4">
          {[
            ["Entity", entityName(selected.entityId)], ["Currency", selected.currency],
            ["Balance status", selected.balanceStatus ?? "Retained"], ["Balance date", selected.asOfDate], ["NAV valuation date", selected.valuationDate ?? "Unavailable"],
            ["Evidence", selected.evidenceCompleteness],
            ["Reconciliation", selected.reconciliationStatus === "NotReconciled" ? "Not reconciled" : selected.reconciliationStatus === "OpenBreaks" ? "Open breaks" : selected.reconciliationStatus === "NeedsReview" ? "Needs review" : selected.reconciliationStatus]
          ].map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="break-words font-mono">{value}</dd></div>)}
        </dl>
        <TechnicalDetails key={privateCapitalRowKey(selected)} label="Technical and source details" description="Retained identifiers and source evidence for the selected vehicle.">
          <DetailTechnicalFields fields={[
            { label: "Commitment identity", value: selected.commitmentId }, { label: "Entity identity", value: selected.entityId },
            { label: "Source system", value: selected.sourceSystem }, { label: "Source document", value: selected.sourceDocumentId ?? "Unavailable" },
            { label: "Snapshot", value: selected.snapshotId ?? "Not supplied" }, { label: "Source revision", value: selected.sourceRevision?.toString() ?? "Not supplied" },
            { label: "Snapshot content SHA-256", value: selected.sourceContentSha256 ?? "Not supplied" }, { label: "Recorded at", value: selected.recordedAtUtc ?? "Not supplied" },
            { label: "Retained reconciliation status", value: selected.reconciliationStatus }, { label: "Retained evidence status", value: selected.evidenceCompleteness }
          ]} />
        </TechnicalDetails>
        {hasPrivateCapitalBalances(selected) ? <dl className="grid grid-cols-2 gap-3 text-xs lg:grid-cols-5" aria-label="Selected vehicle exact balances">
          {PRIVATE_CAPITAL_METRICS.map(({ key, label }) => <div key={key}><dt className="text-muted-foreground">{label}</dt><dd className="break-words font-mono">{selected.exactBalances?.[key] ?? selected[key].toString()} {selected.currency}</dd></div>)}
        </dl> : null}
        <h4 className="text-sm font-semibold">Retained capital activity</h4>
        {selectedActivity.length === 0 ? <p className="text-xs text-muted-foreground">No retained activity supplied for this vehicle. This does not establish that no calls or distributions occurred.</p> : <div className="overflow-x-auto"><table className="w-full text-xs">
          <caption className="sr-only">Capital activity for {selected.vehicleName}</caption>
          <thead><tr className="text-left">{["Effective date", "Activity", "Amount", "Status", "Source evidence"].map((heading) => <th key={heading} scope="col" className="p-2">{heading}</th>)}</tr></thead>
          <tbody>{selectedActivity.map((activity) => <tr key={activity.activityId} className="border-t border-border"><td className="p-2">{activity.effectiveDate}</td><td className="p-2">{activity.activityType}</td><td className="p-2 font-mono">{money(activity.amount, activity.currency)}</td><td className="p-2">{activity.status}</td><td className="p-2"><TechnicalDetails label="Activity source details"><DetailTechnicalFields fields={[
            { label: "Activity identity", value: activity.activityId }, { label: "Commitment identity", value: activity.commitmentId },
            { label: "Entity identity", value: activity.entityId }, { label: "Source system", value: activity.sourceSystem },
            { label: "Source document", value: activity.sourceDocumentId ?? "Unavailable" }
          ]} /></TechnicalDetails></td></tr>)}</tbody>
        </table></div>}
      </section> : null}
    </CardContent>
  </Card>;
}
