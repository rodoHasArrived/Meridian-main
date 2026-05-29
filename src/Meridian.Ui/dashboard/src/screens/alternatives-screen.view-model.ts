import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type AlternativesTone = "default" | "success" | "warning" | "danger";
export type PrivateAssetRecordSource = "fixture" | "import";

export interface AlternativesRouteMetadata {
  path: string;
  workspaceLabel: string;
  label: string;
  title: string;
  description: string;
  ariaLabel: string;
  dataSourcePolicy: string;
  disabledReason: string | null;
}

export interface AlternativesSummaryCard {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AlternativesTone;
  ariaLabel: string;
}

export interface AlternativesWarning {
  id: string;
  label: OperatorWarningLabel;
  detail: string;
  tone: AlternativesTone;
}

export type OperatorWarningLabel =
  | "manual valuation"
  | "statement imported"
  | "unreconciled"
  | "missing source document"
  | "stale NAV";

export interface PrivateAssetRecord {
  id: string;
  name: string;
  strategy: string;
  entity: string;
  nav: string;
  commitment: string;
  unfunded: string;
  lastValuation: string;
  source: PrivateAssetRecordSource;
  sourceLabel: string;
  staleDays: number;
  warnings: AlternativesWarning[];
  rowClassName: string;
  selected: boolean;
  detailPanelId: string;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface ValuationHistoryPoint {
  id: string;
  asOf: string;
  value: string;
  basis: string;
  source: string;
  warningLabel: OperatorWarningLabel | null;
}

export interface DocumentEvidenceItem {
  id: string;
  label: string;
  status: string;
  detail: string;
  warningLabel: OperatorWarningLabel | null;
}

export interface CapitalActivityItem {
  id: string;
  date: string;
  type: string;
  amount: string;
  status: string;
  detail: string;
  warningLabel: OperatorWarningLabel | null;
}

export interface PrivateAssetDetail {
  assetId: string;
  title: string;
  subtitle: string;
  overview: string;
  commitmentSummary: AlternativesSummaryCard[];
  valuationHistory: ValuationHistoryPoint[];
  documents: DocumentEvidenceItem[];
  capitalActivity: CapitalActivityItem[];
  staleDataWarnings: AlternativesWarning[];
}

export interface AlternativesScreenViewModel {
  route: AlternativesRouteMetadata;
  statusChips: Array<{ label: string; value: string }>;
  summaryCards: AlternativesSummaryCard[];
  privateAssets: PrivateAssetRecord[];
  selectedAsset: PrivateAssetRecord;
  selectedDetail: PrivateAssetDetail;
  explicitWarnings: OperatorWarningLabel[];
  listEmptyText: string;
  detailDrawerTitle: string;
  detailDrawerDescription: string;
}

const EXPLICIT_WARNINGS: OperatorWarningLabel[] = [
  "manual valuation",
  "statement imported",
  "unreconciled",
  "missing source document",
  "stale NAV"
];

const ROUTE_METADATA: AlternativesRouteMetadata = {
  path: WORKSTATION_ROUTE_CATALOG.portfolioAlternatives,
  workspaceLabel: "Portfolio",
  label: "Alternatives",
  title: "Private Alternatives Register",
  description:
    "Fixture and import-backed private asset records for commitments, valuation evidence, document coverage, capital activity, and stale NAV warnings before direct custodian or private-market integrations are built.",
  ariaLabel: "Portfolio alternatives private asset route",
  dataSourcePolicy:
    "Records may be loaded from fixtures or statement imports while Meridian waits for full custodian and private-market integrations; every operator warning remains explicit.",
  disabledReason: null
};

const PRIVATE_ASSETS: Array<Omit<PrivateAssetRecord, "rowClassName" | "selected" | "detailPanelId" | "selectAriaLabel" | "ariaLabel">> = [
  {
    id: "aurora-growth-v",
    name: "Aurora Growth Fund V",
    strategy: "Venture capital",
    entity: "Alpha Family Trust",
    nav: "$12.8M",
    commitment: "$18.0M",
    unfunded: "$5.2M",
    lastValuation: "2026-02-29",
    source: "import",
    sourceLabel: "statement imported",
    staleDays: 90,
    warnings: [
      warning("statement imported", "Q1 capital statement imported from PDF; API integration is not connected."),
      warning("stale NAV", "NAV is 90 days old and exceeds the 60 day private-fund policy threshold.")
    ]
  },
  {
    id: "granite-credit-opps",
    name: "Granite Credit Opportunities",
    strategy: "Private credit",
    entity: "Meridian Family HoldCo",
    nav: "$9.4M",
    commitment: "$12.0M",
    unfunded: "$1.6M",
    lastValuation: "2026-04-30",
    source: "fixture",
    sourceLabel: "fixture record",
    staleDays: 29,
    warnings: [
      warning("manual valuation", "Operator-entered valuation pending independent review."),
      warning("unreconciled", "Capital call cash movement has not been matched to bank activity.")
    ]
  },
  {
    id: "harbor-re-dev",
    name: "Harbor Industrial Development",
    strategy: "Direct real estate",
    entity: "Beta Holdings LLC",
    nav: "$16.1M",
    commitment: "$16.1M",
    unfunded: "$0.0M",
    lastValuation: "2025-12-31",
    source: "import",
    sourceLabel: "statement imported",
    staleDays: 149,
    warnings: [
      warning("missing source document", "Updated appraisal source document is missing from the evidence binder."),
      warning("stale NAV", "Last valuation is older than the quarterly real-estate policy threshold."),
      warning("manual valuation", "Interim mark is manually maintained until appraisal evidence is attached.")
    ]
  }
];

const DETAILS: Record<string, Omit<PrivateAssetDetail, "assetId" | "title" | "subtitle">> = {
  "aurora-growth-v": {
    overview: "LP interest imported from quarterly capital statement with NAV and unfunded commitment pending custodian feed coverage.",
    commitmentSummary: [summary("Committed", "$18.0M", "Subscription agreement total commitment.", "default"), summary("Called", "$12.8M", "Capital called through the imported Q1 statement.", "success"), summary("Unfunded", "$5.2M", "Remaining callable commitment.", "warning")],
    valuationHistory: [
      valuation("2026-02-29", "$12.8M", "Quarterly statement", "Imported statement", "statement imported"),
      valuation("2025-12-31", "$11.9M", "Quarterly statement", "Imported statement", null),
      valuation("2025-09-30", "$10.7M", "Manager estimate", "Manual upload", "manual valuation")
    ],
    documents: [
      documentItem("Q1 2026 capital statement", "Attached", "Imported PDF statement anchors the current NAV.", "statement imported"),
      documentItem("Subscription agreement", "Attached", "Commitment source document is present.", null),
      documentItem("Side letter", "Missing", "Side-letter evidence is expected before approval.", "missing source document")
    ],
    capitalActivity: [
      activity("2026-03-15", "Capital call", "$1.1M", "Matched", "Wire matched to imported call notice.", null),
      activity("2026-04-05", "Distribution", "$240K", "Pending review", "Distribution memo is present but statement reconciliation is open.", "unreconciled")
    ],
    staleDataWarnings: [warning("stale NAV", "Refresh the Aurora NAV or document why a 90 day mark is acceptable.")]
  },
  "granite-credit-opps": {
    overview: "Fixture-backed credit fund record used to exercise commitment and valuation workflows before private-credit servicer integrations are available.",
    commitmentSummary: [summary("Committed", "$12.0M", "Fixture commitment from internal register.", "default"), summary("Called", "$10.4M", "Called capital after latest fixture activity.", "success"), summary("Unfunded", "$1.6M", "Callable exposure remaining.", "warning")],
    valuationHistory: [
      valuation("2026-04-30", "$9.4M", "Operator mark", "Fixture register", "manual valuation"),
      valuation("2026-03-31", "$9.2M", "Statement import", "Imported statement", "statement imported"),
      valuation("2026-02-28", "$9.1M", "Statement import", "Imported statement", null)
    ],
    documents: [
      documentItem("April manual valuation memo", "Needs approval", "Manual valuation memo is present but not approved.", "manual valuation"),
      documentItem("Capital call notice", "Attached", "Notice is awaiting cash reconciliation.", "unreconciled")
    ],
    capitalActivity: [
      activity("2026-05-01", "Capital call", "$600K", "Unreconciled", "Bank transaction has not been matched.", "unreconciled"),
      activity("2026-04-12", "Interest distribution", "$90K", "Matched", "Distribution reconciled to custody cash.", null)
    ],
    staleDataWarnings: []
  },
  "harbor-re-dev": {
    overview: "Direct real-estate holding with stale appraisal evidence and a manually maintained interim NAV.",
    commitmentSummary: [summary("Committed", "$16.1M", "Fully funded project equity.", "default"), summary("Called", "$16.1M", "All capital has been funded.", "success"), summary("Unfunded", "$0.0M", "No remaining commitment.", "success")],
    valuationHistory: [
      valuation("2025-12-31", "$16.1M", "Year-end appraisal", "Imported package", "stale NAV"),
      valuation("2025-09-30", "$15.6M", "Manual quarter mark", "Operator memo", "manual valuation"),
      valuation("2025-06-30", "$15.2M", "Appraisal", "Source document", null)
    ],
    documents: [
      documentItem("2026 appraisal update", "Missing", "Required source document has not been attached.", "missing source document"),
      documentItem("2025 appraisal", "Attached", "Prior-year appraisal remains in evidence binder.", "stale NAV")
    ],
    capitalActivity: [
      activity("2026-01-20", "Operating contribution", "$350K", "Matched", "Operating draw matched to treasury ledger.", null),
      activity("2026-04-30", "Management fee", "$75K", "Unreconciled", "Fee invoice is not matched to accounting approval.", "unreconciled")
    ],
    staleDataWarnings: [
      warning("stale NAV", "Real-estate NAV is 149 days old."),
      warning("missing source document", "Attach the current appraisal before using this mark for approvals.")
    ]
  }
};

const SUMMARY_CARDS = [
  summary("Private NAV", "$38.3M", "Aggregate NAV across fixture and imported private asset records.", "warning"),
  summary("Commitments", "$46.1M", "Total subscribed commitments across alternatives.", "default"),
  summary("Unfunded", "$6.8M", "Callable capital still outstanding.", "warning"),
  summary("Explicit warnings", "5 labels", "Operator labels remain literal: manual valuation, statement imported, unreconciled, missing source document, and stale NAV.", "danger")
];

export function buildAlternativesScreenViewModel(selectedAssetId?: string | null): AlternativesScreenViewModel {
  const selectedId = PRIVATE_ASSETS.some((asset) => asset.id === selectedAssetId) ? selectedAssetId : PRIVATE_ASSETS[0].id;
  const privateAssets = PRIVATE_ASSETS.map<PrivateAssetRecord>((asset) => {
    const selected = asset.id === selectedId;
    return {
      ...asset,
      selected,
      rowClassName: selected ? "bg-primary/10" : asset.warnings.some((item) => item.tone === "danger") ? "bg-danger/5" : undefined,
      detailPanelId: "alternatives-asset-detail-drawer",
      selectAriaLabel: `Open private asset detail drawer for ${asset.name}`,
      ariaLabel: `${asset.name}, ${asset.strategy}, NAV ${asset.nav}, warnings ${asset.warnings.map((item) => item.label).join(", ")}`
    };
  });
  const selectedAsset = privateAssets.find((asset) => asset.id === selectedId) ?? privateAssets[0];
  const detailBase = DETAILS[selectedAsset.id];

  return {
    route: ROUTE_METADATA,
    statusChips: [
      { label: "Route", value: ROUTE_METADATA.path },
      { label: "Records", value: `${privateAssets.length} fixture/import` },
      { label: "Integration mode", value: "pre-custodian" }
    ],
    summaryCards: SUMMARY_CARDS,
    privateAssets,
    selectedAsset,
    selectedDetail: {
      assetId: selectedAsset.id,
      title: selectedAsset.name,
      subtitle: `${selectedAsset.strategy} · ${selectedAsset.entity} · ${selectedAsset.sourceLabel}`,
      ...detailBase
    },
    explicitWarnings: EXPLICIT_WARNINGS,
    listEmptyText: "No private asset records are loaded. Import statements or enable fixtures before building direct private-market integrations.",
    detailDrawerTitle: "Private asset detail drawer",
    detailDrawerDescription: "Commitment summary, valuation history, evidence documents, Capital activity, and stale data warnings for the selected private asset."
  };
}

function warning(label: OperatorWarningLabel, detail: string): AlternativesWarning {
  return {
    id: `${label}-${detail.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "")}`,
    label,
    detail,
    tone: label === "stale NAV" || label === "missing source document" ? "danger" : "warning"
  };
}

function summary(label: string, value: string, detail: string, tone: AlternativesTone): AlternativesSummaryCard {
  return {
    id: label.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, ""),
    label,
    value,
    detail,
    tone,
    ariaLabel: `${label}: ${value}. ${detail}`
  };
}

function valuation(asOf: string, value: string, basis: string, source: string, warningLabel: OperatorWarningLabel | null): ValuationHistoryPoint {
  return {
    id: `${asOf}-${basis}`.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
    asOf,
    value,
    basis,
    source,
    warningLabel
  };
}

function documentItem(label: string, status: string, detail: string, warningLabel: OperatorWarningLabel | null): DocumentEvidenceItem {
  return {
    id: `${label}-${status}`.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
    label,
    status,
    detail,
    warningLabel
  };
}

function activity(date: string, type: string, amount: string, status: string, detail: string, warningLabel: OperatorWarningLabel | null): CapitalActivityItem {
  return {
    id: `${date}-${type}`.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
    date,
    type,
    amount,
    status,
    detail,
    warningLabel
  };
}
