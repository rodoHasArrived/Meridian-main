import { useCallback, useEffect, useRef, useState } from "react";
import { Link, Navigate, useSearchParams } from "react-router-dom";
import { LotsTrackerPanel, SecurityDetailsPanel } from "@/components/meridian/security-details-tracker";
import { SecurityPassportEditorLauncher } from "@/components/meridian/security-passport-editor-launcher";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ScreenLayout } from "@/components/ui/screen-layout";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { TechnicalDetails } from "@/components/ui/technical-details";
import {
  getCorporateActions,
  getSecurityDetail,
  getSecurityIdentity,
  getTradingParameters,
  searchSecurities
} from "@/lib/api";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { ContractualCashFlowWorkbench } from "@/components/meridian/contractual-cash-flow-workbench";
import {
  buildAssetDetailOverviewViewState,
  selectExactSecuritySearchResult
} from "@/screens/asset-detail-screen.view-model";
import type { SecurityMasterEntry } from "@/types";
import { ApiError } from "@/lib/api-errors";
import { AssetSwitcher } from "@/components/meridian/asset-switcher";
import { AssetFinancialSummary } from "@/components/meridian/asset-financial-summary";
import { AssetPassportSummary } from "@/components/meridian/asset-passport-summary";
import { useAssetDetailResource } from "./asset-detail-resource";
import { AssetDetailEvents } from "./asset-detail-events";
import { SecurityInstrumentTerms } from "@/components/meridian/security-instrument-terms";
import { RecordIdentitySpine, useRecordWorkspaceContext } from "@/components/meridian/record-identity-spine";
import { StatusBanner } from "@/components/ui/status-banner";
import { instrumentDossierRoute } from "@/lib/instrument-dossier-route";
import { instrumentRecordIdentity } from "@/lib/instrument-record-identity";
import { appendRecordWorkspaceContext, readRecordWorkspaceContext } from "@/lib/record-workspace-context";
import { bondWorkflowRoute, isBondAssetClass, resolveBondWorkflowAction } from "@/lib/fixed-income-navigation";
import { BondSettlementPanel } from "@/components/meridian/bond-settlement-panel";

const assetSearchSuggestions = ["AAPL", "MSFT", "US91282CHT18"];
const assetDetailReadinessCues = [
  { label: "Identity", value: "Identifiers, aliases, and current status" },
  {
    label: "Passport",
    value: "Controlled edits, approvals, and trust evidence"
  },
  { label: "Lots", value: "Open lots, cost basis, and accounting impact" },
  {
    label: "Trading",
    value: "Lot size, tick size, hours, and circuit breakers"
  }
];
const assetExampleSearches = [
  {
    id: "equity-book",
    label: "Equity example",
    symbol: "AAPL",
    detail: "Identity, lots, trading parameters"
  },
  {
    id: "treasury",
    label: "Treasury example",
    symbol: "US91282CHT18",
    detail: "Fixed income identity and term review"
  },
  {
    id: "exceptions",
    label: "Microsoft example",
    symbol: "MSFT",
    detail: "Identity, terms, and available evidence"
  }
];
const assetDetailNextActions = [
  {
    id: "security-master",
    label: "Open Security Master",
    href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
    detail: "Review the security's identity, approvals, and passport status."
  },
  {
    id: "live-quotes",
    label: "Open live quotes",
    href: WORKSTATION_ROUTE_CATALOG.dataQuotes,
    detail: "Check current provider price and freshness before trading decisions."
  },
  {
    id: "reconciliation",
    label: "Open reconciliation",
    href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
    detail: "Inspect accounting impact for lots, cash, and unmatched records."
  }
];

export function AssetDetailScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  const securityId = searchParams.get("securityId");
  const initialQuery = searchParams.get("symbol") ?? searchParams.get("query") ?? "";

  const selectSecurity = useCallback(
    (id: string) =>
      setSearchParams((current) => {
        const next = new URLSearchParams(current);
        next.set("securityId", id);
        return next;
      }),
    [setSearchParams]
  );

  if (!securityId) return <AssetSearchPanel initialQuery={initialQuery} onSelect={selectSecurity} />;
  return <AssetDetailPanel key={securityId} securityId={securityId} />;
}

