import { useEffect, useRef, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { ScreenLayout } from "@/components/ui/screen-layout";
import { StatusBanner } from "@/components/ui/status-banner";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { screenOptions } from "@/lib/api/option-visualization.api";
import { describeApiError } from "@/lib/api-errors";
import { OptionAssessmentPanel } from "@/screens/options-screen.assessments";
import { candidateLeg, comparableBasket, DEFAULT_OPTION_CRITERIA, EMPTY_OPTION_BASKET, newEquityLeg, optionCoincidentGroups } from "@/screens/options-screen.view-model";
import type { OptionAssessmentBasket, OptionBasketLeg, OptionScreenCandidate, OptionScreenCriteria, OptionScreenResult } from "@/types/option-visualization.types";

const columns: DenseDataTableColumn<OptionScreenCandidate>[] = [
  { id: "contract", label: "Contract", render: row => row.symbol },
  { id: "right", label: "Right", render: row => row.right },
  { id: "expiry", label: "Expiry / DTE", render: row => `${row.expiration} / ${row.daysToExpiration}` },
  { id: "strike", label: "Strike", render: row => row.strike, align: "right" },
  { id: "delta", label: "Delta", render: row => row.delta ?? "Unavailable", align: "right" },
  { id: "quote", label: "Bid / Ask", render: row => `${row.bid} / ${row.ask}`, align: "right" },
  { id: "result", label: "Screen", render: row => row.passesScreen ? "Pass" : "Rejected" }
];

function readCriteria(params: URLSearchParams): OptionScreenCriteria {
  const defaults = DEFAULT_OPTION_CRITERIA;
  const number = (key: string, fallback: number) => params.has(key) && Number.isFinite(Number(params.get(key))) ? Number(params.get(key)) : fallback;
  return { ...defaults, underlyingSymbol: params.get("symbol") ?? "",
    minDte: number("optionMinDte", defaults.minDte), maxDte: number("optionMaxDte", defaults.maxDte),
    minDelta: number("optionMinDelta", defaults.minDelta), maxDelta: number("optionMaxDelta", defaults.maxDelta),
    right: ["Call", "Put"].includes(params.get("optionRight") ?? "") ? params.get("optionRight") as "Call" | "Put" : "Both",
    minOpenInterest: number("optionOi", 0), minVolume: number("optionVolume", 0), strikeRange: number("optionStrikes", 20),
    maxSpreadFraction: params.has("optionSpread") ? number("optionSpread", 0) : null,
    minPremium: params.has("optionPremium") ? number("optionPremium", 0) : null,
    premiumBasis: ["Ask", "Mid"].includes(params.get("optionPremiumBasis") ?? "") ? params.get("optionPremiumBasis") as "Ask" | "Mid" : "Bid" };
}

export function OptionsScreen() {
  const [params, setParams] = useSearchParams();
  const [criteria, setCriteria] = useState(() => readCriteria(params));
  const [numberDrafts, setNumberDrafts] = useState<Partial<Record<keyof OptionScreenCriteria, string>>>({});
  const [data, setData] = useState<OptionScreenResult | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [pending, setPending] = useState(false); const [error, setError] = useState<string | null>(null);
  const [basket, setBasket] = useState<OptionAssessmentBasket>(() => ({ ...EMPTY_OPTION_BASKET, legs: [] }));
  const [account, setAccount] = useState(params.get("fundAccountId") ?? "");
  const [activeLeg, setActiveLeg] = useState("new");
  const controller = useRef<AbortController | null>(null); const ownQuery = useRef<string | null>(null);
  const query = params.toString();
  useEffect(() => {
    if (ownQuery.current === query) { ownQuery.current = null; return; }
    setCriteria(readCriteria(new URLSearchParams(query))); setNumberDrafts({}); setAccount(new URLSearchParams(query).get("fundAccountId") ?? "");
    controller.current?.abort(); setPending(false); setData(null); setSelectedId(null);
  }, [query]);
  useEffect(() => () => controller.current?.abort(), []);

  function edit<K extends keyof OptionScreenCriteria>(key: K, value: OptionScreenCriteria[K]) {
    controller.current?.abort(); setPending(false); setData(null); setSelectedId(null); setError(null);
    setCriteria(previous => ({ ...previous, [key]: value }));
  }
  function editNumber(key: "minDte" | "maxDte" | "minDelta" | "maxDelta" | "minOpenInterest" | "minVolume" | "strikeRange" | "maxSpreadFraction" | "minPremium", raw: string) {
    setNumberDrafts(previous => ({ ...previous, [key]: raw }));
    controller.current?.abort(); setPending(false); setData(null); setSelectedId(null); setError(null);
    if (raw.trim() && Number.isFinite(Number(raw))) edit(key, Number(raw));
    else if (key === "maxSpreadFraction" || key === "minPremium") edit(key, null);
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); controller.current?.abort();
    const abort = new AbortController(); controller.current = abort; setPending(true); setData(null); setError(null);
    const requested = { ...criteria, underlyingSymbol: criteria.underlyingSymbol.trim().toUpperCase() };
    // Keep criteria shareable without storing financial basket or imported broker evidence in URLs.
    const next = new URLSearchParams(params); next.set("symbol", requested.underlyingSymbol);
    next.set("optionMinDte", String(requested.minDte)); next.set("optionMaxDte", String(requested.maxDte));
    next.set("optionMinDelta", String(requested.minDelta)); next.set("optionMaxDelta", String(requested.maxDelta));
    next.set("optionRight", requested.right);
    next.set("optionOi", String(requested.minOpenInterest)); next.set("optionVolume", String(requested.minVolume));
    next.set("optionStrikes", String(requested.strikeRange)); next.set("optionPremiumBasis", requested.premiumBasis);
    if (requested.maxSpreadFraction === null) next.delete("optionSpread"); else next.set("optionSpread", String(requested.maxSpreadFraction));
    if (requested.minPremium === null) next.delete("optionPremium"); else next.set("optionPremium", String(requested.minPremium));
    // URL update precedes the request; let the route restoration effect settle first.
    if (next.toString() !== params.toString()) { ownQuery.current = next.toString(); setParams(next, { replace: true }); }
    try {
      const result = await screenOptions(requested, { signal: abort.signal });
      if (abort.signal.aborted) return;
      if (result.criteria.underlyingSymbol !== requested.underlyingSymbol) throw new Error("Options response did not match the requested underlying.");
      setData(result); setSelectedId(result.candidates[0]?.id ?? null);
    } catch (failure) { if (!abort.signal.aborted) setError(describeApiError(failure, "Option screening unavailable.").summary); }
    finally { if (!abort.signal.aborted) setPending(false); }
  }
  const selected = data?.candidates.find(row => row.id === selectedId) ?? null;
  const coincident = selected ? optionCoincidentGroups(data?.candidates ?? []).find(group => group.some(row => row.id === selected.id)) : null;
  function addLeg(createLeg: (legId: string) => OptionBasketLeg) {
    setBasket(previous => {
      if (previous.legs.length >= 50) return previous;
      const usedIds = new Set(previous.legs.map(leg => leg.legId));
      let number = 1;
      while (usedIds.has(`leg-${number}`)) number++;
      return { ...previous, legs: [...previous.legs, createLeg(`leg-${number}`)] };
    });
  }
  function addSelected() {
    if (!selected) return;
    if (activeLeg === "new") {
      addLeg(legId => candidateLeg(selected, legId));
    } else setBasket(previous => ({ ...previous, legs: previous.legs.map(leg => leg.legId === activeLeg
      ? { ...candidateLeg(selected, leg.legId), side: leg.side, positionIntent: leg.positionIntent, ratio: leg.ratio } : leg) }));
  }

  return <ScreenLayout title="Options screening" scope="Market data" description="Explore call and put contracts, assemble a proposed basket, and inspect dated imported broker assessments.">
    <Card><CardHeader><CardTitle>Contract criteria</CardTitle><CardDescription>Pass means these neutral criteria match. Account permission and the complete strategy basket are separate.</CardDescription></CardHeader>
      <CardContent><form onSubmit={submit} className="grid gap-3 md:grid-cols-4" aria-label="Option screening criteria">
        <label className="grid gap-1 text-xs">Underlying<Input required value={criteria.underlyingSymbol} onChange={event => edit("underlyingSymbol", event.target.value)} /></label>
        <label className="grid gap-1 text-xs">Right<Select aria-label="Right" value={criteria.right} onChange={event => edit("right", event.target.value as OptionScreenCriteria["right"])}><option>Both</option><option>Call</option><option>Put</option></Select></label>
        {([['minDte', 'Minimum DTE'], ['maxDte', 'Maximum DTE'], ['minDelta', 'Minimum signed delta'], ['maxDelta', 'Maximum signed delta'], ['minOpenInterest', 'Minimum open interest'], ['minVolume', 'Minimum volume'], ['strikeRange', 'Strikes each side of ATM']] as const).map(([key, label]) =>
          <label key={key} className="grid gap-1 text-xs">{label}<Input type="number" step={key.includes("Delta") ? "0.01" : "1"} required value={numberDrafts[key] ?? String(criteria[key])} onChange={event => editNumber(key, event.target.value)} /></label>)}
        <label className="grid gap-1 text-xs">Maximum spread / mid (fraction)<Input type="number" min="0" step="0.01" placeholder="No ceiling" value={numberDrafts.maxSpreadFraction ?? String(criteria.maxSpreadFraction ?? "")} onChange={event => editNumber("maxSpreadFraction", event.target.value)} /></label>
        <label className="grid gap-1 text-xs">Minimum premium per share<Input type="number" min="0" step="0.01" placeholder="No minimum" value={numberDrafts.minPremium ?? String(criteria.minPremium ?? "")} onChange={event => editNumber("minPremium", event.target.value)} /></label>
        <label className="grid gap-1 text-xs">Premium basis<Select aria-label="Premium basis" value={criteria.premiumBasis} onChange={event => edit("premiumBasis", event.target.value as OptionScreenCriteria["premiumBasis"])}><option>Bid</option><option>Ask</option><option>Mid</option></Select></label>
        <Button type="submit" disabled={pending} className="w-fit" aria-busy={pending}>{pending ? "Screening…" : "Screen contracts"}</Button>
      </form><p role="status" className="mt-3 text-xs">{pending ? "Loading current provider snapshots…" : data ? `${data.candidates.filter(row => row.passesScreen).length} of ${data.candidates.length} loaded contracts pass.` : "Enter an underlying and screen contracts. No market data is assumed."}</p>
      {error ? <StatusBanner role="alert" tone="danger" title="Screen unavailable" detail={error} /> : null}</CardContent></Card>

    {data ? <Card><CardHeader><CardTitle>Days to expiry × signed delta</CardTitle><CardDescription>{data.authorityNote}</CardDescription></CardHeader><CardContent className="grid gap-4">
      <p className="text-xs">{data.coverageNote} Screened {data.screenedAt}.</p>
      {data.failedExpirations.length || data.omittedExpirations.length ? <StatusBanner tone="warning" title="Partial chain coverage" detail={`Failed: ${data.failedExpirations.join(", ") || "none"}. Omitted by expiration limit: ${data.omittedExpirations.join(", ") || "none"}.`} /> : null}
      <OptionsMap data={data} selectedId={selectedId} onSelect={setSelectedId} />
      <p className="text-xs text-muted-foreground">Circle: call. Triangle: put. Filled: passes screen; outline: rejected. Numbered marks contain coincident contracts; fill describes the active contract. Missing delta stays in the table. The dashed boundary shows DTE and delta only; liquidity, premium and spread are evaluated separately.</p>
      <DenseDataTable rows={data.candidates} columns={columns} getRowId={row => row.id} onRowSelect={row => setSelectedId(row.id)} selectedRowId={selectedId} ariaLabel="Options screening contracts" caption="Loaded contracts and neutral screen outcomes" emptyText="No contracts were returned in this scope. Review expiration coverage." />
      {selected ? <section aria-label="Selected option contract" className="grid gap-3 border border-border p-3">
        <h3 className="text-sm font-semibold">{selected.symbol} · {selected.right} · {selected.expiration}</h3>
        {coincident && coincident.length > 1 ? <label className="grid gap-1 text-xs">Contracts at this point<Select aria-label="Contracts at this point" value={selected.id} onChange={event => setSelectedId(event.target.value)}>{coincident.map(row => <option key={row.id} value={row.id}>{row.symbol} · strike {row.strike}</option>)}</Select></label> : null}
        <dl className="grid gap-2 text-xs md:grid-cols-3">{Object.entries({ "Bid / ask per share": `${selected.bid} / ${selected.ask} ${selected.currency}`, "Spread / mid fraction": selected.spreadFraction ?? "Unavailable", "Open interest / volume": `${selected.openInterest ?? "Unavailable"} / ${selected.volume ?? "Unavailable"}`, "Contract multiplier": selected.multiplier, "Style / exchange": `${selected.style} / ${selected.exchange}`, "Quote source / observed": `${selected.source} / ${selected.observedAt}` }).map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="break-words font-mono">{value}</dd></div>)}</dl>
        <p className="text-xs">{selected.passesScreen ? "Passes selected screen criteria." : `Rejection reasons: ${selected.rejectionReasons.join("; ")}`}</p>
        <div className="flex flex-wrap gap-2"><label className="grid gap-1 text-xs">Basket destination<Select aria-label="Basket destination" value={activeLeg} onChange={event => setActiveLeg(event.target.value)}><option value="new">New leg</option>{basket.legs.filter(leg => leg.instrumentType === "Option").map(leg => <option key={leg.legId} value={leg.legId}>Replace {leg.legId}: {leg.symbol}</option>)}</Select></label><Button variant="outline" onClick={addSelected} disabled={activeLeg === "new" && basket.legs.length >= 50}>Use selected contract</Button></div>
      </section> : null}
    </CardContent></Card> : null}

    <Card><CardHeader><CardTitle>Proposed strategy basket</CardTitle><CardDescription>Arbitrary call, put and equity legs, including uncovered selling. These are comparison terms; they submit no order. Specify exact deliverables from the broker evidence.</CardDescription></CardHeader><CardContent className="grid gap-3">
      <div className="grid gap-3 md:grid-cols-4">
        <label className="grid gap-1 text-xs">Basket quantity<Input value={basket.quantity} inputMode="decimal" onChange={event => setBasket(previous => ({ ...previous, quantity: event.target.value }))} /></label>
        <label className="grid gap-1 text-xs">Order basis<Select aria-label="Order basis" value={basket.orderType} onChange={event => setBasket(previous => ({ ...previous, orderType: event.target.value as "Limit" | "Market", limitPrice: event.target.value === "Market" ? null : "" }))}><option>Limit</option><option>Market</option></Select></label>
        <label className="grid gap-1 text-xs">Price effect<Select aria-label="Price effect" value={basket.priceEffect} onChange={event => setBasket(previous => ({ ...previous, priceEffect: event.target.value as OptionAssessmentBasket["priceEffect"] }))}><option>Debit</option><option>Credit</option><option>Even</option></Select></label>
        <label className="grid gap-1 text-xs">Net limit price<Input inputMode="decimal" disabled={basket.orderType === "Market"} value={basket.limitPrice ?? ""} onChange={event => setBasket(previous => ({ ...previous, limitPrice: event.target.value }))} /></label>
        <label className="grid gap-1 text-xs">Basket currency<Input value={basket.currency} onChange={event => setBasket(previous => ({ ...previous, currency: event.target.value }))} /></label>
      </div>
      {basket.legs.map(leg => <BasketLegEditor key={leg.legId} leg={leg} onChange={updated => setBasket(previous => ({ ...previous, legs: previous.legs.map(current => current.legId === updated.legId ? updated : current) }))} onRemove={() => { setBasket(previous => ({ ...previous, legs: previous.legs.filter(current => current.legId !== leg.legId) })); if (activeLeg === leg.legId) setActiveLeg("new"); }} />)}
      {!basket.legs.length ? <p className="text-xs">Select a map or table contract to add an option leg, or add an equity leg below. Canonical JSON and CSV imports can also describe a basket.</p> : null}
      <Button variant="outline" className="w-fit" disabled={basket.legs.length >= 50} onClick={() => addLeg(newEquityLeg)}>Add equity leg</Button>
    </CardContent></Card>
    <Card><CardHeader><CardTitle>Account and dated broker evidence</CardTitle><CardDescription>A matching imported assessment applies to its exact basket and account at the broker's stated time. It does not establish current acceptance.</CardDescription></CardHeader><CardContent className="grid gap-3">
      <label className="grid gap-1 text-xs">Owned fund account ID<Input value={account} onChange={event => setAccount(event.target.value)} placeholder="Select an account in the workstation scope or enter its UUID" /></label>
      <OptionAssessmentPanel key={account} account={account} basket={basket} basketKey={comparableBasket(basket)} onUseBasket={setBasket} />
    </CardContent></Card>
  </ScreenLayout>;
}

