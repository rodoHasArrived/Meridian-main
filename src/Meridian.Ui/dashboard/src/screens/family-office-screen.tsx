import type { KeyboardEvent } from "react";
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle, Boxes, GitBranch, Landmark, Network, TableProperties, WalletCards } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { cn } from "@/lib/utils";
import {
  buildFamilyOfficeScreenViewModel,
  selectAdjacentFamilyOfficeNode,
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

const toneBadgeVariant: Record<FamilyOfficeTone, "outline" | "success" | "warning" | "danger"> = {
  default: "outline",
  success: "success",
  warning: "warning",
  danger: "danger"
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

export function FamilyOfficeScreen() {
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [showTableFallback, setShowTableFallback] = useState(false);
  const vm = buildFamilyOfficeScreenViewModel(selectedNodeId);
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
    const nextNodeId = selectAdjacentFamilyOfficeNode(vm.ownershipGraph.selectedNodeId, direction);
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
    <section className="space-y-6" aria-label={vm.route.ariaLabel}>
      <header className="space-y-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className="eyebrow-label">{vm.route.workspaceLabel} / {vm.route.label}</div>
            <h1 className="text-2xl font-semibold tracking-tight text-foreground">{vm.route.title}</h1>
            <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">{vm.route.description}</p>
            {vm.route.disabledReason ? (
              <p className="mt-2 text-sm text-warning">{vm.route.disabledReason}</p>
            ) : null}
          </div>
          <div className="flex flex-wrap gap-2 lg:justify-end">
            <Button asChild variant="outline" size="sm">
              <Link to={WORKSTATION_ROUTE_CATALOG.portfolioAlternatives}>Open alternatives register</Link>
            </Button>
            {vm.statusChips.map((chip) => (
              <span
                key={chip.label}
                className="rounded-md border border-border bg-secondary/40 px-3 py-2 text-xs text-muted-foreground"
                aria-label={`${chip.label}: ${chip.value}`}
              >
                {chip.label}<b className="ml-2 font-mono text-foreground">{chip.value}</b>
              </span>
            ))}
          </div>
        </div>
      </header>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4" aria-label="Family office summary panels">
        <h2 className="sr-only">Family office summary panels</h2>
        {vm.panels.map((panel) => (
          <FamilyOfficePanel key={panel.id} panel={panel} />
        ))}
      </section>

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
                  <div className="eyebrow-label">{vm.ownershipGraph.selectedNode.type}</div>
                  <h2 className="mt-2 text-lg font-semibold text-foreground">{vm.ownershipGraph.selectedNode.label}</h2>
                  <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
                    {vm.ownershipGraph.selectedNode.detail}
                  </p>
                </div>
                <dl className="grid gap-2 sm:grid-cols-3 lg:min-w-[28rem]">
                  <OwnershipDetail label="Value" value={vm.ownershipGraph.selectedNode.value} />
                  <OwnershipDetail label="Ownership" value={vm.ownershipGraph.selectedNode.percentage} />
                  <OwnershipDetail label="Parent" value={vm.ownershipGraph.selectedNode.parentId ?? "Root"} />
                </dl>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">{vm.ownershipGraph.selectedDetailEmptyTitle}</p>
            )}
          </section>
        </CardContent>
      </Card>
    </section>
  );
}

function FamilyOfficePanel({ panel }: { panel: FamilyOfficePanelViewModel }) {
  const Icon = panelIconMap[panel.id] ?? Landmark;

  return (
    <Card className="panel-surface border-border/80" aria-label={panel.ariaLabel}>
      <CardHeader className="space-y-3">
        <div className="flex items-start justify-between gap-3">
          <Icon className="mt-1 h-4 w-4 text-primary" aria-hidden="true" />
          <Badge variant={toneBadgeVariant[panel.tone]}>{panel.tone === "default" ? "Tracked" : panel.tone}</Badge>
        </div>
        <div>
          <CardTitle className="text-sm font-semibold text-foreground">{panel.label}</CardTitle>
          <div className="mt-2 font-mono text-2xl font-semibold text-foreground">{panel.value}</div>
        </div>
      </CardHeader>
      <CardContent>
        <p className="text-sm leading-6 text-muted-foreground">{panel.detail}</p>
      </CardContent>
    </Card>
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
        "min-h-28 rounded-xl border px-4 py-3 text-left shadow-hard transition focus:outline-none focus:ring-2 focus:ring-primary/60",
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
      <span className="block text-xs uppercase tracking-[0.2em] opacity-80">{node.type}</span>
      <span className="mt-2 block text-sm font-semibold text-foreground">{node.label}</span>
      <span className="mt-3 flex items-center justify-between gap-3 font-mono text-xs">
        <span>{node.value}</span>
        <span>{node.percentage}</span>
      </span>
    </button>
  );
}

function OwnershipDetail({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-border/60 bg-secondary/20 px-3 py-2">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-1 font-mono text-sm text-foreground">{value}</dd>
    </div>
  );
}
