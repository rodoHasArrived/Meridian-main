import type { KeyboardEvent } from "react";
import { useEffect, useRef, useState } from "react";
import { AlertTriangle, Boxes, GitBranch, Landmark, Network, TableProperties, WalletCards } from "lucide-react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/data/empty-state";
import { OperationalTrustSummary } from "@/components/meridian/operational-trust-summary";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  MetricCard,
  KeyValueGrid
} from "@/components/data/concrete";
import { ScreenLayout } from "@/components/ui/screen-layout";
import { SeverityBadge } from "@/components/operations";
import { cn } from "@/lib/utils";
import { readinessToneToSeverityStatus, semanticToneToMetricCardTone } from "@/lib/shared-tone-mappings";
import {
  buildFamilyOfficeScreenViewModel,
  selectAdjacentFamilyOfficeNode,
  type FamilyOfficeEntityStructure,
  type FamilyOfficeOwnershipNode,
  type FamilyOfficePanelViewModel,
  type FamilyOfficeTone
} from "@/screens/family-office-screen.view-model";

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

const graphNodeClassName: Record<FamilyOfficeTone, string> = {
  default: "border-border bg-secondary/80 text-foreground",
  success: "border-success/50 bg-success/10 text-success",
  warning: "border-warning/50 bg-warning/10 text-warning",
  danger: "border-danger/50 bg-danger/10 text-danger"
};

const ownershipColumns: DenseDataTableColumn<FamilyOfficeOwnershipNode>[] = [
  {
    id: "label",
    label: "Node",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="block text-xs text-muted-foreground">{row.type}</span>
      </span>
    )
  },
  {
    id: "value",
    label: "Value",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.value}</span>
  },
  {
    id: "percentage",
    label: "Ownership",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.percentage}</span>
  },
  {
    id: "parent",
    label: "Parent",
    render: (row) => <span className="font-mono text-xs text-muted-foreground">{row.parentId ?? "Root"}</span>
  },
  {
    id: "detail",
    label: "Detail",
    render: (row) => <span className="text-muted-foreground">{row.detail}</span>
  }
];

