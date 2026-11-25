import { useMemo, useState, type KeyboardEvent } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { formatTaxAmount, isTaxDecimalText } from "@/lib/ledger-tax-results-format";
import type { LedgerDisposalTaxParcel, LedgerDisposalTaxResult } from "@/types/ledger-tax-results";

/** Exact decimal evidence is reduced to bounded ratios only for screen coordinates. */
export function taxParcelCoordinates(parcels: LedgerDisposalTaxParcel[]) {
  const validAmounts = parcels.map((parcel) => isTaxDecimalText(parcel.economicGainOrLoss));
  const scale = Math.max(0, ...parcels.map((parcel, index) => validAmounts[index] ? parcel.economicGainOrLoss.split(".")[1]?.length ?? 0 : 0));
  const scaled = (value: string) => {
    const negative = value.startsWith("-");
    const [integer, fraction = ""] = (negative ? value.slice(1) : value).split(".");
    return BigInt(`${integer}${fraction.padEnd(scale, "0")}`) * (negative ? -1n : 1n);
  };
  const values = parcels.map((parcel, index) => validAmounts[index] ? scaled(parcel.economicGainOrLoss) : 0n);
  let minimum = 0n; let maximum = 0n;
  for (const value of values) { if (value < minimum) minimum = value; if (value > maximum) maximum = value; }
  if (minimum === maximum) { minimum = -1n; maximum = 1n; }
  const span = maximum - minimum;
  const ratio = (value: bigint) => Number(((value - minimum) * 1_000_000n) / span) / 1_000_000;
  const minimumText = minimum < 0n ? parcels[values.findIndex(value => value === minimum)]?.economicGainOrLoss ?? "0" : "0";
  const maximumText = maximum > 0n ? parcels[values.findIndex(value => value === maximum)]?.economicGainOrLoss ?? "0" : "0";
  const validDays = parcels.map((parcel) => Number.isFinite(parcel.holdingPeriodDays) && parcel.holdingPeriodDays >= 0);
  const maxDays = Math.max(1, ...parcels.filter((_, index) => validDays[index]).map((parcel) => parcel.holdingPeriodDays));
  return { minimumText, maximumText, zeroY: 235 - ratio(0n) * 195, maxDays,
    omitted: parcels.filter((_, index) => !validAmounts[index] || !validDays[index]).length,
    points: parcels.map((parcel, index) => ({ available: validAmounts[index] && validDays[index], x: validDays[index] ? 90 + parcel.holdingPeriodDays / maxDays * 555 : 90, y: 235 - ratio(values[index]) * 195 })) };
}

