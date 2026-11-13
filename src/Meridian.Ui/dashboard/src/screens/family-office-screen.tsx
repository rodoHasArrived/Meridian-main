import { useEffect, useRef, useState } from "react";
import { AlertTriangle, Boxes, GitBranch, Landmark, Network, TableProperties, WalletCards } from "lucide-react";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/data/empty-state";
import { OperationalTrustSummary } from "@/components/meridian/operational-trust-summary";
import { FamilyOwnershipGraph } from "@/screens/family-ownership-graph";
import { PrivateCapitalLifecyclePanel } from "@/components/meridian/private-capital-lifecycle-panel";
import {
  MetricCard,
  KeyValueGrid
} from "@/components/data/concrete";
import { ScreenLayout } from "@/components/ui/screen-layout";
import { SeverityBadge } from "@/components/operations";
import { getFamilyOfficeOverview } from "@/lib/api/family-office.api";
import { mapFamilyOfficeOverview } from "@/screens/family-office-screen.mapper";
import { readinessToneToSeverityStatus, semanticToneToMetricCardTone } from "@/lib/shared-tone-mappings";
import {
  buildFamilyOfficeScreenViewModel,
  type FamilyOfficeEntityStructure,
  type FamilyOfficePanelViewModel,
} from "@/screens/family-office-screen.view-model";
import { eyebrowClassName } from "@/design-system/label-case";
import { pluralizeCount } from "@/lib/format";

const panelIconMap: Record<string, typeof Landmark> = {
  "total-family-net-worth": Landmark,
  "entity-breakdown": Network,
  "asset-class-breakdown": Boxes,
  "cash-and-liabilities": WalletCards,
  "private-assets": Landmark,
  "unfunded-commitments": AlertTriangle,
  "unresolved-reconciliation-breaks": AlertTriangle,
  "stale-valuation-warnings": AlertTriangle
};

type FamilyOfficeLoadState = "idle" | "loading" | "ready" | "error";

/** Source chip copy while no structure has resolved yet, so "Not connected" is never claimed mid-load. */
function loadStateSourceLabel(loadState: FamilyOfficeLoadState): string {
  switch (loadState) {
    case "loading":
      return "Loading";
    case "error":
      return "Unavailable";
    default:
      return "Not connected";
  }
}

/**
 * Distinguishes "still loading", "the read failed", and "the household really is
 * unconfigured". Collapsing them would report a transport failure as an empty
 * family office.
 */
function emptyStateCopy(loadState: FamilyOfficeLoadState): { title: string; detail: string | null } {
  switch (loadState) {
    case "loading":
      return { title: "Loading family office data", detail: "Reading the consolidated household projection." };
    case "error":
      return {
        title: "Family office data could not be loaded",
        detail: "The consolidated household read failed. Retry, or confirm the workstation host is reachable."
      };
    default:
      return { title: "Family office data is not connected", detail: null };
  }
}

/**
 * `entityStructure` is an explicit override used by tests and demo surfaces. When it
 * is omitted the screen loads the consolidated household from
 * `/api/workstation/family-office/overview`; passing `null` keeps the
 * not-connected state without issuing a request.
 */
