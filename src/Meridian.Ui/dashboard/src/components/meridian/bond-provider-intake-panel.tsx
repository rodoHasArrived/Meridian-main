import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { FormGrid, FormRow } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { discoverBondIdentifier, importBondFredObservations } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import type { BondFredIntakeResult, BondIdentifierDiscoveryResult } from "@/types/bond-workflow.types";

const today = () => new Date().toISOString().slice(0, 10);
export function BondProviderIntakePanel({ discover = discoverBondIdentifier, importFred = importBondFredObservations }: {
  discover?: typeof discoverBondIdentifier; importFred?: typeof importBondFredObservations;
}) {
  const id = useId(); const [identifier, setIdentifier] = useState(""); const [kind, setKind] = useState<"ISIN" | "CUSIP">("ISIN"); const [sector, setSector] = useState("");
  const [evidence, setEvidence] = useState(""); const [document, setDocument] = useState("");
  const [from, setFrom] = useState(today); const [through, setThrough] = useState(today); const [vintage, setVintage] = useState(today);
  const [candidates, setCandidates] = useState<BondIdentifierDiscoveryResult | null>(null); const [fred, setFred] = useState<BondFredIntakeResult | null>(null);
  const [pending, setPending] = useState(false); const [error, setError] = useState<string | null>(null); const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  function edit(change: () => void) { active.current?.abort(); setPending(false); setError(null); setCandidates(null); setFred(null); change(); }
  async function run(isFred: boolean) {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setError(null); setCandidates(null); setFred(null); setPending(true);
    try {
      const options = { signal: controller.signal, allowDevelopmentFallback: false };
      if (isFred) {
        if (!evidence.trim() || !document.trim() || !from || !through || !vintage || from > through) throw new Error("Enter the accepted FRED binding document and ordered observation dates with an explicit vintage.");
        const result = await importFred({ evidenceUri: evidence.trim(), documentId: document.trim(), from, through, vintageDate: vintage }, options);
        if (!controller.signal.aborted) setFred(result);
      } else {
        if (!identifier.trim()) throw new Error("Enter an ISIN or CUSIP to discover bond candidates.");
        const result = await discover({ identifier: identifier.trim(), identifierType: kind, marketSector: sector.trim() || null }, options);
        if (!controller.signal.aborted) setCandidates(result);
      }
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Bond provider intake unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  return <div className="space-y-3"><details><summary className="cursor-pointer text-sm font-medium">Discover bond identity with OpenFIGI</summary><form aria-label="Bond identifier discovery" onSubmit={(event) => { event.preventDefault(); void run(false); }} className="mt-3 space-y-3"><p className="text-xs text-muted-foreground">Discover debt candidates by identifier. Ambiguous matches remain unresolved; this action does not create or amend a Security Master identity.</p><FormGrid columns={3}>
    <FormRow label="Bond identifier" labelFor={`${id}-identifier`}><Input id={`${id}-identifier`} value={identifier} onChange={(event) => edit(() => setIdentifier(event.target.value))} /></FormRow>
    <FormRow label="Bond identifier type" labelFor={`${id}-kind`}><Select id={`${id}-kind`} value={kind} onChange={(event) => edit(() => setKind(event.target.value as "ISIN" | "CUSIP"))}><option value="ISIN">ISIN</option><option value="CUSIP">CUSIP</option></Select></FormRow>
    <FormRow label="OpenFIGI market sector (optional)" labelFor={`${id}-sector`}><Input id={`${id}-sector`} value={sector} onChange={(event) => edit(() => setSector(event.target.value))} /></FormRow>
  </FormGrid><Button type="submit" variant="outline" disabled={pending}>Discover bond identifier candidates</Button>
    {candidates ? <section aria-label="Bond identifier candidates" className="space-y-2"><StatusBanner tone={candidates.isAmbiguous ? "warning" : "info"} title={candidates.isAmbiguous ? "Multiple bond candidates require source selection" : "Bond identifier discovery returned"} detail={`Verification evidence: ${candidates.evidenceStatus}. No identity was written.`} /><div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="sr-only">OpenFIGI bond identifier candidates</caption><thead><tr>{["FIGI", "Name", "Security type", "Market sector", "Ticker / exchange"].map((label) => <th className="p-2" key={label}>{label}</th>)}</tr></thead><tbody>{candidates.candidates.map((row, index) => <tr className="border-t border-border" key={index}><th className="p-2 font-normal">{row.figi}</th><td className="p-2">{row.name ?? "Unavailable"}</td><td className="p-2">{row.securityType ?? "Unavailable"}</td><td className="p-2">{row.marketSector ?? "Unavailable"}</td><td className="p-2">{row.ticker ?? "Unavailable"} · {row.exchangeCode ?? "Unavailable"}</td></tr>)}</tbody></table>{!candidates.candidates.length ? <p>No bond candidates returned.</p> : null}</div></section> : null}
  </form></details>
    <details><summary className="cursor-pointer text-sm font-medium">Import typed FRED observations</summary><form aria-label="Reviewed FRED observation intake" onSubmit={(event) => { event.preventDefault(); void run(true); }} className="mt-3 space-y-3"><p className="text-xs text-muted-foreground">Use an accepted provider-trust/fred binding. The reviewed binding supplies the index identity and units. Importing observations does not construct a discount curve.</p><FormGrid columns={2}>
      <FormRow label="Reviewed FRED binding evidence URI" labelFor={`${id}-fred-evidence`}><Input id={`${id}-fred-evidence`} value={evidence} onChange={(event) => edit(() => setEvidence(event.target.value))} /></FormRow>
      <FormRow label="Reviewed FRED binding document ID" labelFor={`${id}-fred-document`}><Input id={`${id}-fred-document`} value={document} onChange={(event) => edit(() => setDocument(event.target.value))} /></FormRow>
      <FormRow label="FRED observations from" labelFor={`${id}-from`}><Input id={`${id}-from`} type="date" value={from} onChange={(event) => edit(() => setFrom(event.target.value))} /></FormRow>
      <FormRow label="FRED observations through" labelFor={`${id}-through`}><Input id={`${id}-through`} type="date" value={through} onChange={(event) => edit(() => setThrough(event.target.value))} /></FormRow>
      <FormRow label="FRED vintage date" labelFor={`${id}-vintage`}><Input id={`${id}-vintage`} type="date" value={vintage} onChange={(event) => edit(() => setVintage(event.target.value))} /></FormRow>
    </FormGrid><Button type="submit" variant="outline" disabled={pending}>Import reviewed FRED observations</Button>
      {fred ? <section aria-label="FRED bond observation intake" className="space-y-2"><StatusBanner tone="info" title={`${fred.retainedObservations.appendedCount} FRED observations retained`} detail={`Verification evidence: ${fred.evidenceStatus}. Missing dates remain unavailable.`} />{fred.missingDates.length ? <p className="text-xs">Missing observation dates: {fred.missingDates.join(", ")}</p> : null}<details><summary className="cursor-pointer text-xs">Retained response and binding hashes</summary><p className="break-all font-mono text-xs">Response {fred.responseSha256}</p><p className="break-all font-mono text-xs">Reviewed binding {fred.bindingEvidenceSha256}</p></details></section> : null}
    </form></details>
    {error ? <StatusBanner role="alert" tone="danger" title="Bond provider intake unavailable" detail={error} /> : null}
  </div>;
}