function AssetSearchPanel({
  initialQuery,
  onSelect
}: {
  initialQuery: string;
  onSelect: (securityId: string) => void;
}) {
  const [query, setQuery] = useState(initialQuery);
  const [results, setResults] = useState<SecurityMasterEntry[]>([]);
  const [searching, setSearching] = useState(false);
  const [searchError, setSearchError] = useState(false);
  const [searchAttempt, setSearchAttempt] = useState(0);
  const initialRouteQueryRef = useRef(initialQuery.trim());
  const autoOpenedRef = useRef(false);

  useEffect(() => {
    setQuery(initialQuery);
    initialRouteQueryRef.current = initialQuery.trim();
    autoOpenedRef.current = false;
  }, [initialQuery]);

  useEffect(() => {
    if (query.trim().length < 2) {
      setResults([]);
      setSearching(false);
      setSearchError(false);
      return;
    }

    let cancelled = false;
    setSearching(true);
    setResults([]);
    setSearchError(false);

    searchSecurities(query.trim())
      .then((entries) => {
        if (!cancelled) {
          setResults(entries);
          const routeQuery = initialRouteQueryRef.current;
          if (!autoOpenedRef.current && routeQuery && query.trim().toLowerCase() === routeQuery.toLowerCase()) {
            const exactSecurityId = selectExactSecuritySearchResult(routeQuery, entries);
            if (exactSecurityId) {
              autoOpenedRef.current = true;
              onSelect(exactSecurityId);
            }
          }
        }
      })
      .catch(() => {
        if (!cancelled) {
          setResults([]);
          setSearchError(true);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setSearching(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [onSelect, query, searchAttempt]);

  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Asset Detail</CardTitle>
        <CardDescription>
          Search for a security to view its identity, passport, lots, and trading parameters in one place.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="space-y-3">
          <Input
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Search by name, ticker, or identifier"
            aria-label="Search securities"
          />
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_18rem]" aria-label="Asset detail search readiness">
            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              {query.trim().length < 2 ? (
                <>
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="font-semibold text-foreground">Start from a known instrument</div>
                      <p className="mt-1 text-sm leading-6 text-muted-foreground">
                        Search pulls the selected Security Master record into identity, passport, lot, and
                        trading-parameter review.
                      </p>
                    </div>
                    <Button asChild size="sm" variant="outline">
                      <Link to={WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster}>Open Security Master</Link>
                    </Button>
                  </div>
                  <div className="mt-3 flex flex-wrap gap-2" aria-label="Suggested asset detail searches">
                    {assetSearchSuggestions.map((suggestion) => (
                      <Button
                        key={suggestion}
                        type="button"
                        size="sm"
                        variant="secondary"
                        onClick={() => setQuery(suggestion)}
                      >
                        {suggestion}
                      </Button>
                    ))}
                  </div>
                </>
              ) : (
                <>
                  <div className="font-semibold text-foreground">Search results</div>
                  <p className="mt-1 text-sm leading-6 text-muted-foreground">
                    Select a result to open its identity, passport, lot, and trading parameters without leaving the
                    operator workflow.
                  </p>
                  {searching ? (
                    <p role="status" className="mt-3 text-sm text-muted-foreground">
                      Searching...
                    </p>
                  ) : null}
                  {searchError ? (
                    <div role="alert" className="mt-3 text-sm">
                      <p>Couldn’t search securities.</p>
                      <Button size="sm" variant="outline" onClick={() => setSearchAttempt((value) => value + 1)}>
                        Retry search
                      </Button>
                    </div>
                  ) : null}
                  {!searching && !searchError && results.length === 0 ? (
                    <p role="status" className="mt-3 text-sm text-muted-foreground">
                      No securities match this search.
                    </p>
                  ) : null}
                  {results.length > 0 ? (
                    <ul className="mt-3 space-y-2" aria-label="Security search results">
                      {results.map((entry) => (
                        <li key={entry.securityId}>
                          <button
                            type="button"
                            className="w-full rounded-md border border-border/70 bg-background/70 px-3 py-2 text-left hover:border-primary/40"
                            onClick={() => onSelect(entry.securityId)}
                          >
                            <span className="block font-semibold text-foreground">{entry.displayName}</span>
                            <span className="mt-1 block font-mono text-xs text-muted-foreground">
                              {entry.classification.assetClass} -{" "}
                              {entry.classification.primaryIdentifierValue ?? entry.securityId}
                            </span>
                          </button>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                </>
              )}
            </div>
            <div className="rounded-md border border-border/70 bg-background/70 px-3 py-3">
              <div className="font-mono text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                Review areas
              </div>
              <div className="mt-2 grid gap-2">
                {assetDetailReadinessCues.map((cue) => (
                  <div key={cue.label} className="rounded border border-border/60 bg-secondary/20 px-2 py-1.5">
                    <div className="font-semibold text-foreground">{cue.label}</div>
                    <div className="mt-0.5 text-xs leading-5 text-muted-foreground">{cue.value}</div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>

        <aside className="space-y-4" aria-label="Asset detail example searches and next actions">
          <section className="space-y-2" aria-label="Asset detail example searches">
            <h3 className="text-sm font-semibold text-foreground">Example searches</h3>
            <ul className="grid gap-2">
              {assetExampleSearches.map((view) => (
                <li key={view.id} className="border border-border bg-background/60 px-3 py-2">
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-semibold text-foreground">{view.label}</span>
                    <Button type="button" size="sm" variant="ghost" onClick={() => setQuery(view.symbol)}>
                      {view.symbol}
                    </Button>
                  </div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{view.detail}</p>
                </li>
              ))}
            </ul>
          </section>
          <section className="space-y-2" aria-label="Asset detail next actions">
            <h3 className="text-sm font-semibold text-foreground">Next actions</h3>
            <ul className="grid gap-2">
              {assetDetailNextActions.map((action) => (
                <li key={action.id} className="border border-border bg-background/60 px-3 py-2">
                  <Link
                    className="text-xs font-semibold text-primary underline-offset-2 hover:underline"
                    to={action.href}
                  >
                    {action.label}
                  </Link>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{action.detail}</p>
                </li>
              ))}
            </ul>
          </section>
        </aside>
      </CardContent>
    </Card>
  );
}

const detailTabs = [
  { id: "overview", label: "Overview" },
  { id: "reference", label: "Reference data" },
  { id: "holdings", label: "Holdings & Lots" },
  { id: "events", label: "Events" },
  { id: "trading-parameters", label: "Trading Parameters" },
  { id: "passport", label: "Passport" },
  { id: "cash-flow-scenarios", label: "Cash-flow scenarios" }
];

export function AssetDetailPanel({ securityId, embedded = false }: { securityId: string; embedded?: boolean }) {
  const [params, setParams] = useSearchParams();
  const requestedBondAction = resolveBondWorkflowAction(params.get("bondAction"));
  const entryResource = useAssetDetailResource(securityId, getSecurityDetail);
  const identityResource = useAssetDetailResource(securityId, getSecurityIdentity);
  const eventsResource = useAssetDetailResource(securityId, getCorporateActions);
  const tradingResource = useAssetDetailResource(securityId, getTradingParameters);
  const recordEntry = entryResource.status === "ready" && entryResource.data?.securityId.toLowerCase() === securityId.toLowerCase() ? entryResource.data : null;
  const record = instrumentRecordIdentity(recordEntry, identityResource.status === "ready" ? identityResource.data : null);
  const recordContext = useRecordWorkspaceContext(record, { securityId, bondAction: requestedBondAction ? "" : null });
  const inheritedContext = readRecordWorkspaceContext(params.toString());
  const tabParameter = embedded ? "assetTab" : "tab";
  const requestedTab = params.get(tabParameter) === "details-lots" ? "holdings" : params.get(tabParameter);
  const activeTab = detailTabs.some((tab) => tab.id === requestedTab) ? requestedTab! : "overview";
  const [visitedTabs, setVisitedTabs] = useState(() => new Set([activeTab]));
  useEffect(() => {
    setVisitedTabs((current) => (current.has(activeTab) ? current : new Set([...current, activeTab])));
  }, [activeTab]);
  const keepTab = (tab: string) => activeTab === tab || visitedTabs.has(tab);
  const openTab = (tab: string) =>
    setParams((current) => {
      const next = new URLSearchParams(current);
      next.set(tabParameter, tab);
      return next;
    });
  const searchAgain = () =>
    setParams((current) => {
      const next = new URLSearchParams(current);
      ["securityId", "symbol", "query"].forEach((key) => next.delete(key));
      return next;
    });

  if (entryResource.status === "loading")
    return (
      <Card className="panel-surface" role="status" aria-busy="true">
        <CardHeader>
          <CardTitle>Loading asset detail</CardTitle>
          <CardDescription>Looking up {securityId}.</CardDescription>
        </CardHeader>
      </Card>
    );
  if (entryResource.status === "error" || !entryResource.data || entryResource.data.securityId.toLowerCase() !== securityId.toLowerCase()) {
    const notFound = entryResource.error instanceof ApiError && entryResource.error.status === 404;
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>{notFound ? "Asset not found" : "Couldn’t load asset detail"}</CardTitle>
          <CardDescription>
            {notFound
              ? `No security matching "${securityId}" was found in the Security Master.`
              : "The asset could not be retrieved. Its existence and current state are unknown."}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex gap-2">
          <Button size="sm" variant="outline" onClick={entryResource.retry}>
            Retry asset
          </Button>
          {!embedded ? <Button size="sm" variant="ghost" onClick={searchAgain}>
            Search assets
          </Button> : null}
        </CardContent>
      </Card>
    );
  }

  const entry = entryResource.data;
  const identity =
    identityResource.status === "ready" && identityResource.data?.securityId === securityId
      ? identityResource.data
      : null;
  const tradingParameters =
    tradingResource.status === "ready" && tradingResource.data?.securityId === securityId ? tradingResource.data : null;
  const events =
    eventsResource.status === "ready" ? eventsResource.data.filter((event) => event.securityId === securityId) : [];
  const invalidEvents = eventsResource.status === "ready" && events.length !== eventsResource.data.length;
  const safeEvents = invalidEvents
    ? {
        status: "error" as const,
        data: null,
        error: new Error("Event identity mismatch")
      }
    : eventsResource;
  const overview = buildAssetDetailOverviewViewState({
    entry,
    identity,
    corporateActions: events
  });
  const financialSummary = (
    <AssetFinancialSummary
      securityId={securityId}
      symbol={overview.symbol}
      currency={entry.economicDefinition.currency}
      identifierKind={entry.classification.primaryIdentifierKind}
      ledgerBookId={params.get("ledgerBookId")}
      accountId={params.get("accountId")}
      asOf={params.get("asOf")}
      scopeUnavailableReason={
        params.get("fundAccountId")
          ? "This view cannot bind fund-account scope to retained book positions. Open the scoped portfolio for holdings."
          : undefined
      }
    />
  );

  const quotes = workstationRouteWithQuery("dataQuotes", { symbol: record?.scope?.symbol });
  const registry = workstationRouteWithQuery("accountingSecurityMaster", { securityId });
  const linkedInstrument = inheritedContext?.record.scope?.securityId;
  const linkedTicker = inheritedContext?.record.scope?.symbol;
  const navigationContext = inheritedContext && (linkedInstrument === securityId || (!linkedInstrument && linkedTicker && linkedTicker === record?.scope?.symbol)) ? inheritedContext : recordContext;
  const relatedHref = (route: string) => navigationContext ? appendRecordWorkspaceContext(route, navigationContext) ?? route : route;
  const isBond = isBondAssetClass(entry.classification.assetClass);
  if (isBond && requestedBondAction) return <Navigate to={relatedHref(bondWorkflowRoute(entry.securityId, requestedBondAction))} replace />;

  return (
    <ScreenLayout
      title={overview.displayName}
      scope={`${overview.symbol} · ${entry.classification.assetClass} · ${entry.economicDefinition.currency}`}
      actions={
        <>
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              entryResource.retry();
              identityResource.retry();
              eventsResource.retry();
              tradingResource.retry();
            }}
            title="Reload server data and clear unsaved form inputs"
          >
            Reload asset
          </Button>
          {!embedded ? <Button asChild size="sm" variant="outline">
            <Link to={relatedHref(instrumentDossierRoute(entry.securityId, params.toString()))}>Open instrument dossier</Link>
          </Button> : null}
          <Button asChild size="sm" variant="outline">
            <Link
              to={relatedHref(quotes)}
            >
              Live quotes
            </Link>
          </Button>
          <Button asChild size="sm" variant="outline">
            <Link to={relatedHref(registry)}>Open Security Master</Link>
          </Button>
          {isBond ? <>
            <Button asChild size="sm" variant="outline"><Link to={relatedHref(bondWorkflowRoute(entry.securityId, "analytics"))}>Bond analytics</Link></Button>
            <Button asChild size="sm" variant="outline"><Link to={relatedHref(bondWorkflowRoute(entry.securityId, "migration"))}>Convention migration</Link></Button>
          </> : null}
        </>
      }
    >
      {requestedBondAction && !isBond ? <StatusBanner tone="warning" title="Bond workflow unavailable for this instrument" detail={`The selected Security Master record is ${entry.classification.assetClass}. Select a bond to use analytics or convention migration.`} /> : null}
      {record ? <RecordIdentitySpine record={record} context={recordContext} /> : null}
      {!embedded ? <AssetSwitcher
        securityId={securityId}
        symbol={overview.symbol}
        displayName={overview.displayName}
        onSelect={(target) =>
          setParams((current) => {
            const next = new URLSearchParams(current);
            next.set("securityId", target.securityId);
            next.set("symbol", target.symbol);
            next.delete("query");
            if (target.securityId !== securityId) next.delete("bondAction");
            return next;
          })
        }
      /> : null}
      <p className="text-xs text-muted-foreground">
        {embedded ? "Reloading clears unsaved inputs." : "Switching assets or reloading clears unsaved inputs."} Switching tabs keeps inputs for the current asset.
      </p>
      <Tabs tabs={detailTabs} value={activeTab} onValueChange={openTab}>
        <TabPanel>
          {activeTab === "overview" ? (
            <div className="space-y-4">
              <div className="grid items-start gap-4 xl:grid-cols-2">
                <AssetPassportSummary securityId={securityId} onOpenPassport={() => openTab("passport")} />
                {financialSummary}
              </div>
              <div className="grid gap-4 xl:grid-cols-2">
                <SecurityInstrumentTerms
                  securityId={securityId}
                  expectedVersion={entry.economicDefinition.version}
                  mode="summary"
                  onOpenFull={() => openTab("reference")}
                />
                <Card className="panel-surface">
                  <CardHeader>
                    <CardTitle>Corporate actions</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <AssetDetailEvents
                      resource={safeEvents}
                      retry={eventsResource.retry}
                      compact
                      onOpenEvents={() => openTab("events")}
                    />
                  </CardContent>
                </Card>
              </div>
            </div>
          ) : null}
        </TabPanel>
        <TabPanel>
          {keepTab("reference") ? (
            <div className="space-y-4">
              {activeTab === "reference" ? (
                <SecurityInstrumentTerms
                  securityId={securityId}
                  expectedVersion={entry.economicDefinition.version}
                  mode="full"
                />
              ) : null}
              {identityResource.status === "error" || (identityResource.status === "ready" && !identity) ? (
                <SectionFailure label="identity evidence" retry={identityResource.retry} />
              ) : null}
              {identityResource.status === "loading" ? (
                <p role="status" className="text-sm text-muted-foreground">
                  Loading identity evidence…
                </p>
              ) : null}
              {tradingResource.status === "error" ||
              (tradingResource.status === "ready" && tradingResource.data && !tradingParameters) ? (
                <SectionFailure label="trading parameters" retry={tradingResource.retry} />
              ) : null}
              {tradingResource.status === "loading" ? (
                <p role="status" className="text-sm text-muted-foreground">
                  Loading trading-parameter evidence…
                </p>
              ) : null}
              <SecurityDetailsPanel entry={entry} identity={identity} tradingParameters={tradingParameters} />
              <TechnicalDetails label="Technical identity">
                <dl className="grid gap-3 sm:grid-cols-2">
                  {overview.technicalFields.map((field) => (
                    <div key={field.label}>
                      <dt className="text-xs text-muted-foreground">{field.label}</dt>
                      <dd className="mt-1 break-words font-mono text-sm">{field.value}</dd>
                    </div>
                  ))}
                </dl>
              </TechnicalDetails>
            </div>
          ) : null}
        </TabPanel>
        <TabPanel>
          {keepTab("holdings") ? (
            <div className="space-y-4">
              {activeTab === "holdings" ? financialSummary : null}
              <LotsTrackerPanel securityId={securityId} currency={entry.economicDefinition.currency} />
            </div>
          ) : null}
        </TabPanel>
        <TabPanel>
          {activeTab === "events" ? (
            <Card className="panel-surface">
              <CardHeader>
                <CardTitle>Corporate actions and evidence</CardTitle>
                <CardDescription>
                  Recorded events for {overview.symbol}; dates and source lifecycle state remain separate.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <AssetDetailEvents resource={safeEvents} retry={eventsResource.retry} />
              </CardContent>
            </Card>
          ) : null}
        </TabPanel>
        <TabPanel>
          {keepTab("trading-parameters") ? (
            <div className="space-y-4"><Card className="panel-surface">
              <CardHeader>
                <CardTitle>Trading parameters</CardTitle>
                <CardDescription>
                  {tradingParameters
                    ? `As of ${tradingParameters.asOf ?? "unknown"}`
                    : "Security-specific execution conventions"}
                </CardDescription>
              </CardHeader>
              <CardContent>
                {tradingResource.status === "loading" ? (
                  <p role="status" className="text-sm text-muted-foreground">
                    Loading trading parameters…
                  </p>
                ) : tradingResource.status === "error" || (tradingResource.data && !tradingParameters) ? (
                  <SectionFailure label="trading parameters" retry={tradingResource.retry} />
                ) : tradingParameters ? (
                  <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                    {[
                      ["Lot size", tradingParameters.lotSize],
                      ["Tick size", tradingParameters.tickSize],
                      ["Contract multiplier", tradingParameters.contractMultiplier],
                      [
                        "Margin requirement",
                        tradingParameters.marginRequirementPct != null
                          ? `${tradingParameters.marginRequirementPct}%`
                          : null
                      ],
                      ["Trading hours (UTC)", tradingParameters.tradingHoursUtc],
                      [
                        "Circuit breaker threshold",
                        tradingParameters.circuitBreakerThresholdPct != null
                          ? `${tradingParameters.circuitBreakerThresholdPct}%`
                          : null
                      ]
                    ].map(([label, value]) => (
                      <div key={label}>
                        <dt className="text-xs text-muted-foreground">{label}</dt>
                        <dd className="mt-1 text-sm tabular-nums">{value ?? "Not set"}</dd>
                      </div>
                    ))}
                  </dl>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    No trading parameters are configured for this security.
                  </p>
                )}
              </CardContent>
            </Card>
            {isBond ? <details className="rounded border border-border p-4"><summary className="cursor-pointer font-semibold">Prepare bond settlement</summary><div className="mt-4"><BondSettlementPanel key={entry.securityId} securityId={entry.securityId} termsVersion={identity?.securityId === entry.securityId ? identity.version : null} /></div></details> : null}
            </div>
          ) : null}
        </TabPanel>
        <TabPanel>
          {keepTab("passport") ? (
            <SecurityPassportEditorLauncher
              securityId={securityId}
              symbol={overview.symbol}
              assetClass={entry.classification.assetClass}
            />
          ) : null}
        </TabPanel>
        <TabPanel>
          {keepTab("cash-flow-scenarios") ? (
            <ContractualCashFlowWorkbench
              subjectKind="Security"
              subjectId={securityId}
              subjectLabel={overview.displayName}
            />
          ) : null}
        </TabPanel>
      </Tabs>
    </ScreenLayout>
  );
}

function SectionFailure({ label, retry }: { label: string; retry: () => void }) {
  return (
    <div role="alert" className="flex flex-wrap items-center gap-3 border border-warning/40 px-3 py-2 text-sm">
      <p>Couldn’t load {label}. This section is unavailable.</p>
      <Button size="sm" variant="outline" onClick={retry}>
        Retry {label}
      </Button>
    </div>
  );
}