function BasketLegEditor({ leg, onChange, onRemove }: { leg: OptionBasketLeg; onChange: (leg: OptionBasketLeg) => void; onRemove: () => void }) {
  return <fieldset className="grid gap-2 border border-border p-3 md:grid-cols-4"><legend className="px-1 text-xs font-semibold">{leg.legId} · {leg.instrumentType}</legend>
    <label className="grid gap-1 text-xs">Symbol<Input value={leg.symbol} onChange={event => onChange({ ...leg, symbol: event.target.value })} /></label>
    <label className="grid gap-1 text-xs">Side<Select aria-label="Side" value={leg.side} onChange={event => onChange({ ...leg, side: event.target.value as "Buy" | "Sell" })}><option>Buy</option><option>Sell</option></Select></label>
    <label className="grid gap-1 text-xs">Position intent<Select aria-label="Position intent" value={leg.positionIntent} onChange={event => onChange({ ...leg, positionIntent: event.target.value as "Open" | "Close" })}><option>Open</option><option>Close</option></Select></label>
    <label className="grid gap-1 text-xs">Ratio quantity<Input value={leg.ratio} inputMode="decimal" onChange={event => onChange({ ...leg, ratio: event.target.value })} /></label>
    <label className="grid gap-1 text-xs">Leg currency<Input value={leg.currency} onChange={event => onChange({ ...leg, currency: event.target.value })} /></label>
    {leg.instrumentType === "Option" ? <><span className="text-xs md:col-span-2">{leg.underlyingSymbol} · {leg.right} · {leg.expiration} · strike {leg.strike} · multiplier {leg.multiplier}</span><label className="grid gap-1 text-xs md:col-span-4">Exact deliverable / settlement terms<Input value={leg.deliverable ?? ""} onChange={event => onChange({ ...leg, deliverable: event.target.value })} placeholder="Copy the contract deliverable from broker evidence; adjusted contracts may differ" /></label></> : null}
    <Button variant="ghost" className="w-fit" onClick={onRemove}>Remove {leg.legId}</Button>
  </fieldset>;
}