export function TaxParcelComposition({ parcels, currency, state, disposal }: { parcels: LedgerDisposalTaxParcel[]; currency: string; state: string; disposal?: LedgerDisposalTaxResult }) {
  const [selected, setSelected] = useState<string | null>(null);
  const parcelKey = (item: LedgerDisposalTaxParcel, index: number) => `${disposal?.mutationBatchId ?? ""}:${item.lotId}:${index}`;
  const plot = useMemo(() => taxParcelCoordinates(parcels), [parcels]);
  const parcel = parcels.find((item, index) => parcelKey(item, index) === selected) ?? null;
  const rows = parcels.map((item, index) => ({ ...item, selectionId: parcelKey(item, index) }));
  const columns: DenseDataTableColumn<(typeof rows)[number]>[] = [
    { id: "parcel", label: "Parcel", render: item => <button className="underline" aria-pressed={selected === item.selectionId} onClick={() => setSelected(item.selectionId)}>{item.lotId}</button> },
    { id: "days", label: "Holding days", numeric: true, render: item => Number.isFinite(item.holdingPeriodDays) && item.holdingPeriodDays >= 0 ? item.holdingPeriodDays : "Unavailable" },
    { id: "character", label: "Tax character", render: item => `${item.character === "LongTerm" ? "Long term" : "Short term"}${item.holdingPeriodCarried ? " · Carried" : ""}` },
    { id: "economic", label: "Economic gain / loss", numeric: true, render: item => formatTaxAmount(item.economicGainOrLoss, currency) }
  ];
  return <section aria-label="Tax parcel composition" className="space-y-3 rounded border border-border p-3">
    <h4 className="text-sm font-semibold">Tax parcel composition · {state}</h4>
    <p className="text-xs text-muted-foreground">Retained holding-period days versus economic gain / loss ({currency}). Circles: short term; diamonds: long term. Character comes from the retained policy. Select a parcel for recognized gain and deferred loss.</p>
    <svg viewBox="0 0 720 290" role="group" aria-label="Disposed parcels by retained holding days and economic gain or loss" className="w-full" style={{ minHeight: 180 }}>
      <line x1="90" x2="655" y1={plot.zeroY} y2={plot.zeroY} stroke="currentColor" strokeDasharray="4 3" />
      <line x1="90" x2="90" y1="30" y2="235" stroke="currentColor" />
      <line x1="90" x2="655" y1="235" y2="235" stroke="currentColor" />
      <text x="78" y={plot.zeroY + 4} textAnchor="end" fill="currentColor" fontSize="12">0</text>
      <text x="90" y="256" fill="currentColor" fontSize="12">0 days</text>
      <text x="645" y="256" textAnchor="end" fill="currentColor" fontSize="12">{plot.maxDays} days</text>
      <text x="365" y="280" textAnchor="middle" fill="currentColor" fontSize="12">Retained holding-period days</text>
      {parcels.map((item, index) => {
        const point = plot.points[index];
        if (!point.available) return null;
        const props = { tabIndex: 0, role: "button", "aria-label": `Inspect parcel ${item.lotId}: ${item.holdingPeriodDays} days, ${item.character === "LongTerm" ? "long term" : "short term"}, economic gain or loss ${formatTaxAmount(item.economicGainOrLoss, currency)}`, "aria-pressed": selected === parcelKey(item, index), onClick: () => setSelected(parcelKey(item, index)), onKeyDown: (event: KeyboardEvent<SVGElement>) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); setSelected(parcelKey(item, index)); } }, fill: selected === parcelKey(item, index) ? "currentColor" : "hsl(var(--background, 0 0% 100%))", stroke: "currentColor", strokeWidth: selected === parcelKey(item, index) ? 3 : 2, className: "cursor-pointer focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary" };
        return item.character === "LongTerm" ? <path key={`${item.lotId}:${index}`} {...props} d={`M${point.x},${point.y - 7} l7,7 -7,7 -7,-7 Z`} /> : <circle key={`${item.lotId}:${index}`} {...props} cx={point.x} cy={point.y} r="6" />;
      })}
    </svg>
    {plot.omitted ? <p role="status" className="text-xs text-warning">{plot.omitted} parcels have unavailable plot coordinates. Their retained evidence remains in the table and disposal details.</p> : null}
    <p className="text-xs text-muted-foreground">{plot.omitted < parcels.length ? <>Economic range {formatTaxAmount(plot.minimumText, currency)} to {formatTaxAmount(plot.maximumText, currency)}. </> : "No retained parcels have available plot coordinates. "}Plot positions are approximate; exact retained amounts appear below. Overlapping marks remain separately selectable in the table.</p>
    <DenseDataTable columns={columns} rows={rows} getRowId={item => item.selectionId}
      ariaLabel="Disposed parcel composition table" emptyText="No retained parcel composition is available." />
    {parcel ? <section aria-label="Selected parcel composition" className="rounded border border-border p-3 text-sm"><h5 className="font-semibold">{parcel.lotId} · {state}</h5><p className="text-xs">Retained tax character: {parcel.character === "LongTerm" ? "Long term" : "Short term"}. {disposal?.stateReason}</p><dl className="mt-2 grid gap-2 md:grid-cols-3">{[["Economic gain / loss", parcel.economicGainOrLoss], ["Recognized gain / loss", parcel.recognizedGainOrLoss], ["Deferred loss", parcel.deferredLoss]].map(([label, value]) => <div key={label}><dt className="text-xs text-muted-foreground">{label}</dt><dd className="break-all font-mono">{formatTaxAmount(value, currency)}</dd></div>)}</dl><p className="mt-2 text-xs">Holding start {parcel.holdingPeriodStart} · Acquired {parcel.acquiredDate} · {parcel.holdingPeriodCarried ? "Carried holding period" : "No carried holding period"}. Selection does not change retained tax treatment.</p>{disposal ? <p className="mt-2 break-words text-xs">Policy revision {disposal.policyRevision ?? "Missing evidence"} · disposal {disposal.mutationBatchId} · sale {disposal.saleDate}. {disposal.canChange ? "This retained result can change." : "No change indicated by retained result."} {disposal.reEvaluationRequired ? "Re-evaluation required; this plot does not re-evaluate treatment." : ""}</p> : null}</section> : null}
  </section>;
}
