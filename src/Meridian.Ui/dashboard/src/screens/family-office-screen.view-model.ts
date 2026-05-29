import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type FamilyOfficeTone = "default" | "success" | "warning" | "danger";

export interface FamilyOfficeRouteMetadata {
  path: string;
  workspaceLabel: string;
  label: string;
  title: string;
  description: string;
  ariaLabel: string;
  emptyState: string;
  disabledReason: string | null;
}

export interface FamilyOfficePanelViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: FamilyOfficeTone;
  emptyState: string;
  ariaLabel: string;
}

export interface FamilyOfficeOwnershipNode {
  id: string;
  label: string;
  type: "family" | "entity" | "asset";
  value: string;
  percentage: string;
  detail: string;
  parentId: string | null;
  tone: FamilyOfficeTone;
  rowClassName: string;
  isSelected: boolean;
  expanded: boolean;
  detailPanelId: string;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface FamilyOfficeOwnershipEdge {
  id: string;
  fromId: string;
  toId: string;
  label: string;
}

export interface FamilyOfficeOwnershipGraphViewModel {
  title: string;
  description: string;
  ariaLabel: string;
  keyboardInstructions: string;
  tableFallbackLabel: string;
  graphToggleLabel: string;
  tableToggleLabel: string;
  emptyState: string;
  selectedDetailTitle: string;
  selectedDetailEmptyTitle: string;
  selectedNodeId: string | null;
  selectedNode: FamilyOfficeOwnershipNode | null;
  nodes: FamilyOfficeOwnershipNode[];
  edges: FamilyOfficeOwnershipEdge[];
}

export interface FamilyOfficeScreenViewModel {
  route: FamilyOfficeRouteMetadata;
  statusChips: Array<{ label: string; value: string }>;
  panels: FamilyOfficePanelViewModel[];
  ownershipGraph: FamilyOfficeOwnershipGraphViewModel;
}

const FAMILY_OFFICE_ROUTE_METADATA: FamilyOfficeRouteMetadata = {
  path: WORKSTATION_ROUTE_CATALOG.portfolioFamilyOffice,
  workspaceLabel: "Portfolio",
  label: "Family office",
  title: "Family Office Portfolio",
  description: "Household-level net worth, entity ownership, private holdings, commitments, and reconciliation exceptions.",
  ariaLabel: "Family office portfolio route",
  emptyState: "Family office data is not connected yet. Link portfolio, accounting, and private-asset feeds before using this lane for operator decisions.",
  disabledReason: null
};

const FAMILY_OFFICE_PANELS: Omit<FamilyOfficePanelViewModel, "ariaLabel">[] = [
  {
    id: "total-family-net-worth",
    label: "Total family net worth",
    value: "$128.4M",
    detail: "Consolidated household NAV after liabilities and unfunded commitments.",
    tone: "success",
    emptyState: "No household NAV feed has been connected."
  },
  {
    id: "entity-breakdown",
    label: "Entity breakdown",
    value: "5 entities",
    detail: "Trusts, holding companies, operating LLCs, and direct family accounts mapped to beneficial owners.",
    tone: "default",
    emptyState: "No ownership entities have been mapped."
  },
  {
    id: "asset-class-breakdown",
    label: "Asset-class breakdown",
    value: "9 classes",
    detail: "Public equity, fixed income, cash, real estate, venture, private credit, operating assets, collectibles, and alternatives.",
    tone: "default",
    emptyState: "No asset-class taxonomy is available."
  },
  {
    id: "cash-and-liabilities",
    label: "Cash and liabilities",
    value: "$14.2M / $8.7M",
    detail: "Available cash and short-term liabilities across custody, treasury, and operating accounts.",
    tone: "warning",
    emptyState: "Cash and liability ledgers have not been reconciled."
  },
  {
    id: "private-assets",
    label: "Private assets",
    value: "$42.6M",
    detail: "Private equity, venture funds, direct real estate, and operating-company marks pending valuation review.",
    tone: "warning",
    emptyState: "No private asset register is available."
  },
  {
    id: "unfunded-commitments",
    label: "Unfunded commitments",
    value: "$11.4M",
    detail: "Remaining capital calls across private funds and direct investment side letters.",
    tone: "danger",
    emptyState: "Commitment schedule is unavailable."
  },
  {
    id: "unresolved-reconciliation-breaks",
    label: "Unresolved reconciliation breaks",
    value: "7 breaks",
    detail: "Custody, capital-call, and private valuation differences requiring operator resolution.",
    tone: "danger",
    emptyState: "No reconciliation queue has loaded."
  },
  {
    id: "stale-valuation-warnings",
    label: "Stale valuation warnings",
    value: "4 warnings",
    detail: "Private marks older than policy thresholds or missing independent valuation evidence.",
    tone: "warning",
    emptyState: "No stale valuation policy data has loaded."
  }
];

const FAMILY_OFFICE_OWNERSHIP_NODES: Array<Omit<FamilyOfficeOwnershipNode, "rowClassName" | "isSelected" | "expanded" | "detailPanelId" | "selectAriaLabel" | "ariaLabel">> = [
  {
    id: "family-holdco",
    label: "Meridian Family HoldCo",
    type: "family",
    value: "$128.4M",
    percentage: "100%",
    detail: "Consolidated beneficial ownership across family entities.",
    parentId: null,
    tone: "success"
  },
  {
    id: "alpha-trust",
    label: "Alpha Family Trust",
    type: "entity",
    value: "$54.1M",
    percentage: "42%",
    detail: "Public markets, cash reserves, and private fund LP interests.",
    parentId: "family-holdco",
    tone: "default"
  },
  {
    id: "beta-llc",
    label: "Beta Holdings LLC",
    type: "entity",
    value: "$38.7M",
    percentage: "30%",
    detail: "Operating-company and real-estate holdings with stale-mark warnings.",
    parentId: "family-holdco",
    tone: "warning"
  },
  {
    id: "gamma-foundation",
    label: "Gamma Foundation",
    type: "entity",
    value: "$21.3M",
    percentage: "17%",
    detail: "Restricted charitable assets and investment policy allocations.",
    parentId: "family-holdco",
    tone: "default"
  },
  {
    id: "direct-accounts",
    label: "Direct Family Accounts",
    type: "entity",
    value: "$14.3M",
    percentage: "11%",
    detail: "Personal brokerage, treasury, and liability accounts.",
    parentId: "family-holdco",
    tone: "warning"
  },
  {
    id: "private-funds",
    label: "Private fund sleeve",
    type: "asset",
    value: "$31.9M",
    percentage: "25%",
    detail: "$11.4M unfunded with two capital-call breaks.",
    parentId: "alpha-trust",
    tone: "danger"
  },
  {
    id: "real-estate",
    label: "Real estate sleeve",
    type: "asset",
    value: "$23.6M",
    percentage: "18%",
    detail: "Four assets; three marks are past the valuation freshness policy.",
    parentId: "beta-llc",
    tone: "warning"
  }
];

const FAMILY_OFFICE_OWNERSHIP_EDGES: FamilyOfficeOwnershipEdge[] = FAMILY_OFFICE_OWNERSHIP_NODES
  .filter((node) => node.parentId !== null)
  .map((node) => ({
    id: `${node.parentId}-${node.id}`,
    fromId: node.parentId ?? "",
    toId: node.id,
    label: `${node.percentage} ownership from ${node.parentId} to ${node.label}`
  }));

export function buildFamilyOfficeScreenViewModel(selectedNodeId: string | null = null): FamilyOfficeScreenViewModel {
  const stableSelectedNodeId = FAMILY_OFFICE_OWNERSHIP_NODES.some((node) => node.id === selectedNodeId)
    ? selectedNodeId
    : FAMILY_OFFICE_OWNERSHIP_NODES[0]?.id ?? null;
  const detailPanelId = "family-office-ownership-detail";
  const nodes = FAMILY_OFFICE_OWNERSHIP_NODES.map((node) => {
    const selected = node.id === stableSelectedNodeId;
    return {
      ...node,
      rowClassName: ownershipToneRowClassName(node.tone),
      isSelected: selected,
      expanded: selected,
      detailPanelId,
      selectAriaLabel: `Inspect ownership node ${node.label}`,
      ariaLabel: `${node.label}, ${node.type}, value ${node.value}, ${node.percentage} of consolidated net worth. ${node.detail}`
    };
  });
  const selectedNode = nodes.find((node) => node.id === stableSelectedNodeId) ?? null;

  return {
    route: FAMILY_OFFICE_ROUTE_METADATA,
    statusChips: [
      { label: "Workspace", value: FAMILY_OFFICE_ROUTE_METADATA.workspaceLabel },
      { label: "Route", value: FAMILY_OFFICE_ROUTE_METADATA.path },
      { label: "Graph mode", value: "Keyboard accessible" }
    ],
    panels: FAMILY_OFFICE_PANELS.map((panel) => ({
      ...panel,
      ariaLabel: `${panel.label}: ${panel.value}. ${panel.detail}`
    })),
    ownershipGraph: {
      title: "Ownership graph",
      description: "Navigate family entities and asset sleeves as a graph, or switch to the dense table fallback for assistive technology and audit review.",
      ariaLabel: "Family office ownership graph",
      keyboardInstructions: "Use Arrow keys, Home, and End to move between ownership nodes. Press Enter or Space to inspect the focused node. Use the table view for a dense accessible fallback.",
      tableFallbackLabel: "Family office ownership table fallback",
      graphToggleLabel: "Show ownership graph",
      tableToggleLabel: "Show ownership table fallback",
      emptyState: "No ownership graph data is available yet. Map entities and beneficial owners before graph review.",
      selectedDetailTitle: "Selected ownership detail",
      selectedDetailEmptyTitle: "No ownership node selected",
      selectedNodeId: stableSelectedNodeId,
      selectedNode,
      nodes,
      edges: FAMILY_OFFICE_OWNERSHIP_EDGES
    }
  };
}

export function selectAdjacentFamilyOfficeNode(
  currentNodeId: string | null,
  direction: "next" | "previous" | "first" | "last"
): string | null {
  if (FAMILY_OFFICE_OWNERSHIP_NODES.length === 0) {
    return null;
  }

  const currentIndex = FAMILY_OFFICE_OWNERSHIP_NODES.findIndex((node) => node.id === currentNodeId);
  const safeIndex = currentIndex >= 0 ? currentIndex : 0;

  switch (direction) {
    case "first":
      return FAMILY_OFFICE_OWNERSHIP_NODES[0].id;
    case "last":
      return FAMILY_OFFICE_OWNERSHIP_NODES[FAMILY_OFFICE_OWNERSHIP_NODES.length - 1].id;
    case "previous":
      return FAMILY_OFFICE_OWNERSHIP_NODES[Math.max(0, safeIndex - 1)].id;
    case "next":
      return FAMILY_OFFICE_OWNERSHIP_NODES[Math.min(FAMILY_OFFICE_OWNERSHIP_NODES.length - 1, safeIndex + 1)].id;
  }
}

function ownershipToneRowClassName(tone: FamilyOfficeTone): string {
  switch (tone) {
    case "success":
      return "border-success/30 bg-success/5";
    case "warning":
      return "border-warning/30 bg-warning/5";
    case "danger":
      return "border-danger/30 bg-danger/5";
    default:
      return "";
  }
}