function OptionsMap({ data, selectedId, onSelect }: { data: OptionScreenResult; selectedId: string | null; onSelect: (id: string) => void }) {
  const groups = optionCoincidentGroups(data.candidates); const width = 900; const height = 320;
  const maximumDte = Math.max(1, data.criteria.maxDte, ...data.candidates.map(row => row.daysToExpiration));
  const x = (value: number) => 58 + value / maximumDte * 800; const y = (value: number) => 150 - value * 120;
  return <div className="overflow-x-auto"><svg viewBox={`0 0 ${width} ${height}`} className="min-w-[600px] w-full" role="group" aria-label="Options map. Days to expiry horizontally and signed delta vertically. Use the contract table for all values.">
    <rect x={x(data.criteria.minDte)} y={y(data.criteria.maxDelta)} width={Math.max(0, x(data.criteria.maxDte) - x(data.criteria.minDte))} height={Math.max(0, y(data.criteria.minDelta) - y(data.criteria.maxDelta))} fill="var(--chart-primary, #29323A)" fillOpacity="0.07" stroke="currentColor" strokeDasharray="5 4" />
    {[-1, -0.5, 0, 0.5, 1].map(value => <g key={value}><line x1="58" x2="858" y1={y(value)} y2={y(value)} stroke="currentColor" opacity="0.15" /><text x="48" y={y(value) + 4} textAnchor="end" fontSize="11" fill="currentColor">{value}</text></g>)}
    <line x1="58" y1="270" x2="858" y2="270" stroke="currentColor" />
    {[0, 0.25, 0.5, 0.75, 1].map(fraction => <text key={fraction} x={x(maximumDte * fraction)} y="288" textAnchor="middle" fontSize="11" fill="currentColor">{Math.round(maximumDte * fraction)}</text>)}
    <text x="450" y="311" textAnchor="middle" fontSize="12" fill="currentColor">Days to expiry</text><text x="12" y="155" transform="rotate(-90 12 155)" textAnchor="middle" fontSize="12" fill="currentColor">Signed delta</text>
    {groups.map(group => {
      const representative = group.find(row => row.id === selectedId) ?? group[0]; const selected = group.some(row => row.id === selectedId);
      const cx = x(representative.daysToExpiration); const cy = y(Number(representative.delta));
      const label = group.length > 1 ? `${group.length} coincident contracts at ${representative.daysToExpiration} DTE and delta ${representative.delta}` : `${representative.symbol}, ${representative.right}, ${representative.passesScreen ? "passes" : "rejected"}`;
      return <g key={group[0].id} role="button" tabIndex={0} aria-label={label} aria-pressed={selected} className="focus-visible:outline-2 focus-visible:outline-primary" onClick={() => onSelect(representative.id)} onKeyDown={event => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); onSelect(representative.id); } }} style={{ cursor: "pointer" }}>
        <title>{label}</title><circle cx={cx} cy={cy} r={selected ? 12 : 10} fill="transparent" stroke={selected ? "currentColor" : "none"} strokeWidth="2" />
        {representative.right === "Call" ? <circle cx={cx} cy={cy} r="5" fill={representative.passesScreen ? "currentColor" : "var(--color-background, white)"} stroke="currentColor" /> : <path d={`M ${cx} ${cy - 6} l 6 11 h -12 z`} fill={representative.passesScreen ? "currentColor" : "var(--color-background, white)"} stroke="currentColor" />}
        {group.length > 1 ? <text x={cx + 8} y={cy - 8} fontSize="11" fill="currentColor">{group.length}</text> : null}
      </g>;
    })}
  </svg></div>;
}
