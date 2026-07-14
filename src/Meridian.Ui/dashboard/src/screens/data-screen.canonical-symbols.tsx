import { RefreshCcw, Search } from "lucide-react";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner, type StatusBannerTone } from "@/components/ui/status-banner";
import {
  useCanonicalSymbolRegistryPanel,
  type CanonicalSymbolModeTone,
  type CanonicalSymbolRegistryFetcher,
  type CanonicalSymbolRegistryRow
} from "@/screens/data-screen.canonical-symbols.view-model";

const badgeVariantByTone: Record<CanonicalSymbolModeTone, BadgeProps["variant"]> = {
  success: "success",
  warning: "warning",
  danger: "danger",
  info: "outline"
};

const bannerToneByModeTone: Record<CanonicalSymbolModeTone, StatusBannerTone> = {
  success: "success",
  warning: "warning",
  danger: "danger",
  info: "info"
};

function formatTimestamp(value: string | null): string {
  return value ? new Date(value).toLocaleString() : "No mismatch observed";
}

function symbolSubtitle(symbol: CanonicalSymbolRegistryRow): string {
  return [symbol.assetClass, symbol.exchange, symbol.currency].filter(Boolean).join(" · ");
}

function CanonicalSymbolDetail({ symbol }: { symbol: CanonicalSymbolRegistryRow }) {
  const identifiers = Object.entries(symbol.identifiers).filter((entry): entry is [string, string] => Boolean(entry[1]));
  return (
    <div className="grid gap-4 border-t bg-muted/20 px-4 py-3 text-sm lg:grid-cols-3">
      <div>
        <h4 className="font-semibold">Provider aliases</h4>
        {symbol.providerAliases.length === 0 ? (
          <p className="mt-1 text-muted-foreground">No provider-specific outbound alias is registered.</p>
        ) : (
          <ul className="mt-1 grid gap-1" aria-label={`${symbol.canonicalTicker} provider aliases`}>
            {symbol.providerAliases.map((alias) => (
              <li key={`${alias.provider}:${alias.symbol}`} className="flex flex-wrap items-center gap-2">
                <span className="font-mono font-semibold">{alias.provider}</span>
                <span className="font-mono">{alias.symbol}</span>
                <Badge variant={alias.isOverride ? "warning" : "outline"}>
                  {alias.isOverride ? "override" : alias.source}
                </Badge>
              </li>
            ))}
          </ul>
        )}
      </div>
      <div>
        <h4 className="font-semibold">Known aliases</h4>
        {symbol.aliases.length === 0 ? (
          <p className="mt-1 text-muted-foreground">No historical or alternate alias is registered.</p>
        ) : (
          <ul className="mt-1 grid gap-1" aria-label={`${symbol.canonicalTicker} known aliases`}>
            {symbol.aliases.map((alias) => (
              <li key={`${alias.provider ?? "global"}:${alias.alias}`} className="flex flex-wrap items-center gap-2">
                <span className="font-mono">{alias.alias}</span>
                <span className="text-xs text-muted-foreground">
                  {alias.provider ?? "global"} · {alias.source ?? "unattributed"}{alias.isActive ? "" : " · inactive"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
      <div>
        <h4 className="font-semibold">Identity and provenance</h4>
        <p className="mt-1 break-all font-mono text-xs text-muted-foreground">
          SecurityId: {symbol.securityId ?? "not linked"}
        </p>
        {identifiers.length > 0 ? (
          <ul className="mt-1 grid gap-1" aria-label={`${symbol.canonicalTicker} identifiers`}>
            {identifiers.map(([kind, value]) => (
              <li key={kind} className="font-mono text-xs text-muted-foreground">{kind}: {value}</li>
            ))}
          </ul>
        ) : null}
        <p className="mt-2 text-xs text-muted-foreground">
          Sources: {symbol.provenanceSources.join(", ") || "registry"}
        </p>
      </div>
    </div>
  );
}

export function CanonicalSymbolRegistryRegion({
  fetchRegistry
}: {
  fetchRegistry?: CanonicalSymbolRegistryFetcher;
}) {
  const panel = useCanonicalSymbolRegistryPanel(fetchRegistry);
  const model = panel.model;

  return (
    <section aria-labelledby="canonical-symbol-registry-title" className="workspace-region canonical-symbol-registry-region">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle id="canonical-symbol-registry-title">Canonical symbol registry</CardTitle>
            <CardDescription>
              Durable SecurityId identity, ticker history, provider aliases, and rollout comparison evidence.
              {model ? ` ${model.summary}` : null}
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {model ? <Badge variant={badgeVariantByTone[model.modeTone]}>{model.resolutionMode}</Badge> : null}
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => void panel.refresh()}
              disabled={panel.loading}
              aria-label="Refresh canonical symbol registry"
            >
              <RefreshCcw className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">{panel.loading ? "Refreshing…" : "Refresh"}</span>
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {panel.error ? (
            <StatusBanner tone="danger" title="Canonical symbol registry unavailable" detail={panel.error} />
          ) : !model ? (
            <p className="text-sm text-muted-foreground" role="status">Loading canonical symbol registry…</p>
          ) : (
            <div className="grid gap-4">
              <StatusBanner
                tone={bannerToneByModeTone[model.modeTone]}
                title={model.modeTitle}
                detail={model.modeDetail}
              />

              <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4" role="list" aria-label="Canonical registry status">
                <div className="rounded-md border p-3 text-sm" role="listitem">
                  <div className="text-xs uppercase tracking-wide text-muted-foreground">Registry version</div>
                  <div className="mt-1 font-mono font-semibold">{model.registryVersion}</div>
                </div>
                <div className="rounded-md border p-3 text-sm" role="listitem">
                  <div className="text-xs uppercase tracking-wide text-muted-foreground">Canonical securities</div>
                  <div className="mt-1 font-mono font-semibold">{model.symbols.length.toLocaleString()}</div>
                </div>
                <div className="rounded-md border p-3 text-sm" role="listitem">
                  <div className="text-xs uppercase tracking-wide text-muted-foreground">Provider aliases</div>
                  <div className="mt-1 font-mono font-semibold">{model.providerAliasCount.toLocaleString()}</div>
                </div>
                <div className="rounded-md border p-3 text-sm" role="listitem">
                  <div className="text-xs uppercase tracking-wide text-muted-foreground">Compare mismatches</div>
                  <div className="mt-1 font-mono font-semibold">{model.totalMismatchCount.toLocaleString()}</div>
                  <div className="mt-1 text-xs text-muted-foreground">{formatTimestamp(model.lastMismatchAt)}</div>
                </div>
              </div>

              <div>
                <label htmlFor="canonical-symbol-search" className="text-sm font-semibold">Search registry</label>
                <p id="canonical-symbol-search-help" className="mb-2 text-xs text-muted-foreground">
                  Search canonical ticker, SecurityId, industry identifier, alias, provider, or provenance source.
                </p>
                <Input
                  id="canonical-symbol-search"
                  value={panel.query}
                  onChange={(event) => panel.setQuery(event.currentTarget.value)}
                  placeholder="AAPL, US0378331005, polygon, security-master…"
                  leadingIcon={<Search className="h-4 w-4" />}
                  aria-describedby="canonical-symbol-search-help"
                />
              </div>

              {panel.visibleSymbols.length === 0 ? (
                <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground" role="status">
                  {model.symbols.length === 0
                    ? "The registry has no canonical securities yet."
                    : `No canonical security matches “${panel.query.trim()}”.`}
                </p>
              ) : (
                <div className="overflow-x-auto rounded-md border">
                  <div className="min-w-[720px] divide-y" role="list" aria-label="Canonical securities">
                    {panel.visibleSymbols.map((symbol) => (
                      <details key={symbol.canonicalTicker} role="listitem" className="group text-sm">
                        <summary className="grid cursor-pointer list-none grid-cols-[8rem_1fr_auto] items-center gap-3 px-3 py-2 hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
                          <span className="font-mono font-semibold">{symbol.canonicalTicker}</span>
                          <span>
                            <span className="font-medium">{symbol.displayName ?? "Unnamed security"}</span>
                            <span className="ml-2 text-xs text-muted-foreground">{symbolSubtitle(symbol)}</span>
                          </span>
                          <span className="flex items-center justify-end gap-2">
                            {symbol.hasRecentMismatch ? <Badge variant="warning">mismatch</Badge> : null}
                            <Badge variant="outline">{symbol.providerAliases.length} provider</Badge>
                            <span className="max-w-48 truncate font-mono text-xs text-muted-foreground">
                              {symbol.securityId ?? "SecurityId pending"}
                            </span>
                          </span>
                        </summary>
                        <CanonicalSymbolDetail symbol={symbol} />
                      </details>
                    ))}
                  </div>
                </div>
              )}

              <div className="grid gap-4 lg:grid-cols-2">
                <div className="rounded-md border p-3 text-sm">
                  <h3 className="font-semibold">Recent comparison mismatches</h3>
                  {model.recentMismatches.length === 0 ? (
                    <p className="mt-1 text-muted-foreground">No Legacy-versus-Canonical disagreement is retained.</p>
                  ) : (
                    <ul className="mt-2 grid gap-2" aria-label="Recent symbol comparison mismatches">
                      {model.recentMismatches.map((mismatch) => (
                        <li key={`${mismatch.observedAt}:${mismatch.input}:${mismatch.toProvider}`} className="rounded-md bg-muted/30 p-2">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-mono font-semibold">{mismatch.input}</span>
                            <span className="text-xs text-muted-foreground">
                              {mismatch.fromProvider} → {mismatch.toProvider}
                            </span>
                          </div>
                          <div className="mt-1 font-mono text-xs text-muted-foreground">
                            legacy {mismatch.legacyResult ?? "∅"} · canonical {mismatch.canonicalResult ?? "∅"}
                          </div>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
                <div className="rounded-md border p-3 text-sm">
                  <h3 className="font-semibold">Migration receipts</h3>
                  {model.migrations.length === 0 ? (
                    <p className="mt-1 text-muted-foreground">No legacy source migration receipt is stored.</p>
                  ) : (
                    <ul className="mt-2 grid gap-2" aria-label="Canonical symbol migration receipts">
                      {model.migrations.map((migration) => (
                        <li key={migration.migrationId} className="rounded-md bg-muted/30 p-2">
                          <div className="font-medium">{migration.migrationId}</div>
                          <div className="mt-1 break-all font-mono text-xs text-muted-foreground">
                            {migration.sourceFingerprint}
                          </div>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </section>
  );
}
