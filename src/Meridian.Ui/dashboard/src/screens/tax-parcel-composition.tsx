import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { formatTaxAmount, isTaxDecimalText } from "@/lib/ledger-tax-results-format";
import type { LedgerDisposalTaxParcel, LedgerDisposalTaxResult, LedgerJournalTaxResults } from "@/types/ledger-tax-results";

interface ParcelPoint { id: string; index: number; parcel: LedgerDisposalTaxParcel; disposal: LedgerDisposalTaxResult; x: number; y: number }

export function taxParcelPoints(result: LedgerJournalTaxResults): { points: ParcelPoint[]; omitted: number } {
  const points: ParcelPoint[] = [];
  let omitted = 0;
  let index = 0;
  result.disposals.forEach((disposal, disposalIndex) => disposal.parcels.forEach((parcel, parcelIndex) => {
    index++;
    const x = parcel.holdingPeriodDays;
    const y = isTaxDecimalText(parcel.economicGainOrLoss) ? Number(parcel.economicGainOrLoss) : Number.NaN;
    if (!Number.isFinite(x) || x < 0 || !Number.isFinite(y)) { omitted++; return; }
    points.push({ id: `${disposal.mutationBatchId}:${disposalIndex}:${parcel.lotId}:${parcelIndex}`, index, parcel, disposal, x, y });
  }));
  return { points, omitted };
}

const character = { ShortTerm: "Short term", LongTerm: "Long term" };

