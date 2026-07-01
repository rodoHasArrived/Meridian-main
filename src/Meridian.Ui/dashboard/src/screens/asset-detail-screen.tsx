import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { LotsTrackerPanel, SecurityDetailsPanel } from "@/components/meridian/security-details-tracker";
import { SecurityPassportEditorLauncher } from "@/components/meridian/security-passport-editor-launcher";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { getCorporateActions, getSecurityDetail, getSecurityIdentity, getTradingParameters, searchSecurities } from "@/lib/api";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { buildAssetDetailOverviewViewState } from "@/screens/asset-detail-screen.view-model";
import type { CorporateAction, SecurityIdentityDrillIn, SecurityMasterEntry, TradingParameters } from "@/types";

const assetSearchSuggestions = ["AAPL", "MSFT", "US91282CHT18"];
const assetDetailReadinessCues = [
  { label: "Identity", value: "Identifiers, aliases, and current status" },
  { label: "Passport", value: "Governed edits, approvals, and trust evidence" },
  { label: "Lots", value: "Open lots, cost basis, and accounting impact" },
  { label: "Trading", value: "Lot size, tick size, hours, and circuit breakers" }
];
const assetSavedViews = [
  { id: "equity-book", label: "Equity book", symbol: "AAPL", detail: "Identity, lots, trading parameters" },
  { id: "treasury", label: "Treasury holdings", symbol: "US91282CHT18", detail: "Fixed income identity and term review" },
  { id: "exceptions", label: "Review queue", symbol: "MSFT", detail: "Assets with missing identifiers or stale trust evidence" }
];
const assetDetailNextActions = [
  { id: "security-master", label: "Open Security Master", href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster, detail: "Review governed identity, approvals, and passport posture." },
  { id: "live-quotes", label: "Open live quotes", href: WORKSTATION_ROUTE_CATALOG.dataQuotes, detail: "Check current provider price and freshness before trading decisions." },
  { id: "reconciliation", label: "Open reconciliation", href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation, detail: "Inspect accounting impact for lots, cash, and unmatched records." }
];

export function AssetDetailScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  const securityId = searchParams.get("securityId");
  const initialQuery = searchParams.get("symbol") ?? searchParams.get("query") ?? "";

  if (!securityId) {
    return <AssetSearchPanel initialQuery={initialQuery} onSelect={(id) => setSearchParams({ securityId: id })} />;
  }

  return <AssetDetailPanel securityId={securityId} />;
}

