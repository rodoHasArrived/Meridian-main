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

export function AssetDetailScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  const securityId = searchParams.get("securityId");

  if (!securityId) {
    return <AssetSearchPanel onSelect={(id) => setSearchParams({ securityId: id })} />;
  }

  return <AssetDetailPanel securityId={securityId} />;
}

function AssetSearchPanel({ onSelect }: { onSelect: (securityId: string) => void }) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SecurityMasterEntry[]>([]);
  const [searching, setSearching] = useState(false);

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
      <CardContent className="space-y-3">
        <Input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Search by name, ticker, or identifier"
          aria-label="Search securities"
        />
        {searching ? <p className="text-sm text-muted-foreground">Searching...</p> : null}
        {results.length > 0 ? (
          <ul className="space-y-2" aria-label="Security search results">
            {results.map((entry) => (
              <li key={entry.securityId}>
                <button
                  type="button"
                  className="w-full rounded-md border border-border/70 bg-secondary/15 px-3 py-2 text-left hover:border-primary/40"
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
