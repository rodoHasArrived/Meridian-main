import { useEffect, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { AssetDetailPanel } from "@/screens/asset-detail-screen";
import { FinancialValue } from "@/components/meridian/financial-value";
import { RecordIdentitySpine, useRecordWorkspaceContext } from "@/components/meridian/record-identity-spine";
import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { getSecurityDetail } from "@/lib/api";
import { getLedgerBooks, getLedgerPeriods, getLedgerPeriodJournalEntries } from "@/lib/ledger-reports-api";
import { parseExactDecimal } from "@/lib/exact-financial-value";
import { instrumentRecordIdentity } from "@/lib/instrument-record-identity";
import { describeBrokerageHouseholdSource, describeLiveHoldingsSource, positionSourceAttributes, type PositionSource } from "@/lib/position-source";
import { narrowRowsWithFundScopeContext, useFundScopeNarrowingContext } from "@/lib/operating-scope/fund-scope-context";
import { useFundAccountScopeSelection } from "@/lib/operating-scope/scope-picker-context";
import type { AmountPassportReference } from "@/types/amount-passport";
import { instrumentCases, instrumentPositions, isLedgerInstrumentId, readInstrumentDossierTab, sameInstrument, type InstrumentPositionRow } from "./instrument-dossier.view-model";
import type { AccountingWorkspaceResponse, BrokerageHouseholdPortfolio, LedgerBook, LedgerPeriod, LedgerPostedJournalEntry, LedgerPostedJournalEntryLine, PortfolioWorkspaceResponse, SecurityMasterEntry, TradingWorkspaceResponse } from "@/types";

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
  const accountScope = useFundAccountScopeSelection();
  const accountId = params.get("fundAccountId") ?? accountScope?.scope?.fundAccountId ?? null;
  const periodId = params.get("periodId");
  const fundScope = useFundScopeNarrowingContext();
  const record = instrumentRecordIdentity(entry, null);
  const recordContext = useRecordWorkspaceContext(record, { securityId: id });

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

  const brokerageScope = narrowRowsWithFundScopeContext(fundScope, brokeragePortfolio?.positions ?? [], row => row.fundAccountId, "positions");
  const liveScope = narrowRowsWithFundScopeContext(fundScope, portfolio?.positions ?? trading?.positions ?? [], row => row.markFreshness?.financialAccountId, "positions");
  const caseScope = narrowRowsWithFundScopeContext(fundScope, accounting?.breakQueue ?? [], row => row.fundAccountId, "cases");
  const positionState = instrumentPositions(id, accountId, brokeragePortfolio ? { ...brokeragePortfolio, positions: brokerageScope.rows } : null, liveScope.rows);
  const positionSource: PositionSource = brokeragePortfolio ? describeBrokerageHouseholdSource(null)
    : portfolio ? { ...describeLiveHoldingsSource(), label: "Portfolio holdings" }
    : { kind: "trading-workspace", label: "Trading workspace positions", accountRef: null };
  const cases = instrumentCases(id, caseScope.rows, accountId, periodId, params.get("ledgerBookId"));
  const changeTab = (value: string) => setParams((current) => {
    const next = new URLSearchParams(current);
    next.set("tab", value);
    return next;
  });
  const allMarksCurrent = positionState.rows.length > 0 && positionState.rows.every((row) => row.markStatus === "Current"
    && row.price !== null && row.pricePassport?.freshness !== "stale" && row.pricePassport?.freshness !== "blocked");
  return <div className="min-w-0 space-y-4">
    <header className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-3">
      <div className="min-w-0"><p className="text-xs text-muted-foreground">Instrument dossier</p><h1 className="text-xl font-semibold">{entry.displayName}</h1><p className="break-all font-mono text-xs text-muted-foreground">{entry.classification.primaryIdentifierValue ?? "Identifier not recorded"} · {entry.economicDefinition.currency}</p></div>
      <div className="flex flex-wrap gap-2"><Badge variant="outline">{entry.status}</Badge><Badge variant={allMarksCurrent ? "success" : "warning"}>{allMarksCurrent ? "Marks current" : positionState.rows.length > 0 ? "Marks need review" : "Mark unavailable"}</Badge></div>
    </header>
    {record ? <RecordIdentitySpine record={record} context={recordContext} /> : null}
    <p className="text-sm text-muted-foreground">{accountId ? `Account ${accountId}` : "All loaded authorized accounts"} · {periodId ? `Period ${periodId}` : "No ledger period selected"}. Source quantities and values are shown per account without combining currencies or positions.</p>
    <Tabs value={tab} onValueChange={changeTab} tabs={[{ id: "reference", label: "Reference" }, { id: "positions", label: "Positions" }, { id: "ledger", label: "Ledger lines" }, { id: "cases", label: "Cases" }]}>
      <TabPanel>{tab === "reference" ? <AssetDetailPanel securityId={id} embedded /> : null}</TabPanel>
      <TabPanel><Card {...positionSourceAttributes(positionSource)}><CardHeader><h2 className="card-title font-semibold">Positions by account</h2><CardDescription>Only retained Security Master identities establish an association.</CardDescription></CardHeader><CardContent>
        {positionState.unlinkedCount > 0 ? <p className="mb-3 text-sm text-muted-foreground">{positionState.unlinkedCount} loaded position(s) have no retained instrument identity and are excluded from this dossier.</p> : null}
        {(brokeragePortfolio ? brokerageScope.note : liveScope.note) ? <p className="mb-3 text-sm text-muted-foreground">{brokeragePortfolio ? brokerageScope.note : liveScope.note}</p> : null}
        {!portfolio && !trading && !brokeragePortfolio ? <p>Position sources are unavailable.</p> : positionState.rows.length === 0 ? <p>No identity-linked positions were found in the loaded authorized accounts.</p> : <InstrumentPositionsTable rows={positionState.rows} sourceLabel={positionSource.label} sourceKey={JSON.stringify([id, accountId, fundScope?.entityId, fundScope?.fundId])} />}
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

function DossierValue({ value, currency, unit, amountPassport }: { value: string | number | null; currency?: string; unit?: string; amountPassport?: AmountPassportReference }) {
  if (typeof value === "string" && (value.trim() === "—" || value.trim() === "")) value = null;
  // Legacy display strings such as "$1,000" have already been formatted upstream.
  if (typeof value === "string" && !/^[+-]?\d+(?:\.\d+)?(?:e[+-]?\d+)?$/i.test(value.trim())) return <span className="font-mono">{value}<span className="block text-xs text-muted-foreground">Source-formatted value</span></span>;
  const condition = amountPassport?.freshness === "stale" ? "stale" : amountPassport?.freshness === "blocked" ? "review-required"
    : amountPassport?.origin === "estimated" ? "estimated" : amountPassport && amountPassport.origin !== "unavailable" ? "provisional" : "unavailable";
  const denomination = amountPassport ? amountPassport.currency ?? undefined : currency;
  return <FinancialValue value={value} currency={denomination} unit={unit ?? (!denomination ? "Unit not recorded" : undefined)} decimals={unit === "units" ? Math.min(28, parseExactDecimal(value)?.scale ?? 0) : undefined} condition={condition} summarizeUnavailableCondition amountPassport={amountPassport} freshness={amountPassport?.freshness ?? undefined} reason={amountPassport ? "Inspect the source-owned snapshot and its recorded condition in the Amount passport." : "This legacy source has not supplied an amount-level data condition or exact scoped proof."} />;
}

function InstrumentLedger({ instrumentId }: { instrumentId: string }) {
  const [params, setParams] = useSearchParams();
  const ledgerBookId = params.get("ledgerBookId") ?? "";
  const periodId = params.get("periodId") ?? "";
  const accountId = params.get("financialAccountId");
  const accountScope = useFundAccountScopeSelection();
  const unmappedBrokerageScope = Boolean((params.get("fundAccountId") ?? accountScope?.scope?.fundAccountId) && !accountId);
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
    if (selectedPeriod && isLedgerInstrumentId(instrumentId) && !unmappedBrokerageScope) {
      setError(null);
      setLoading(true);
      getLedgerPeriodJournalEntries(selectedPeriod.periodId, { instrumentId, ...(accountId ? { accountId } : {}) }, { signal: controller.signal, allowDevelopmentFallback: false }).then((result) => { if (!controller.signal.aborted) setEntries(result); }).catch(() => { if (!controller.signal.aborted) setError("Instrument-filtered ledger lines could not be loaded. No unfiltered fallback was used."); }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }
    return () => controller.abort();
  }, [instrumentId, selectedPeriod, accountId, unmappedBrokerageScope]);
  const book = books.find((item) => sameInstrument(item.ledgerBookId, ledgerBookId));
  const lines = unmappedBrokerageScope ? [] : entries.filter((entry) => sameInstrument(entry.ledgerBookId, ledgerBookId) && sameInstrument(entry.periodId, periodId)).flatMap((entry) => entry.lines.filter((line) => sameInstrument(line.dimensions?.instrumentId, instrumentId)
    && (!accountId || sameInstrument(line.financialAccountId ?? line.dimensions?.accountId, accountId))));
  const changeScope = (key: "ledgerBookId" | "periodId", value: string) => setParams((current) => { const next = new URLSearchParams(current); if (value) next.set(key, value); else next.delete(key); if (key === "ledgerBookId") next.delete("periodId"); return next; });
  const ledgerColumns: TableWorkbenchColumn<LedgerPostedJournalEntryLine>[] = [
    { id: "account", label: "Account", rowHeader: true, render: line => line.accountName, exportValue: line => line.accountName },
    { id: "date", label: "Date", render: line => line.timestamp.slice(0, 10), exportValue: line => line.timestamp },
    { id: "description", label: "Description", wrap: true, render: line => line.description, exportValue: line => line.description },
    ...(["debit", "credit"] as const).map(side => ({ id: side, label: side === "debit" ? "Debit" : "Credit", numeric: true,
      exportValue: (line: LedgerPostedJournalEntryLine) => line[side],
      render: (line: LedgerPostedJournalEntryLine) => {
        const reference = line[side] !== 0 ? line.amountPassport : null;
        return <FinancialValue value={reference?.exactValue ?? line[side]} currency={reference?.currency ?? book?.baseCurrency}
          condition={reference ? "provisional" : "unavailable"} amountPassport={reference}
          reason={reference ? "Inspect the exact retained posting scope and revision in its Amount passport." : "This legacy numeric wire has not supplied an exact decimal or amount-level condition."}
          passport={!reference && book && line[side] !== 0 ? { subjectId: `${line.journalEntryId}:${line.entryId}:${side}`, ledgerBookId, periodId, fundProfileId: book.fundProfileId,
            amount: line[side], currency: book.baseCurrency, label: `${line.accountName} ${side}` } : null} />;
      } }))
  ];
  return <Card><CardHeader><h2 className="card-title font-semibold">Instrument ledger lines</h2><CardDescription>The ledger retains Security Master identity in its instrument dimension; symbol text does not establish this link.</CardDescription></CardHeader><CardContent className="space-y-3">
    <div className="grid gap-3 md:grid-cols-2"><label className="text-sm">Ledger book<Select value={ledgerBookId} onChange={(event) => changeScope("ledgerBookId", event.target.value)}><option value="">Choose ledger book</option>{books.map((item) => <option key={item.ledgerBookId} value={item.ledgerBookId}>{item.displayName} · {item.baseCurrency}</option>)}</Select></label><label className="text-sm">Period<Select value={periodId} disabled={!ledgerBookId} onChange={(event) => changeScope("periodId", event.target.value)}><option value="">Choose period</option>{periods.map((item) => <option key={item.periodId} value={item.periodId}>{item.label}</option>)}</Select></label></div>
    {!isLedgerInstrumentId(instrumentId) ? <p>Ledger lineage is unavailable: this record does not have the canonical Security Master GUID required by the ledger.</p> : null}
    {unmappedBrokerageScope ? <div className="space-y-2"><p>Ledger review is waiting for an explicit financial-account selection. The selected brokerage account is distinct from a ledger financial account.</p><Button variant="outline" size="sm" onClick={() => setParams(current => { const next = new URLSearchParams(current); next.delete("fundAccountId"); next.set("recordScope", "1"); return next; })}>Clear brokerage scope for book-wide review</Button></div> : null}
    {error ? <p role="alert">{error}</p> : null}
    {loading ? <p role="status">Loading instrument ledger lines…</p> : selectedPeriod && isLedgerInstrumentId(instrumentId) && !unmappedBrokerageScope && !error && lines.length === 0 ? <p>No retained instrument-linked lines were found in this period. Missing lineage is not replaced by a symbol search.</p> : null}
    {lines.length > 0 ? <TableWorkbench<LedgerPostedJournalEntryLine>
      workbenchId="instrument-dossier-ledger" sourceKey={JSON.stringify([instrumentId, ledgerBookId, periodId, accountId])}
      rows={lines} getRowId={line => `${line.journalEntryId}:${line.entryId}`} columns={ledgerColumns}
      ariaLabel="Instrument-linked posted ledger lines" emptyText="No retained instrument-linked ledger lines."
      sourceDescription="Loaded instrument-linked postings from the selected book and period" completePopulation={false}
      exportLabel="Export loaded ledger rows" /> : null}
  </CardContent></Card>;
}

