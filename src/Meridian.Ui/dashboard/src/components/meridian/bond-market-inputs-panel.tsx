import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { BondProviderIntakePanel } from "@/components/meridian/bond-provider-intake-panel";
import { getBondConnectionReadiness, importBondMarketObservations, queryBondMarketObservations } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText } from "@/lib/bond-decimal-format";
import type { BondConnectionCapability, BondMarketObservation } from "@/types/bond-workflow.types";

export function BondMarketInputsPanel({ securityId, accountId, onQuoteSelected, query = queryBondMarketObservations, importObservations = importBondMarketObservations }: {
  securityId: string; accountId?: string; onQuoteSelected: (quote: BondMarketObservation) => void;
  query?: typeof queryBondMarketObservations; importObservations?: typeof importBondMarketObservations;
}) {
  const id = useId(); const [rows, setRows] = useState<BondMarketObservation[] | null>(null); const [handoff, setHandoff] = useState("");
  const [pending, setPending] = useState(false); const [error, setError] = useState<string | null>(null); const [message, setMessage] = useState<string | null>(null);
  const [readiness, setReadiness] = useState<BondConnectionCapability[] | null>(null);
  const active = useRef<AbortController | null>(null); useEffect(() => () => active.current?.abort(), []);
  async function run(importing: boolean) {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setError(null); setMessage(null); setPending(true);
    try {
      const options = { signal: controller.signal, allowDevelopmentFallback: false };
      if (importing) {
        const request = JSON.parse(handoff) as { observations: unknown[] };
        if (!request || !Array.isArray(request.observations) || !request.observations.length) throw new Error("Supply a nonempty observations array with source evidence and explicit units.");
        const result = await importObservations(request, options);
        if (controller.signal.aborted) return; setMessage(`${result.appendedCount} source observations appended. Exact retries retain their original identity.`);
      }
      const response = await query(securityId, options);
      if (!controller.signal.aborted) setRows(response);
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Market inputs unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  async function loadReadiness() {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setError(null); setPending(true);
    try { const response = await getBondConnectionReadiness(securityId, accountId, { signal: controller.signal, allowDevelopmentFallback: false }); if (!controller.signal.aborted) setReadiness(response); }
    catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Connection readiness unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  return <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-medium">Retained quotes, fixings and market inputs</summary><div className="mt-3 space-y-3">
    <BondProviderIntakePanel />
    <Button type="button" variant="outline" disabled={pending} onClick={() => void run(false)}>Load retained market inputs</Button>
    <Button type="button" variant="outline" disabled={pending} onClick={() => void loadReadiness()}>Check configured bond input capabilities</Button>
    {error ? <StatusBanner role="alert" tone="danger" title="Market inputs unavailable" detail={error} /> : null}
    {message ? <StatusBanner role="status" tone="info" title={message} /> : null}
    {readiness ? <div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="mb-2 text-left">Configured connection capabilities · entitlement and verification are separate</caption><thead><tr>{["Connection / provider", "Input capability", "Configuration", "Entitlement", "Verification evidence", "Availability"].map((label) => <th className="p-2" key={label}>{label}</th>)}</tr></thead><tbody>{readiness.map((row, index) => <tr key={index} className="border-t border-border"><td className="p-2">{row.connectionId} · {row.providerId}</td><td className="p-2">{row.capability}</td><td className="p-2">{row.implemented ? "Implemented" : "Not implemented"} · {row.configured ? "Configured" : "Not configured"}</td><td className="p-2">{row.entitled === null ? "Unverified" : row.entitled ? "Entitled" : "Not entitled"}</td><td className="p-2">{row.evidenceStatus}{row.verifiedAtUtc ? ` · ${row.verifiedAtUtc}` : ""}</td><td className="p-2">{row.usable ? "Usable" : "Unavailable"}{row.blockers.map((blocker, blockerIndex) => <p key={blockerIndex}>{blocker}</p>)}</td></tr>)}</tbody></table>{!readiness.length ? <p>No configured connection capabilities are available for this security.</p> : null}</div> : null}
    {rows ? <div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="sr-only">Retained bond market observations</caption><thead><tr>{["Category", "Source", "Economic date", "Value / units", "Quote terms", "Use"].map((label) => <th key={label} className="p-2">{label}</th>)}</tr></thead><tbody>{rows.map((row) => <tr key={row.observationId} className="border-t border-border"><td className="p-2">{row.input.kind}</td><td className="p-2">{row.input.sourceName} · revision {row.input.revision}</td><td className="p-2">{row.input.economicDate}</td><td className="p-2">{row.input.rating ?? bondDecimalText(row.input.value)} · {row.input.unit}</td><td className="p-2">{row.input.quote ? <>{row.input.quote.category} · {row.input.quote.priceBasis} · settlement {row.input.quote.settlementDate}{row.input.quote.providerYield != null ? <p>Provider yield {bondDecimalText(row.input.quote.providerYield)} · {row.input.quote.providerYieldConvention}</p> : null}</> : "—"}</td><td className="p-2">{row.input.quote ? <Button type="button" size="sm" variant="outline" onClick={() => onQuoteSelected(row)}>Use retained quote</Button> : "Input evidence"}</td></tr>)}</tbody></table>{!rows.length ? <p>No retained observations for this security.</p> : null}</div> : null}
    <details><summary className="cursor-pointer text-sm">Import a market-observation evidence handoff</summary><label htmlFor={`${id}-handoff`} className="mt-2 block text-xs text-muted-foreground">JSON observations with provider record/revision, category, economic date, units, source evidence reference and SHA-256. Reference indices use their contractual series identity.</label><textarea id={`${id}-handoff`} className="mt-2 min-h-28 w-full rounded border border-border bg-background p-2 font-mono text-xs" value={handoff} onChange={(e) => { active.current?.abort(); setPending(false); setError(null); setHandoff(e.target.value); }} /><Button type="button" variant="outline" disabled={pending || !handoff.trim()} onClick={() => void run(true)}>Import retained market observations</Button></details>
  </div></details>;
}