export function FamilyOfficeScreen({ entityStructure = null }: { entityStructure?: FamilyOfficeEntityStructure | null }) {
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [showTableFallback, setShowTableFallback] = useState(false);
  const vm = buildFamilyOfficeScreenViewModel(selectedNodeId, entityStructure);
  const graphNodeRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const shouldFocusGraphNode = useRef(false);

  useEffect(() => {
    if (!shouldFocusGraphNode.current || !vm.ownershipGraph.selectedNodeId) {
      return;
    }

    shouldFocusGraphNode.current = false;
    graphNodeRefs.current[vm.ownershipGraph.selectedNodeId]?.focus();
  }, [vm.ownershipGraph.selectedNodeId]);

  const selectNode = (nodeId: string) => {
    setSelectedNodeId(nodeId);
  };

  const moveGraphSelection = (direction: "next" | "previous" | "first" | "last") => {
    const nextNodeId = selectAdjacentFamilyOfficeNode(vm.ownershipGraph.selectedNodeId, direction, entityStructure);
    if (nextNodeId) {
      shouldFocusGraphNode.current = true;
      setSelectedNodeId(nextNodeId);
    }
  };

  const handleGraphNodeKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    switch (event.key) {
      case "ArrowRight":
      case "ArrowDown":
        event.preventDefault();
        moveGraphSelection("next");
        break;
      case "ArrowLeft":
      case "ArrowUp":
        event.preventDefault();
        moveGraphSelection("previous");
        break;
      case "Home":
        event.preventDefault();
        moveGraphSelection("first");
        break;
      case "End":
        event.preventDefault();
        moveGraphSelection("last");
        break;
      default:
        break;
    }
  };

  return (
    <ScreenLayout
      title={vm.route.title}
      scope={`${vm.route.workspaceLabel} / ${vm.route.label}`}
      description={vm.route.description}
    >
      <OperationalTrustSummary
        label="Family office data confidence"
        source={{
          value: entityStructure?.displayName ?? "Not connected",
          detail: "Entity, portfolio, accounting, and private-asset sources",
          tone: entityStructure ? "ready" : "blocked"
        }}
        scope={{
          value: entityStructure ? `${entityStructure.entities.length} entities` : "No family entities",
          detail: entityStructure ? `${entityStructure.baseCurrency} consolidated scope` : "Complete entity setup to establish scope",
          tone: entityStructure?.entities.length ? "ready" : "blocked"
        }}
        freshness={{
          value: entityStructure?.asOfDate ?? "Unavailable",
          detail: "Latest consolidated family-office evidence",
          tone: entityStructure?.asOfDate ? "ready" : "unknown"
        }}
        completeness={{
          value: entityStructure
            ? `${entityStructure.privateAssets.length} private assets · ${entityStructure.commitments.length} commitments`
            : "No records loaded",
          detail: "Private-asset and commitment coverage",
          tone: entityStructure ? "ready" : "blocked"
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
            title="Family office data is not connected"
            detail={vm.route.emptyState}
          />
          <div className="flex justify-center">
            <Button asChild>
              <Link to={vm.emptyActionHref}>{vm.emptyActionLabel}</Link>
            </Button>
          </div>
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
              onClick={() => setShowTableFallback((current) => !current)}
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

          {showTableFallback ? (
            <DenseDataTable
              columns={ownershipColumns}
              rows={vm.ownershipGraph.nodes}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowAriaControls={(row) => row.detailPanelId}
              getRowAriaExpanded={(row) => row.expanded}
              getRowClassName={(row) => row.rowClassName}
              getRowSelectAriaLabel={(row) => row.selectAriaLabel}
              onRowSelect={(row) => selectNode(row.id)}
              selectedRowId={vm.ownershipGraph.selectedNodeId}
              emptyText={vm.ownershipGraph.emptyState}
              ariaLabel={vm.ownershipGraph.tableFallbackLabel}
              tableId="family-office-ownership-table"
              caption={vm.ownershipGraph.keyboardInstructions}
            />
          ) : (
            <div
              className="rounded-xl border border-border/70 bg-secondary/10 p-4"
              role="group"
              aria-label={vm.ownershipGraph.ariaLabel}
              aria-describedby="family-office-graph-keyboard-instructions"
            >
              <div className="sr-only" aria-live="polite" aria-atomic="true">
                {vm.ownershipGraph.selectedNode ? `Selected ${vm.ownershipGraph.selectedNode.label}.` : vm.ownershipGraph.emptyState}
              </div>
              <div className="grid gap-4 lg:grid-cols-[1fr_1.2fr] lg:items-center">
                <OwnershipNodeButton
                  node={vm.ownershipGraph.nodes[0]}
                  refCallback={(node) => {
                    graphNodeRefs.current[vm.ownershipGraph.nodes[0]?.id ?? ""] = node;
                  }}
                  onSelect={selectNode}
                  onKeyDown={handleGraphNodeKeyDown}
                />
                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                  {vm.ownershipGraph.nodes.slice(1).map((node) => (
                    <OwnershipNodeButton
                      key={node.id}
                      node={node}
                      refCallback={(element) => {
                        graphNodeRefs.current[node.id] = element;
                      }}
                      onSelect={selectNode}
                      onKeyDown={handleGraphNodeKeyDown}
                    />
                  ))}
                </div>
              </div>
              <ul className="mt-4 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2" aria-label="Ownership graph edges">
                {vm.ownershipGraph.edges.map((edge) => (
                  <li key={edge.id} className="rounded-md border border-border/60 bg-background/30 px-3 py-2">
                    {edge.label}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <section
            id="family-office-ownership-detail"
            tabIndex={-1}
            className="rounded-xl border border-border/70 bg-background/30 p-4 focus:outline-none focus:ring-2 focus:ring-primary/60"
            role="region"
            aria-label={vm.ownershipGraph.selectedDetailTitle}
          >
            {vm.ownershipGraph.selectedNode ? (
              <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <div className="eyebrow-label">{vm.ownershipGraph.selectedNode.type}</div>
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
                      { label: "Ownership", value: vm.ownershipGraph.selectedNode.percentage },
                      { label: "Parent", value: vm.ownershipGraph.selectedNode.parentId ?? "Root" }
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

function OwnershipNodeButton({
  node,
  refCallback,
  onSelect,
  onKeyDown
}: {
  node: FamilyOfficeOwnershipNode;
  refCallback: (node: HTMLButtonElement | null) => void;
  onSelect: (nodeId: string) => void;
  onKeyDown: (event: KeyboardEvent<HTMLButtonElement>) => void;
}) {
  return (
    <button
      ref={refCallback}
      type="button"
      className={cn(
        "min-h-28 rounded-sm border px-4 py-3 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-primary/60",
        graphNodeClassName[node.tone],
        node.isSelected ? "ring-2 ring-primary/70" : "hover:border-primary/50"
      )}
      aria-label={node.selectAriaLabel}
      aria-pressed={node.isSelected}
      aria-controls={node.detailPanelId}
      aria-expanded={node.expanded}
      tabIndex={node.isSelected ? 0 : -1}
      onClick={() => onSelect(node.id)}
      onKeyDown={onKeyDown}
    >
      <span className="block text-[10px] font-semibold uppercase tracking-[0.2em] opacity-80">{node.type}</span>
      <span className="mt-2 block text-sm font-semibold text-foreground">{node.label}</span>
      <span className="mt-3 flex items-center justify-between gap-3 font-mono text-xs tabular-nums">
        <span>{node.value}</span>
        <span>{node.percentage}</span>
      </span>
    </button>
  );
}