function InstrumentPositionsTable({ rows, sourceKey, sourceLabel }: { rows: InstrumentPositionRow[]; sourceKey: string; sourceLabel: string }) {
  const columns: TableWorkbenchColumn<InstrumentPositionRow>[] = [
    { id: "account", label: "Account", rowHeader: true, render: row => row.account, exportValue: row => row.account },
    { id: "quantity", label: "Quantity", numeric: true, render: row => <DossierValue value={row.quantity} unit="units" amountPassport={row.quantityPassport} />, exportValue: row => row.quantity },
    { id: "mark", label: "Mark", numeric: true, render: row => <DossierValue value={row.price} currency={row.currency} amountPassport={row.pricePassport} />, exportValue: row => row.price },
    { id: "value", label: "Market value / exposure", numeric: true, render: row => <DossierValue value={row.value} currency={row.currency} amountPassport={row.valuePassport} />, exportValue: row => row.value },
    { id: "condition", label: "Mark condition", wrap: true, render: row => <><span>{row.markStatus}</span><p className="text-xs text-muted-foreground">{row.markReason}</p>
      {row.markPolicyVersion ? <TechnicalDetails label="Mark policy"><span className="break-all font-mono text-xs">{row.markPolicyVersion}</span></TechnicalDetails> : null}</>,
      exportValue: row => [row.markStatus, row.markReason, row.markPolicyVersion].filter(Boolean).join(" · ") }
  ];
  return <div className="space-y-2">
    <p className="text-sm text-muted-foreground"><strong>Source</strong> {sourceLabel}</p>
    <p className="text-xs text-muted-foreground">Amounts without a source assessment have an unavailable data condition. See Mark condition for assessment details.</p>
    <TableWorkbench workbenchId="instrument-dossier-positions" sourceKey={sourceKey} columns={columns}
      rows={rows} getRowId={row => row.id} ariaLabel="Instrument positions by account" emptyText="No identity-linked positions."
      sourceDescription={`${sourceLabel} · loaded authorized positions associated by retained instrument identity`} completePopulation={false} exportLabel="Export loaded positions" />
  </div>;
}