export function FamilyOfficeScreen({ entityStructure }: { entityStructure?: FamilyOfficeEntityStructure | null } = {}) {
  const [query, setQuery] = useSearchParams();
  const location = useLocation();
  // Router search-param callbacks do not queue updates. Preserve the latest pending
  // query when adjacent controls are used before the next location render.
  const queryRef = useRef(query);
  const renderedQuery = useRef(query.toString());
  // An unrelated state render can precede the pending router navigation. It must
  // not replace the synchronously retained query with the previous location.
  if (renderedQuery.current !== query.toString()) {
    renderedQuery.current = query.toString();
    queryRef.current = query;
  }
  const updateQuery = (update: (current: URLSearchParams) => URLSearchParams) => {
    // Browser history is updated before concurrent router renders. Read its latest
    // query for this route; MemoryRouter/test hosts retain the in-memory writer.
    const browserRoute = typeof window !== "undefined" && location.pathname !== "/"
      && window.location.pathname.endsWith(location.pathname);
    const next = update(new URLSearchParams(browserRoute ? window.location.search : queryRef.current));
    queryRef.current = next;
    setQuery(next);
  };
  const familyOfficeId = query.get("familyOfficeId") ?? "";
  const selectedNodeId = query.get("ownershipNode");
  const showTableFallback = query.get("ownershipView") === "table";
  const [availableClients, setAvailableClients] = useState<Array<{ clientId: string; displayName: string }>>([]);
  const [loadedClientScope, setLoadedClientScope] = useState("");
  const [loadedStructure, setLoadedStructure] = useState<FamilyOfficeEntityStructure | null>(null);
  const [loadState, setLoadState] = useState<FamilyOfficeLoadState>(
    entityStructure === undefined ? "loading" : "idle"
  );
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    if (entityStructure !== undefined) {
      return;
    }

    const controller = new AbortController();
    let active = true;
    setLoadState("loading");
    setLoadedStructure(null);
    getFamilyOfficeOverview({ signal: controller.signal }, familyOfficeId || undefined)
      .then((overview) => {
        if (!active) {
          return;
        }

        setAvailableClients(overview.availableClients ?? []);
        setLoadedClientScope(familyOfficeId);
        setLoadedStructure(mapFamilyOfficeOverview(overview));
        setLoadState("ready");
      })
      .catch(() => {
        if (active) {
          setLoadState("error");
        }
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [entityStructure, reloadToken, familyOfficeId]);

  const resolvedStructure = entityStructure === undefined
    ? loadedClientScope === familyOfficeId ? loadedStructure : null
    : entityStructure;
  const vm = buildFamilyOfficeScreenViewModel(selectedNodeId, resolvedStructure);
  const selectNode = (nodeId: string) => {
    updateQuery((current) => { const next = new URLSearchParams(current); next.set("ownershipNode", nodeId); return next; });
  };

  return (
    <ScreenLayout
      title={vm.route.title}
      scope={`${vm.route.workspaceLabel} / ${vm.route.label}`}
      description={vm.route.description}
    >
      {entityStructure === undefined && availableClients.length > 0 ? <label className="block text-sm">Family office
        <select className="ml-3 rounded border border-border bg-background p-2" value={familyOfficeId || (availableClients.length === 1 ? availableClients[0].clientId : "")} onChange={(event) => {
          updateQuery((current) => { const next = new URLSearchParams(current); next.set("familyOfficeId", event.target.value); next.delete("ownershipNode"); return next; });
        }}><option value="">Select a family office</option>{availableClients.map((client) => <option key={client.clientId} value={client.clientId}>{client.displayName}</option>)}</select>
      </label> : null}
      <OperationalTrustSummary
        label="Family office data confidence"
        source={{
          value: resolvedStructure?.displayName ?? loadStateSourceLabel(loadState),
          detail: "Entity, portfolio, accounting, and private-asset sources",
          tone: resolvedStructure ? "ready" : "blocked"
        }}
        scope={{
          value: resolvedStructure ? pluralizeCount(resolvedStructure.entities.length, "entity", { plural: "entities" }) : "No family entities",
          detail: resolvedStructure ? `${resolvedStructure.baseCurrency} consolidated scope` : "Complete entity setup to establish scope",
          tone: resolvedStructure?.entities.length ? "ready" : "blocked"
        }}
        freshness={{
          value: resolvedStructure?.asOfDate ?? "Unavailable",
          detail: "Latest consolidated family-office evidence",
          tone: resolvedStructure?.asOfDate ? "ready" : "unknown"
        }}
        completeness={{
          value: resolvedStructure
            ? `${pluralizeCount(resolvedStructure.privateAssets.length, "private asset")} · ${pluralizeCount(resolvedStructure.commitments.length, "commitment")}`
            : "No records loaded",
          detail: "Private-asset and commitment coverage",
          tone: resolvedStructure ? resolvedStructure.warnings?.length ? "review" : "ready" : "blocked"
        }}
        blocker={vm.notConnected ? {
          value: "Entity setup required",
          detail: "Connect governed sources before using consolidated values for decisions",
          tone: "blocked"
        } : undefined}
      />

      {vm.notConnected ? (
      <Card className="panel-surface border-border/80">
        <CardContent className="space-y-4">
          <EmptyState
            icon="inbox"
            title={loadState === "ready" && availableClients.length > 1 && !familyOfficeId ? "Select a family office" : emptyStateCopy(loadState).title}
            detail={loadState === "ready" && availableClients.length > 1 && !familyOfficeId ? "Choose an authorized client to read its current ownership and balances." : emptyStateCopy(loadState).detail ?? vm.route.emptyState}
          />
          {loadState === "loading" ? null : (
          <div className="flex justify-center">
            {loadState === "error" ? (
              <Button type="button" onClick={() => setReloadToken((token) => token + 1)}>
                Retry loading family office data
              </Button>
            ) : (
              <Button asChild>
                <Link to={vm.emptyActionHref}>{vm.emptyActionLabel}</Link>
              </Button>
            )}
          </div>
          )}
        </CardContent>
      </Card>
      ) : null}

      {!vm.notConnected ? (
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4" aria-label="Family office summary panels">
        <h2 className="sr-only">Family office summary panels</h2>
        {vm.panels.map((panel) => (
          <FamilyOfficePanel key={panel.id} panel={panel} />
        ))}
      </section>
      ) : null}

      {!vm.notConnected ? (
      <Card className="panel-surface border-border/80">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Beneficial ownership</div>
              <h2 className="mt-2 flex items-center gap-2 text-base font-semibold text-foreground">
                <GitBranch className="h-4 w-4 text-primary" />
                {vm.ownershipGraph.title}
              </h2>
              <CardDescription>{vm.ownershipGraph.description}</CardDescription>
            </div>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => updateQuery((current) => { const next = new URLSearchParams(current); if (showTableFallback) next.delete("ownershipView"); else next.set("ownershipView", "table"); return next; })}
              aria-pressed={showTableFallback}
              aria-label={showTableFallback ? vm.ownershipGraph.graphToggleLabel : vm.ownershipGraph.tableToggleLabel}
            >
              <TableProperties className="h-4 w-4" />
              {showTableFallback ? "Graph view" : "Table fallback"}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <p id="family-office-graph-keyboard-instructions" className="sr-only">
            {vm.ownershipGraph.keyboardInstructions}
          </p>

          {resolvedStructure?.warnings?.map((warning) => <p key={warning} role="status" className="text-xs text-warning">{warning}</p>)}
          {selectedNodeId && !vm.ownershipGraph.nodes.some((node) => node.id === selectedNodeId) ? <p role="status">The selected ownership node is unavailable in this scope.</p> : null}
          <FamilyOwnershipGraph graph={vm.ownershipGraph} table={showTableFallback} onSelect={selectNode} />

          <section
            id="family-office-ownership-detail"
            tabIndex={-1}
            className="rounded-xl border border-border/70 bg-background/30 p-4 focus:outline-hidden focus:ring-2 focus:ring-primary/60"
            role="region"
            aria-label={vm.ownershipGraph.selectedDetailTitle}
          >
            {vm.ownershipGraph.selectedNode ? (
              <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <div className={eyebrowClassName(vm.ownershipGraph.selectedNode.type)}>{vm.ownershipGraph.selectedNode.type}</div>
                    {vm.ownershipGraph.selectedNode.tone !== "default" ? (
                      <SeverityBadge status={readinessToneToSeverityStatus(vm.ownershipGraph.selectedNode.tone)} />
                    ) : null}
                  </div>
                  <h2 className="mt-2 text-lg font-semibold text-foreground">{vm.ownershipGraph.selectedNode.label}</h2>
                  <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
                    {vm.ownershipGraph.selectedNode.detail}
                  </p>
                </div>
                <div className="lg:min-w-[28rem]">
                  <KeyValueGrid
                    columns={3}
                    items={[
                      { label: "Value", value: vm.ownershipGraph.selectedNode.value },
                      { label: "Relationship percentages", value: "Inspect individual relationships" },
                      { label: "Legal title", value: vm.ownershipGraph.selectedNode.type === "asset" ? "Not established by association" : "See retained Owns relationships" }
                    ]}
                  />
                </div>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">{vm.ownershipGraph.selectedDetailEmptyTitle}</p>
            )}
          </section>
        </CardContent>
      </Card>
      ) : null}
      {resolvedStructure ? <PrivateCapitalLifecyclePanel holderEntityIds={resolvedStructure.entities.map((entity) => entity.entityId)} scopeKey={resolvedStructure.familyOfficeId} asOfDate={resolvedStructure.asOfDate} onViewStateChange={(patch) => updateQuery((current) => {
        for (const [key, value] of Object.entries(patch)) { if (value === null) current.delete(key); else current.set(key, value); }
        return current;
      })} /> : null}
    </ScreenLayout>
  );
}

function FamilyOfficePanel({ panel }: { panel: FamilyOfficePanelViewModel }) {
  const Icon = panelIconMap[panel.id] ?? Landmark;

  return (
    <div className="grid gap-2" aria-label={panel.ariaLabel} role="group">
      <MetricCard label={panel.label} value={panel.value} tone={semanticToneToMetricCardTone(panel.tone)} />
      <p className="flex items-start gap-2 px-1 text-xs leading-5 text-muted-foreground">
        <Icon className="mt-0.5 h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
        <span>{panel.detail}</span>
      </p>
    </div>
  );
}
