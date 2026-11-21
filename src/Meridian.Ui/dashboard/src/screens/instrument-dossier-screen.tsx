import { useEffect, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { AssetDetailPanel } from "@/screens/asset-detail-screen";
import { FinancialValue } from "@/components/meridian/financial-value";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { getSecurityDetail } from "@/lib/api";
import { getLedgerBooks, getLedgerPeriods, getLedgerPeriodJournalEntries } from "@/lib/ledger-reports-api";
import { parseExactDecimal } from "@/lib/exact-financial-value";
import { instrumentCases, instrumentPositions, isLedgerInstrumentId, readInstrumentDossierTab, sameInstrument } from "./instrument-dossier.view-model";
import type { AccountingWorkspaceResponse, BrokerageHouseholdPortfolio, LedgerBook, LedgerPeriod, LedgerPostedJournalEntry, PortfolioWorkspaceResponse, SecurityMasterEntry, TradingWorkspaceResponse } from "@/types";

interface InstrumentDossierScreenProps {
  portfolio?: PortfolioWorkspaceResponse | null;
  trading?: TradingWorkspaceResponse | null;
  accounting?: AccountingWorkspaceResponse | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
}

/** Investigation keeps source scope; it never substitutes a symbol match for identity. */
export function InstrumentDossierScreen({ portfolio, trading, accounting, brokeragePortfolio }: InstrumentDossierScreenProps) {
  const { id = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const tab = readInstrumentDossierTab(params.get("tab"));
  const [entry, setEntry] = useState<SecurityMasterEntry | null>(null);
  const [identityState, setIdentityState] = useState<"loading" | "ready" | "error">("loading");
  const accountId = params.get("fundAccountId");
  const periodId = params.get("periodId");

  useEffect(() => {
    const controller = new AbortController();
    setEntry(null);
    setIdentityState("loading");
    getSecurityDetail(id, { signal: controller.signal, allowDevelopmentFallback: false }).then((result) => {
      if (!controller.signal.aborted) {
        setEntry(result);
        setIdentityState(sameInstrument(result.securityId, id) ? "ready" : "error");
      }
    }).catch(() => { if (!controller.signal.aborted) setIdentityState("error"); });
    return () => controller.abort();
  }, [id]);

  if (identityState === "loading" || entry && !sameInstrument(entry.securityId, id)) return <p role="status">Loading instrument dossier…</p>;
  if (identityState === "error" || !entry) return <Card><CardHeader><h1 className="card-title font-semibold">Instrument unavailable</h1><CardDescription>The requested Security Master identity could not be loaded. No symbol substitution was applied.</CardDescription></CardHeader></Card>;

  const positionState = instrumentPositions(id, accountId, brokeragePortfolio, portfolio?.positions ?? trading?.positions ?? []);
  const cases = instrumentCases(id, accounting?.breakQueue ?? [], accountId, periodId, params.get("ledgerBookId"));
  const changeTab = (value: string) => setParams((current) => {
    const next = new URLSearchParams(current);
    next.set("tab", value);
    return next;
  });
  const allMarksCurrent = positionState.rows.length > 0 && positionState.rows.every((row) => row.markStatus === "Current");
  return <div className="min-w-0 space-y-4">
    <header className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-3">
      <div className="min-w-0"><p className="text-xs text-muted-foreground">Instrument dossier</p><h1 className="text-xl font-semibold">{entry.displayName}</h1><p className="break-all font-mono text-xs text-muted-foreground">{id} · {entry.classification.primaryIdentifierValue ?? "Identifier not recorded"} · {entry.economicDefinition.currency}</p></div>
      <div className="flex flex-wrap gap-2"><Badge variant="outline">{entry.status}</Badge><Badge variant={allMarksCurrent ? "success" : "warning"}>{allMarksCurrent ? "Marks current" : positionState.rows.length > 0 ? "Marks need review" : "Mark unavailable"}</Badge></div>
    </header>
    <p className="text-sm text-muted-foreground">{accountId ? `Account ${accountId}` : "All loaded authorized accounts"} · {periodId ? `Period ${periodId}` : "No ledger period selected"}. Source quantities and values are shown per account without combining currencies or positions.</p>
    <Tabs value={tab} onValueChange={changeTab} tabs={[{ id: "reference", label: "Reference" }, { id: "positions", label: "Positions" }, { id: "ledger", label: "Ledger lines" }, { id: "cases", label: "Cases" }]}>
      <TabPanel>{tab === "reference" ? <AssetDetailPanel securityId={id} embedded /> : null}</TabPanel>
      <TabPanel><Card><CardHeader><h2 className="card-title font-semibold">Positions by account</h2><CardDescription>Only retained Security Master identities establish an association.</CardDescription></CardHeader><CardContent>
        {positionState.unlinkedCount > 0 ? <p className="mb-3 text-sm text-muted-foreground">{positionState.unlinkedCount} loaded position(s) have no retained instrument identity and are excluded from this dossier.</p> : null}
        {!portfolio && !trading && !brokeragePortfolio ? <p>Position sources are unavailable.</p> : positionState.rows.length === 0 ? <p>No identity-linked positions were found in the loaded authorized accounts.</p> : <div className="overflow-x-auto"><table className="w-full text-left text-sm"><caption className="sr-only">Instrument positions by account</caption><thead><tr><th className="p-2">Account</th><th className="p-2">Quantity</th><th className="p-2">Mark</th><th className="p-2">Market value / exposure</th><th className="p-2">Mark condition</th></tr></thead><tbody>{positionState.rows.map((row) => <tr key={row.id} className="border-t border-border"><th scope="row" className="p-2 font-normal">{row.account}</th><td className="p-2"><DossierValue value={row.quantity} unit="units" /></td><td className="p-2"><DossierValue value={row.price} currency={row.currency} /></td><td className="p-2"><DossierValue value={row.value} currency={row.currency} /></td><td className="max-w-64 p-2"><span>{row.markStatus}</span><p className="text-xs text-muted-foreground">{row.markReason}</p></td></tr>)}</tbody></table></div>}
      </CardContent></Card></TabPanel>
      <TabPanel>{tab === "ledger" ? <InstrumentLedger instrumentId={id} /> : null}</TabPanel>
      <TabPanel><Card><CardHeader><h2 className="card-title font-semibold">Reconciliation cases</h2><CardDescription>Cases require explicit instrument lineage and retain their own accounting scope.</CardDescription></CardHeader><CardContent>
        {!accounting ? <p>Reconciliation source is unavailable.</p> : cases.length === 0 ? <p>No cases with retained instrument lineage were found in the loaded scope. Symbol-only and unlinked cases are not assigned to this instrument.</p> : <ul className="space-y-2">{cases.map((item) => {
          const caseParams = new URLSearchParams(params);
          caseParams.delete("tab");
          caseParams.set("reconciliationBreak", item.breakId);
          caseParams.set("reconciliationRun", item.runId);
          if (item.fundAccountId) caseParams.set("fundAccountId", item.fundAccountId);
          return <li key={item.breakId}><Link className="underline" to={`/accounting/reconciliation?${caseParams}`}>{item.reason}</Link><span className="ml-2 text-xs text-muted-foreground">{item.status} · {item.assignedTo ?? "Owner not recorded"}</span></li>;
        })}</ul>}
      </CardContent></Card></TabPanel>
    </Tabs>
  </div>;
}

function DossierValue({ value, currency, unit }: { value: string | number; currency?: string; unit?: string }) {
  // Legacy display strings such as "$1,000" have already been formatted upstream.
  if (typeof value === "string" && !/^[+-]?\d+(?:\.\d+)?(?:e[+-]?\d+)?$/i.test(value.trim())) return <span className="font-mono">{value}<span className="block text-[10px] text-muted-foreground">Source-formatted value</span></span>;
  return <FinancialValue value={value} currency={currency} unit={unit ?? (!currency ? "Unit not recorded" : undefined)} decimals={unit === "units" ? Math.min(28, parseExactDecimal(value)?.scale ?? 0) : undefined} condition="unavailable" reason="This legacy source has not supplied an amount-level data condition or exact scoped proof." />;
}

function InstrumentLedger({ instrumentId }: { instrumentId: string }) {
  const [params, setParams] = useSearchParams();
  const ledgerBookId = params.get("ledgerBookId") ?? "";
  const periodId = params.get("periodId") ?? "";
  const accountId = params.get("fundAccountId");
  const [books, setBooks] = useState<LedgerBook[]>([]);
  const [periods, setPeriods] = useState<LedgerPeriod[]>([]);
  const [entries, setEntries] = useState<LedgerPostedJournalEntry[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    setError(null);
    getLedgerBooks({ signal: controller.signal, allowDevelopmentFallback: false }).then((result) => { if (!controller.signal.aborted) setBooks(result); }).catch(() => { if (!controller.signal.aborted) setError("Ledger books could not be loaded."); });
    return () => controller.abort();
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    setPeriods([]);
    setError(null);
    if (ledgerBookId) getLedgerPeriods({ ledgerBookId }, { signal: controller.signal, allowDevelopmentFallback: false }).then((result) => { if (!controller.signal.aborted) setPeriods(result); }).catch(() => { if (!controller.signal.aborted) setError("Periods could not be loaded for the selected book."); });
    return () => controller.abort();
  }, [ledgerBookId]);
  const selectedPeriod = periods.find((period) => sameInstrument(period.periodId, periodId) && sameInstrument(period.ledgerBookId, ledgerBookId));
  useEffect(() => {
    const controller = new AbortController();
    setEntries([]);
    setLoading(false);
    if (selectedPeriod && isLedgerInstrumentId(instrumentId)) {
      setError(null);
      setLoading(true);
      getLedgerPeriodJournalEntries(selectedPeriod.periodId, { instrumentId, ...(accountId ? { accountId } : {}) }, { signal: controller.signal, allowDevelopmentFallback: false }).then((result) => { if (!controller.signal.aborted) setEntries(result); }).catch(() => { if (!controller.signal.aborted) setError("Instrument-filtered ledger lines could not be loaded. No unfiltered fallback was used."); }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }
    return () => controller.abort();
  }, [instrumentId, selectedPeriod, accountId]);
  const book = books.find((item) => sameInstrument(item.ledgerBookId, ledgerBookId));
  const lines = entries.filter((entry) => sameInstrument(entry.ledgerBookId, ledgerBookId) && sameInstrument(entry.periodId, periodId)).flatMap((entry) => entry.lines.filter((line) => sameInstrument(line.dimensions?.instrumentId, instrumentId)
    && (!accountId || sameInstrument(line.financialAccountId ?? line.dimensions?.accountId, accountId))));
  const changeScope = (key: "ledgerBookId" | "periodId", value: string) => setParams((current) => { const next = new URLSearchParams(current); if (value) next.set(key, value); else next.delete(key); if (key === "ledgerBookId") next.delete("periodId"); return next; });
  return <Card><CardHeader><h2 className="card-title font-semibold">Instrument ledger lines</h2><CardDescription>The ledger retains Security Master identity in its instrument dimension; symbol text does not establish this link.</CardDescription></CardHeader><CardContent className="space-y-3">
    <div className="grid gap-3 md:grid-cols-2"><label className="text-sm">Ledger book<Select value={ledgerBookId} onChange={(event) => changeScope("ledgerBookId", event.target.value)}><option value="">Choose ledger book</option>{books.map((item) => <option key={item.ledgerBookId} value={item.ledgerBookId}>{item.displayName} · {item.baseCurrency}</option>)}</Select></label><label className="text-sm">Period<Select value={periodId} disabled={!ledgerBookId} onChange={(event) => changeScope("periodId", event.target.value)}><option value="">Choose period</option>{periods.map((item) => <option key={item.periodId} value={item.periodId}>{item.label}</option>)}</Select></label></div>
    {!isLedgerInstrumentId(instrumentId) ? <p>Ledger lineage is unavailable: this record does not have the canonical Security Master GUID required by the ledger.</p> : null}
    {error ? <p role="alert">{error}</p> : null}
    {loading ? <p role="status">Loading instrument ledger lines…</p> : selectedPeriod && isLedgerInstrumentId(instrumentId) && !error && lines.length === 0 ? <p>No retained instrument-linked lines were found in this period. Missing lineage is not replaced by a symbol search.</p> : null}
    {lines.length > 0 ? <div className="overflow-x-auto"><table className="w-full text-left text-sm"><caption className="sr-only">Instrument-linked posted ledger lines</caption><thead><tr><th className="p-2">Account</th><th className="p-2">Date</th><th className="p-2">Description</th><th className="p-2">Debit</th><th className="p-2">Credit</th></tr></thead><tbody>{lines.map((line) => <tr key={`${line.journalEntryId}:${line.entryId}`} className="border-t border-border"><th scope="row" className="p-2 font-normal">{line.accountName}</th><td className="p-2">{line.timestamp.slice(0, 10)}</td><td className="p-2">{line.description}</td>{(["debit", "credit"] as const).map((side) => <td key={side} className="p-2"><FinancialValue value={line[side]} currency={book?.baseCurrency} condition="unavailable" reason="This legacy numeric wire has not supplied an exact decimal or amount-level condition." passport={book ? { subjectId: `${line.journalEntryId}:${line.entryId}:${side}`, ledgerBookId, periodId, fundProfileId: book.fundProfileId, amount: line[side], currency: book.baseCurrency, label: `${line.accountName} ${side}` } : null} /></td>)}</tr>)}</tbody></table></div> : null}
  </CardContent></Card>;
}
