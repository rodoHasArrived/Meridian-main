import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { Tabs, TabPanel } from "@/components/ui/tabs";
import { getAccountOversightContext, listAccountOversightBooks, provisionAccountOversight } from "@/lib/api/account-oversight.api";
import type { LedgerBook } from "@/types";
import type { OversightContext, OversightScope } from "@/types/account-oversight";
import { AccountCashCasesPanel } from "./accounting-screen.account-cash-cases";
import { AccountClosePricesPanel } from "./accounting-screen.account-close-prices";
import { AccountCashForecastsPanel } from "./accounting-screen.account-cash-forecasts";

const tabs = [
  { id: "cases", label: "Cash cases", panelId: "account-oversight-cases" },
  { id: "prices", label: "Close prices", panelId: "account-oversight-prices" },
  { id: "forecast", label: "Cash forecast", panelId: "account-oversight-forecast" }
];
const message = (reason: unknown) => reason instanceof Error ? reason.message : "Account oversight is unavailable.";

/** Account evidence stays within Accounting's existing reconciliation workstream. */
export function AccountOversightPanel() {
  const location = useLocation();
  const navigate = useNavigate();
  const query = new URLSearchParams(location.search);
  const fundProfileId = query.get("fundProfileId")?.trim() || undefined;
  const requestedBook = query.get("ledgerBookId")?.trim() || "";
  const requestedAccount = query.get("accountId")?.trim() || "";
  const activeTab = tabs.some(tab => tab.id === query.get("oversight")) ? query.get("oversight")! : "cases";
  const [books, setBooks] = useState<LedgerBook[]>([]);
  const [bookScope, setBookScope] = useState<string | undefined>(fundProfileId);
  const [loading, setLoading] = useState(true);
  const [booksError, setBooksError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const [context, setContext] = useState<OversightContext | null>(null);
  const [contextError, setContextError] = useState<string | null>(null);
  const [contextLoading, setContextLoading] = useState(false);
  const [dirty, setDirty] = useState({ cases: false, prices: false, forecast: false });
  const [provisioning, setProvisioning] = useState(false);
  const [provisionStatus, setProvisionStatus] = useState<{ error: boolean; text: string } | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setBooksError(null);
    listAccountOversightBooks(fundProfileId, { signal: controller.signal }).then(rows => {
      if (controller.signal.aborted) return;
      setBooks(rows);
      setBookScope(fundProfileId);
    }).catch(reason => {
      if (controller.signal.aborted) return;
      setBooks([]);
      setBookScope(fundProfileId);
      setBooksError(message(reason));
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [fundProfileId, reload]);

  const availableBooks = bookScope === fundProfileId ? books : [];
  const selectedBook = availableBooks.find(book => book.ledgerBookId.toLowerCase() === requestedBook.toLowerCase()
    && (!requestedAccount || book.fundStructureNodeId.toLowerCase() === requestedAccount.toLowerCase()));
  const bookId = selectedBook?.ledgerBookId;
  const accountId = selectedBook?.fundStructureNodeId;
  const baseCurrency = selectedBook?.baseCurrency;
  const scope = useMemo<OversightScope | null>(() => bookId && accountId && baseCurrency
    ? { ledgerBookId: bookId, accountId, baseCurrency } : null, [bookId, accountId, baseCurrency]);
  const scopeKey = scope ? `${scope.ledgerBookId}:${scope.accountId}:${scope.baseCurrency}` : "";
  const currentScopeKey = useRef(scopeKey);
  currentScopeKey.current = scopeKey;
  const provisionController = useRef<AbortController | null>(null);
  const matchingContext = context && scope && context.bookId === scope.ledgerBookId && context.accountId === scope.accountId
    && context.baseCurrency === scope.baseCurrency ? context : null;

  useEffect(() => {
    const controller = new AbortController();
    setContext(null);
    setContextError(null);
    setProvisionStatus(null);
    setProvisioning(false);
    setDirty({ cases: false, prices: false, forecast: false });
    setContextLoading(Boolean(scope));
    if (scope) {
      getAccountOversightContext(scope, { signal: controller.signal }).then(next => {
        if (!controller.signal.aborted) setContext(next);
      }).catch(reason => { if (!controller.signal.aborted) setContextError(message(reason)); })
        .finally(() => { if (!controller.signal.aborted) setContextLoading(false); });
    }
    return () => {
      controller.abort();
      provisionController.current?.abort();
    };
  }, [scope, reload]);

  const casesDirty = useCallback((value: boolean) => setDirty(current => current.cases === value ? current : { ...current, cases: value }), []);
  const pricesDirty = useCallback((value: boolean) => setDirty(current => current.prices === value ? current : { ...current, prices: value }), []);
  const forecastDirty = useCallback((value: boolean) => setDirty(current => current.forecast === value ? current : { ...current, forecast: value }), []);

  function selectBook(value: string) {
    if (Object.values(dirty).some(Boolean)
      && !window.confirm("Discard unsaved account observations and switch books? Retained history will remain available.")) return;
    const next = new URLSearchParams(location.search);
    const book = availableBooks.find(row => row.ledgerBookId === value);
    if (book) {
      next.set("ledgerBookId", book.ledgerBookId);
      next.set("accountId", book.fundStructureNodeId);
    } else {
      next.delete("ledgerBookId");
      next.delete("accountId");
    }
    navigate({ pathname: location.pathname, search: `?${next.toString()}`, hash: "#account-oversight" });
  }

  function selectTab(value: string) {
    const next = new URLSearchParams(location.search);
    next.set("oversight", value);
    navigate({ pathname: location.pathname, search: `?${next.toString()}`, hash: "#account-oversight" }, { replace: true });
  }

  async function provision() {
    if (!scope || !matchingContext?.canProvision || provisioning) return;
    const originalScope = scopeKey;
    const controller = new AbortController();
    provisionController.current = controller;
    setProvisioning(true);
    setProvisionStatus(null);
    try {
      await provisionAccountOversight(scope, { signal: controller.signal });
      if (!controller.signal.aborted && originalScope === currentScopeKey.current) setProvisionStatus({ error: false,
        text: "History initialized. Refresh cash cases or forecasts to load retained history." });
    } catch (reason) {
      if (!controller.signal.aborted && originalScope === currentScopeKey.current) setProvisionStatus({ error: true, text: message(reason) });
    } finally {
      if (!controller.signal.aborted && originalScope === currentScopeKey.current) setProvisioning(false);
    }
  }

  return <Card id="account-oversight" className="panel-surface scroll-mt-6" role="region" aria-label="Account oversight">
    <CardHeader>
      <CardTitle>Account oversight</CardTitle>
      <CardDescription>Keep cash cases, reviewed close prices and cash forecasts connected to one account and its retained evidence.</CardDescription>
    </CardHeader>
    <CardContent className="flex flex-col gap-4">
      <FormGrid columns={2}>
        <FormRow label="Account-owned ledger book" labelFor="oversight-book" hint="Choose the exact account book. Entity and fund books cannot substitute for account scope.">
          <Select id="oversight-book" value={selectedBook?.ledgerBookId ?? ""} disabled={loading || provisioning}
            onChange={event => selectBook(event.target.value)}>
            <option value="">Select an account book</option>
            {availableBooks.map(book => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName} · {book.baseCurrency}</option>)}
          </Select>
        </FormRow>
        {matchingContext ? <div className="flex flex-col justify-center gap-1 text-sm" aria-label="Verified account scope">
          <p>{matchingContext.bookName} · {matchingContext.baseCurrency}</p>
          <p className="break-all font-mono text-xs text-muted-foreground">Account {matchingContext.accountId}</p>
          <p className="text-xs text-muted-foreground">Signed in as {matchingContext.actor}{matchingContext.canWrite ? "" : " · Read only"}</p>
        </div> : null}
      </FormGrid>
      {loading ? <StatusBanner tone="info" role="status" title={<span className="text-foreground">Loading account books</span>} detail="Reading available ledger books before selecting account evidence." /> : null}
      {booksError ? <StatusBanner tone="danger" role="alert" title={<span className="text-foreground">Account books unavailable</span>} detail={booksError} /> : null}
      {!loading && !booksError && availableBooks.length === 0 ? <StatusBanner tone="warning" title={<span className="text-foreground">No account-owned books available</span>} detail="Create or select an account-owned ledger book in Accounting configuration before capturing account evidence." /> : null}
      {!loading && requestedBook && !selectedBook && !booksError ? <StatusBanner tone="warning" role="alert" title={<span className="text-foreground">Requested account scope is unavailable</span>} detail="Select an accessible account-owned book. No other account has been selected automatically." /> : null}
      {!requestedBook && availableBooks.length > 0 ? <p className="text-sm text-muted-foreground">Select an account book to load retained cases, prices and forecasts.</p> : null}
      {contextLoading ? <StatusBanner tone="info" role="status" title={<span className="text-foreground">Verifying account access</span>} detail="Checking retained account ownership and your current permissions." /> : null}
      {contextError ? <StatusBanner tone="danger" role="alert" title={<span className="text-foreground">Account evidence unavailable</span>} detail={contextError} /> : null}
      {booksError || contextError ? <div><Button type="button" size="sm" variant="outline" onClick={() => setReload(value => value + 1)}>Retry account access</Button></div> : null}
      {matchingContext && scope ? <div key={scopeKey} className="flex flex-col gap-4">
        {matchingContext.canProvision ? <div className="flex flex-wrap items-center gap-3">
          <Button type="button" variant="outline" size="sm" disabled={provisioning} busy={provisioning} onClick={() => void provision()}>Initialize oversight history</Button>
          <p className="text-xs text-muted-foreground">Initializes empty history only. Missing or corrupt initialized history requires recovery.</p>
        </div> : null}
        {provisionStatus ? <StatusBanner role={provisionStatus.error ? "alert" : "status"} tone={provisionStatus.error ? "danger" : "success"}
          title={<span className="text-foreground">{provisionStatus.error ? "History initialization failed" : "History initialized"}</span>} detail={provisionStatus.text} /> : null}
        <Tabs tabs={tabs} value={activeTab} onValueChange={selectTab}>
          <TabPanel><AccountCashCasesPanel scope={scope} context={matchingContext} onDirtyChange={casesDirty} /></TabPanel>
          <TabPanel><AccountClosePricesPanel scope={scope} context={matchingContext} onDirtyChange={pricesDirty} /></TabPanel>
          <TabPanel><AccountCashForecastsPanel scope={scope} context={matchingContext} onDirtyChange={forecastDirty} /></TabPanel>
        </Tabs>
      </div> : null}
    </CardContent>
  </Card>;
}