export function TaxParcelComposition({ result }: { result: LedgerJournalTaxResults }) {
  const id = useId();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const { points, omitted } = taxParcelPoints(result);
  const selected = points.find((point) => point.id === selectedId);
  const maxX = Math.max(1, ...points.map((point) => point.x));
  const rawMin = Math.min(0, ...points.map((point) => point.y));
  const rawMax = Math.max(0, ...points.map((point) => point.y));
  const minY = rawMin === rawMax ? -1 : rawMin;
  const maxY = rawMin === rawMax ? 1 : rawMax;
  const x = (value: number) => 88 + (value / maxX) * 640;
  const y = (value: number) => 250 - ((value - minY) / (maxY - minY)) * 210;
  const approximate = (value: number) => new Intl.NumberFormat("en", { maximumSignificantDigits: 4, notation: Math.abs(value) >= 1_000_000 ? "compact" : "standard" }).format(value);
  return (
    <section aria-labelledby={`${id}-title`} className="space-y-3 rounded-sm border border-border p-3">
      <h3 id={`${id}-title`} className="text-sm font-semibold">Tax-parcel composition</h3>
      <p className="text-xs text-muted-foreground">Retained holding-period days versus economic gain / loss ({result.functionalCurrency || "currency unavailable"}). Circles: short term; squares: long term. Dashed outlines: provisional or missing evidence. Tax character is supplied by the retained policy. Plot coordinates are approximate; selected amounts retain every decimal.</p>
      {points.length ? (
        <>
          <svg viewBox="0 0 780 310" className="max-h-80 w-full min-w-0" role="group" aria-label="Disposed parcels by retained holding-period days and economic gain or loss">
            <line x1="88" x2="728" y1={y(0)} y2={y(0)} stroke="currentColor" strokeDasharray="4 3" opacity="0.65" />
            <line x1="88" x2="88" y1="40" y2="250" stroke="currentColor" />
            <line x1="88" x2="728" y1="250" y2="250" stroke="currentColor" />
            {[minY, (minY + maxY) / 2, maxY].map((value, index) => <text key={index} x="80" y={y(value) + 4} textAnchor="end" fill="currentColor" fontSize="12">{approximate(value)}</text>)}
            {[...new Set([0, Math.floor(maxX / 2), maxX])].map((value) => <text key={value} x={x(value)} y="272" textAnchor="middle" fill="currentColor" fontSize="12">{value}</text>)}
            <text x="408" y="300" textAnchor="middle" fill="currentColor" fontSize="13">Retained holding-period days</text>
            <text x="88" y="22" fill="currentColor" fontSize="12">Economic gain / loss ({result.functionalCurrency || "unavailable currency"})</text>
            {points.map((point) => {
              const label = `Inspect parcel ${point.index}, ${point.parcel.lotId}, ${point.x} holding days, ${character[point.parcel.character]}, ${point.disposal.state}, economic gain or loss ${formatTaxAmount(point.parcel.economicGainOrLoss, result.functionalCurrency)}`;
              return <g key={point.id} role="button" tabIndex={0} aria-label={label} aria-pressed={point.id === selectedId}
                onClick={() => setSelectedId(point.id)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); setSelectedId(point.id); } }}
                className="cursor-pointer text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary"
                stroke="currentColor" fill={point.id === selectedId ? "currentColor" : "hsl(var(--background))"}
                strokeWidth={point.id === selectedId ? 3 : 2} strokeDasharray={point.disposal.state === "Settled" ? undefined : "3 2"}>
                <rect x={x(point.x) - 13} y={y(point.y) - 13} width="26" height="26" fill="transparent" stroke="none" />
                {point.parcel.character === "LongTerm" ? <rect x={x(point.x) - 6} y={y(point.y) - 6} width="12" height="12" /> : <circle cx={x(point.x)} cy={y(point.y)} r="6" />}
              </g>;
            })}
          </svg>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <caption className="sr-only">Parcel plot data and keyboard selection; full lot identities and exact tax evidence open on selection.</caption>
              <thead><tr>{["Parcel", "Security", "Holding days", "Supplied character", "State", "Economic gain / loss"].map((label) => <th scope="col" key={label} className="border-b border-border p-2">{label}</th>)}</tr></thead>
              <tbody>{points.map((point) => <tr key={point.id} className={point.id === selectedId ? "bg-secondary/40" : ""}>
                <td className="p-2"><Button size="sm" variant="outline" aria-label={`Select parcel ${point.index}: ${point.parcel.lotId}`} aria-pressed={point.id === selectedId} onClick={() => setSelectedId(point.id)}>Parcel {point.index}</Button></td>
                <td className="p-2">{point.disposal.symbol ?? point.disposal.accountName}</td><td className="p-2 font-mono">{point.x}</td>
                <td className="p-2">{point.parcel.character}</td><td className="p-2">{point.disposal.state} result</td>
                <td className="p-2 font-mono">{formatTaxAmount(point.parcel.economicGainOrLoss, result.functionalCurrency)}</td>
              </tr>)}</tbody>
            </table>
          </div>
        </>
      ) : <p role="status" className="text-sm text-muted-foreground">No plottable retained parcel evidence for this journal entry.</p>}
      {omitted ? <p role="status" className="text-xs text-warning">{omitted} parcels have unavailable plot coordinates. Original evidence remains in the disposal details.</p> : null}
      {selected ? <section aria-label="Selected tax parcel evidence" className="space-y-2 rounded-sm border border-border p-3 text-xs">
        <h4 className="font-semibold">Parcel {selected.index} · {selected.parcel.lotId}</h4>
        <p>{character[selected.parcel.character]} · {selected.disposal.state} · {selected.disposal.stateReason}</p>
        <dl className="grid gap-3 md:grid-cols-3">
          {[["Economic gain / loss", formatTaxAmount(selected.parcel.economicGainOrLoss, result.functionalCurrency)], ["Recognized gain / loss", formatTaxAmount(selected.parcel.recognizedGainOrLoss, result.functionalCurrency)], ["Deferred loss", formatTaxAmount(selected.parcel.deferredLoss, result.functionalCurrency)], ["Holding-period start", selected.parcel.holdingPeriodStart], ["Retained holding days", String(selected.parcel.holdingPeriodDays)], ["Holding-period treatment", selected.parcel.holdingPeriodCarried ? "Carried from prior parcel" : "Original parcel"], ["Policy revision", selected.disposal.policyRevision ?? "Missing evidence"], ["Disposal identity", selected.disposal.mutationBatchId], ["Sale date", selected.disposal.saleDate]].map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="break-words font-mono">{value}</dd></div>)}
        </dl>
        <p>{selected.disposal.canChange ? "This retained result can change." : "No change indicated by retained result."} {selected.disposal.reEvaluationRequired ? "Re-evaluation required; this plot does not re-evaluate tax treatment." : ""}</p>
      </section> : points.length ? <p className="text-xs text-muted-foreground">Select a point or parcel row to compare economic gain, recognized gain and deferred loss.</p> : null}
    </section>
  );
}