function AssetSearchPanel({ initialQuery, onSelect }: { initialQuery: string; onSelect: (securityId: string) => void }) {
  const [query, setQuery] = useState(initialQuery);
  const [results, setResults] = useState<SecurityMasterEntry[]>([]);
  const [searching, setSearching] = useState(false);

  useEffect(() => {
    setQuery(initialQuery);
  }, [initialQuery]);

  useEffect(() => {
    if (query.trim().length < 2) {
      setResults([]);
      return;
    }

    let cancelled = false;
    setSearching(true);

    searchSecurities(query.trim())
      .then((entries) => {
        if (!cancelled) {
          setResults(entries);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setResults([]);
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
  }, [query]);

  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Asset Detail</CardTitle>
        <CardDescription>Search for a security to view its identity, passport, lots, and trading parameters in one place.</CardDescription>
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
                        Search pulls the selected Security Master record into identity, passport, lot, and trading-parameter review.
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
                    Select a result to open governed identity, passport, lot, and trading parameters without leaving the operator workflow.
                  </p>
                  {searching ? <p className="mt-3 text-sm text-muted-foreground">Searching...</p> : null}
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
                              {entry.classification.assetClass} - {entry.classification.primaryIdentifierValue ?? entry.securityId}
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
              <div className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Review areas</div>
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

        <aside className="space-y-4" aria-label="Asset detail saved views and next actions">
          <section className="space-y-2" aria-label="Asset detail saved views">
            <h3 className="text-sm font-semibold text-foreground">Saved views</h3>
            <ul className="grid gap-2">
              {assetSavedViews.map((view) => (
                <li key={view.id} className="border border-border bg-background/60 px-3 py-2">
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-semibold text-foreground">{view.label}</span>
                    <Button type="button" size="sm" variant="ghost" onClick={() => setQuery(view.symbol)}>
                      {view.symbol}
                    </Button>
                  </div>
                  <p className="mt-1 text-[11px] leading-5 text-muted-foreground">{view.detail}</p>
                </li>
              ))}
            </ul>
          </section>
          <section className="space-y-2" aria-label="Asset detail next actions">
            <h3 className="text-sm font-semibold text-foreground">Next actions</h3>
            <ul className="grid gap-2">
              {assetDetailNextActions.map((action) => (
                <li key={action.id} className="border border-border bg-background/60 px-3 py-2">
                  <Link className="text-xs font-semibold text-primary underline-offset-2 hover:underline" to={action.href}>
                    {action.label}
                  </Link>
                  <p className="mt-1 text-[11px] leading-5 text-muted-foreground">{action.detail}</p>
                </li>
              ))}
            </ul>
          </section>
        </aside>
      </CardContent>
    </Card>
  );
}

function AssetDetailPanel({ securityId }: { securityId: string }) {
  const [entry, setEntry] = useState<SecurityMasterEntry | null>(null);
  const [identity, setIdentity] = useState<SecurityIdentityDrillIn | null>(null);
  const [corporateActions, setCorporateActions] = useState<CorporateAction[]>([]);
  const [tradingParameters, setTradingParameters] = useState<TradingParameters | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setNotFound(false);

    Promise.all([
      getSecurityDetail(securityId),
      getSecurityIdentity(securityId).catch(() => null),
      getCorporateActions(securityId).catch(() => []),
      getTradingParameters(securityId).catch(() => null)
    ])
      .then(([detail, identityResult, corporateActionsResult, tradingParametersResult]) => {
        if (cancelled) {
          return;
        }

        setEntry(detail);
        setIdentity(identityResult);
        setCorporateActions(corporateActionsResult);
        setTradingParameters(tradingParametersResult);
      })
      .catch(() => {
        if (!cancelled) {
          setEntry(null);
          setNotFound(true);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [securityId]);

  if (loading) {
    return (
      <Card
        className="panel-surface"
        role="status"
        aria-busy="true"
        aria-live="polite"
        aria-labelledby="asset-detail-loading-title"
      >
        <CardHeader>
          <CardTitle id="asset-detail-loading-title">Loading asset detail</CardTitle>
          <CardDescription>Looking up {securityId}.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (notFound || !entry) {
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Asset not found</CardTitle>
          <CardDescription>No security matching "{securityId}" was found in the Security Master.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const overview = buildAssetDetailOverviewViewState({ entry, identity, corporateActions });

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>{overview.displayName}</CardTitle>
            <CardDescription>{overview.symbol}</CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={overview.statusTone} dot>{overview.statusLabel}</Badge>
            <Button asChild size="sm" variant="outline">
              <Link to={workstationRouteWithQuery("dataQuotes", { symbol: overview.symbol })}>Live quotes</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to={WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster}>Open Security Master</Link>
            </Button>
          </div>
        </CardHeader>
      </Card>

      <Tabs
        tabs={[
          { id: "overview", label: "Overview" },
          { id: "passport", label: "Passport" },
          { id: "details-lots", label: "Details & Lots" },
          { id: "trading-parameters", label: "Trading Parameters" }
        ]}
      >
        <TabPanel>
          <Card className="panel-surface">
            <CardContent className="space-y-4 pt-4">
              <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {overview.fields.map((field) => (
                  <div key={field.label}>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
                    <dd className="mt-1 text-sm text-foreground">{field.value}</dd>
                  </div>
                ))}
              </dl>
              <div>
                <h3 className="text-sm font-semibold text-foreground">Corporate actions</h3>
                {overview.hasCorporateActions ? (
                  <ul className="mt-2 space-y-2" aria-label="Corporate actions">
                    {overview.corporateActions.map((action) => (
                      <li key={action.corpActId} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                        <span className="block text-sm font-semibold text-foreground">{action.label}</span>
                        <span className="mt-1 block text-xs text-muted-foreground">{action.detail}</span>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="mt-2 text-sm text-muted-foreground">No corporate actions recorded for this security.</p>
                )}
              </div>
            </CardContent>
          </Card>
        </TabPanel>
        <TabPanel>
          <SecurityPassportEditorLauncher
            securityId={entry.securityId}
            symbol={overview.symbol}
            assetClass={entry.classification.assetClass}
          />
        </TabPanel>
        <TabPanel>
          <div className="space-y-4">
            <SecurityDetailsPanel entry={entry} identity={identity} tradingParameters={tradingParameters} />
            <LotsTrackerPanel securityId={entry.securityId} currency={entry.economicDefinition.currency} />
          </div>
        </TabPanel>
        <TabPanel>
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>Trading parameters</CardTitle>
              <CardDescription>As of {tradingParameters?.asOf ?? "unknown"}</CardDescription>
            </CardHeader>
            <CardContent>
              {tradingParameters ? (
                <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                  <div>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Lot size</dt>
                    <dd className="mt-1 text-sm text-foreground">{tradingParameters.lotSize ?? "Not set"}</dd>
                  </div>
                  <div>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Tick size</dt>
                    <dd className="mt-1 text-sm text-foreground">{tradingParameters.tickSize ?? "Not set"}</dd>
                  </div>
                  <div>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Contract multiplier</dt>
                    <dd className="mt-1 text-sm text-foreground">{tradingParameters.contractMultiplier ?? "Not set"}</dd>
                  </div>
                  <div>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Margin requirement</dt>
                    <dd className="mt-1 text-sm text-foreground">{tradingParameters.marginRequirementPct != null ? `${tradingParameters.marginRequirementPct}%` : "Not set"}</dd>
                  </div>
                  <div>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Trading hours (UTC)</dt>
                    <dd className="mt-1 text-sm text-foreground">{tradingParameters.tradingHoursUtc ?? "Not set"}</dd>
                  </div>
                  <div>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Circuit breaker threshold</dt>
                    <dd className="mt-1 text-sm text-foreground">{tradingParameters.circuitBreakerThresholdPct != null ? `${tradingParameters.circuitBreakerThresholdPct}%` : "Not set"}</dd>
                  </div>
                </dl>
              ) : (
                <p className="text-sm text-muted-foreground">No trading parameters are configured for this security.</p>
              )}
            </CardContent>
          </Card>
        </TabPanel>
      </Tabs>
    </div>
  );
}
