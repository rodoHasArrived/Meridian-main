import type { CorporateActionDescriptor } from "@/types";

export type SecurityMasterTab = "overview" | "company" | "corporate-actions" | "print";
export type SecurityMasterStatusFilter = "active" | "all";

export interface SecurityMasterDetailField {
  id: string;
  label: string;
  value: string;
  tone?: "default" | "success" | "warning";
}

export interface SecurityMasterIdentifierSection {
  id: string;
  title: string;
  badge: string;
  meta: string;
  rows: SecurityMasterDetailField[];
}

export interface SecurityMasterAuditItem {
  id: string;
  label: string;
  meta: string;
  current?: boolean;
}

export interface SecurityMasterVenueRow {
  id: string;
  venue: string;
  mic: string;
  ticker: string;
  primary?: boolean;
}

export interface SecurityMasterRationaleItem {
  id: string;
  label: string;
  detail: string;
  weight: string;
}

export interface SecurityMasterCompanyCard {
  title: string;
  subtitle: string;
  description: string;
  logoText: string;
}

export interface SecurityMasterCompanySection {
  id: string;
  title: string;
  badge: string;
  meta: string;
  rows: SecurityMasterDetailField[];
}

export interface SecurityMasterOwnershipBar {
  id: string;
  label: string;
  percent: number;
  tone: "primary" | "success" | "warning" | "muted";
}

export interface SecurityMasterTimelineEvent {
  id: string;
  label: string;
  date: string;
  done?: boolean;
  current?: boolean;
}

/**
 * Canonical-taxonomy chip replacing the bare event-type string: the catalog display name with
 * a small CAEV badge (hidden when `caevCode` is null — internal extensions and unknown event
 * types have no ISO 15022 alignment). Cancelled chips render struck-through with the danger token.
 */
export interface SecurityMasterCorporateActionChip {
  label: string;
  caevCode: string | null;
  cancelled: boolean;
  ariaLabel: string;
}

export type SecurityMasterLifecycleStopId = "announced" | "confirmed" | "ex" | "paid";

/** One of the four fixed lifecycle stops (Announced → Confirmed → Ex → Paid); reached stops render filled. */
export interface SecurityMasterLifecycleStop {
  id: SecurityMasterLifecycleStopId;
  label: string;
  date: string | null;
  reached: boolean;
  current: boolean;
}

/** One supersede-chain event under the lifecycle timeline; `amended` entries carry the "Amended" marker. */
export interface SecurityMasterCorporateActionTimelineEntry {
  corpActId: string;
  label: string;
  detail: string;
  amended: boolean;
}

/** Expandable lifecycle detail for one corporate-action row. */
export interface SecurityMasterCorporateActionLifecycle {
  state: string;
  cancelled: boolean;
  amended: boolean;
  stops: SecurityMasterLifecycleStop[];
  entries: SecurityMasterCorporateActionTimelineEntry[];
}

/**
 * Source facts for one corporate-action row: the presentation fields the table shows plus the
 * canonical descriptor projected by the workbench read model (CorporateActionDescriptorDto).
 * A live adapter produces these by joining `corporateActionDescriptors` to the raw actions on
 * the trust snapshot; the records below seed the same shape with fixture data.
 */
export interface SecurityMasterCorporateActionSeed {
  id: string;
  icon: "dividend" | "split" | "buyback" | "merger" | "spinoff";
  description: string;
  announcedOn: string;
  effectiveOn: string;
  amount: string;
  status: "Confirmed" | "Pending" | "Complete" | "Cancelled";
  note: string;
  descriptor: CorporateActionDescriptor;
}

export interface SecurityMasterCorporateActionRow {
  id: string;
  chip: SecurityMasterCorporateActionChip;
  icon: "dividend" | "split" | "buyback" | "merger" | "spinoff";
  description: string;
  announcedOn: string;
  effectiveOn: string;
  amount: string;
  status: "Confirmed" | "Pending" | "Complete" | "Cancelled";
  note: string;
  expanded: boolean;
  toggleLabel: string;
  lifecycle: SecurityMasterCorporateActionLifecycle;
}

export interface SecurityMasterFilingRow {
  id: string;
  form: string;
  description: string;
  meta: string;
}

export interface SecurityMasterPrintChecklistItem {
  id: string;
  label: string;
  owner: string;
  status: "Ready" | "Review" | "Draft";
}

export interface SecurityMasterPrintSection {
  id: string;
  title: string;
  description: string;
}

export interface SecurityMasterExportEvidence {
  id: string;
  title: string;
  detail: string;
  destination: string;
}

interface SecurityMasterRecord {
  securityId: string;
  displayName: string;
  issuer: string;
  ticker: string;
  status: "Active" | "Pending" | "Inactive";
  assetType: string;
  market: string;
  country: string;
  isin: string;
  score: number;
  searchTags: string[];
  titleCode: string;
  subtitle: string;
  description: string;
  summaryFields: SecurityMasterDetailField[];
  identifierSections: SecurityMasterIdentifierSection[];
  auditTrail: SecurityMasterAuditItem[];
  venues: SecurityMasterVenueRow[];
  rationale: SecurityMasterRationaleItem[];
  companyCard: SecurityMasterCompanyCard;
  companySections: SecurityMasterCompanySection[];
  companyTags: string[];
  companyMetrics: SecurityMasterDetailField[];
  ownership: SecurityMasterOwnershipBar[];
  upcomingActionLabel: string;
  upcomingActionTitle: string;
  upcomingActionDescription: string;
  upcomingActionCountdown: string;
  upcomingActionCountdownLabel: string;
  corporateActionStats: SecurityMasterDetailField[];
  corporateTimeline: SecurityMasterTimelineEvent[];
  corporateActions: SecurityMasterCorporateActionSeed[];
  filings: SecurityMasterFilingRow[];
  printPacketId: string;
  printGeneratedAt: string;
  printDistribution: string;
  printSummary: string;
  printSections: SecurityMasterPrintSection[];
  printChecklist: SecurityMasterPrintChecklistItem[];
  exportEvidence: SecurityMasterExportEvidence[];
}

export interface SecurityMasterSearchRowState {
  securityId: string;
  displayName: string;
  issuer: string;
  ticker: string;
  assetType: string;
  market: string;
  country: string;
  isin: string;
  status: string;
  statusVariant: "success" | "warning" | "outline";
  scoreText: string;
  scoreWidth: string;
  selected: boolean;
  ariaLabel: string;
}

export interface SecurityMasterTabState {
  id: SecurityMasterTab;
  label: string;
  badge: string | null;
  panelId: string;
  selected: boolean;
}

export interface SecurityMasterSelectedState {
  securityId: string;
  title: string;
  issuer: string;
  titleCode: string;
  subtitle: string;
  description: string;
  status: string;
  statusVariant: "success" | "warning" | "outline";
  assetType: string;
  scoreText: string;
  summaryFields: SecurityMasterDetailField[];
  identifierSections: SecurityMasterIdentifierSection[];
  auditTrail: SecurityMasterAuditItem[];
  venues: SecurityMasterVenueRow[];
  rationale: SecurityMasterRationaleItem[];
  companyCard: SecurityMasterCompanyCard;
  companySections: SecurityMasterCompanySection[];
  companyTags: string[];
  companyMetrics: SecurityMasterDetailField[];
  ownership: SecurityMasterOwnershipBar[];
  upcomingActionLabel: string;
  upcomingActionTitle: string;
  upcomingActionDescription: string;
  upcomingActionCountdown: string;
  upcomingActionCountdownLabel: string;
  corporateActionStats: SecurityMasterDetailField[];
  corporateTimeline: SecurityMasterTimelineEvent[];
  corporateActions: SecurityMasterCorporateActionRow[];
  filings: SecurityMasterFilingRow[];
  printPacketId: string;
  printGeneratedAt: string;
  printDistribution: string;
  printSummary: string;
  printSections: SecurityMasterPrintSection[];
  printChecklist: SecurityMasterPrintChecklistItem[];
  exportEvidence: SecurityMasterExportEvidence[];
}

export interface SecurityMasterWorkspaceState {
  searchValue: string;
  statusFilter: SecurityMasterStatusFilter;
  resultCountLabel: string;
  searchMetaLabel: string;
  statusChipLabel: string;
  hasResults: boolean;
  emptyState: { title: string; description: string } | null;
  results: SecurityMasterSearchRowState[];
  tabs: SecurityMasterTabState[];
  selectedSecurity: SecurityMasterSelectedState | null;
}

export const SECURITY_MASTER_DEFAULT_QUERY = "goldman";

const baseIdentifierSections: SecurityMasterIdentifierSection[] = [
  {
    id: "canonical",
    title: "Canonical identity",
    badge: "01",
    meta: "Primary identifiers routed into order, accounting, and package lanes.",
    rows: [
      { id: "isin", label: "ISIN", value: "US38141G1040" },
      { id: "cusip", label: "CUSIP", value: "38141G104" },
      { id: "figi", label: "FIGI", value: "BBG000BLNNH6" },
      { id: "composite", label: "Composite FIGI", value: "BBG000BLNNH6" },
      { id: "lei", label: "LEI", value: "784F5XWPLTWKTBV3E584" },
      { id: "sedol", label: "SEDOL", value: "2407966" }
    ]
  },
  {
    id: "economic",
    title: "Economic definition",
    badge: "02",
    meta: "Shared facts used by valuation, bookings, and policy checks.",
    rows: [
      { id: "currency", label: "Currency", value: "USD" },
      { id: "asset-family", label: "Asset family", value: "Listed equity" },
      { id: "issuer-type", label: "Issuer type", value: "Bank holding company" },
      { id: "country-risk", label: "Country of risk", value: "United States" },
      { id: "effective", label: "Effective from", value: "2024-01-01" },
      { id: "provider-pack", label: "Provider pack", value: "Polygon · Databento · Internal refdata" }
    ]
  }
];

const baseAuditTrail: SecurityMasterAuditItem[] = [
  {
    id: "audit-1",
    label: "Reference package rebuilt with Bloomberg and Polygon parity.",
    meta: "09:18 UTC · packet DK1-SM-417 · owner MK",
    current: true
  },
  {
    id: "audit-2",
    label: "Corporate action chain attested against SEC and issuer relations.",
    meta: "08:54 UTC · filings sync"
  },
  {
    id: "audit-3",
    label: "Export evidence published to operator packet queue.",
    meta: "08:31 UTC · report-pack pipeline"
  }
];

const baseRationale: SecurityMasterRationaleItem[] = [
  {
    id: "rationale-1",
    label: "Matched ISIN, CUSIP, and FIGI without provider drift.",
    detail: "Canonical identifiers align across all enabled feeds.",
    weight: "weight 0.46"
  },
  {
    id: "rationale-2",
    label: "Corporate actions reconciled with no pending override.",
    detail: "Dividend and split history are signed off for export use.",
    weight: "weight 0.29"
  },
  {
    id: "rationale-3",
    label: "Venue coverage includes primary listing plus execution alternates.",
    detail: "Routing aliases remain in sync for workstation lookup and broker maps.",
    weight: "weight 0.25"
  }
];

const baseCompanySections: SecurityMasterCompanySection[] = [
  {
    id: "company-profile",
    title: "Company profile",
    badge: "01",
    meta: "Legal and issuer registration facts.",
    rows: [
      { id: "legal-name", label: "Legal name", value: "The Goldman Sachs Group, Inc." },
      { id: "incorporated", label: "Incorporated", value: "1998-10-02" },
      { id: "headquarters", label: "Headquarters", value: "200 West Street, New York, NY" },
      { id: "website", label: "Website", value: "goldmansachs.com" },
      { id: "cik", label: "CIK", value: "886982" },
      { id: "fiscal-year-end", label: "Fiscal year end", value: "December" }
    ]
  },
  {
    id: "company-coverage",
    title: "Coverage posture",
    badge: "02",
    meta: "Meridian-specific routing and support metadata.",
    rows: [
      { id: "sector", label: "Sector", value: "Financials" },
      { id: "industry", label: "Industry", value: "Capital markets" },
      { id: "sic", label: "SIC", value: "6211 · Security brokers, dealers, and flotation companies" },
      { id: "ir", label: "Investor relations", value: "ir@gs.com · +1 212 902 0300" },
      { id: "coverage", label: "Coverage owner", value: "Data / Security Master desk" },
      { id: "service-level", label: "Review cadence", value: "Intraday with EOD packet sign-off" }
    ]
  }
];

const baseCompanyTags = [
  "Large cap",
  "GSIB",
  "Dividend tracked",
  "Primary venue attested",
  "Execution aliases live"
];

const baseCompanyMetrics: SecurityMasterDetailField[] = [
  { id: "market-cap", label: "Market cap", value: "$149.7B" },
  { id: "employees", label: "Employees", value: "45,300" },
  { id: "ops-risk", label: "Ops risk", value: "Low", tone: "success" },
  { id: "review-window", label: "Review window", value: "T+0 packet" }
];

const baseOwnership: SecurityMasterOwnershipBar[] = [
  { id: "inst", label: "Institutional", percent: 72, tone: "primary" },
  { id: "index", label: "Passive / ETF", percent: 18, tone: "success" },
  { id: "insider", label: "Insider", percent: 3, tone: "warning" },
  { id: "other", label: "Other", percent: 7, tone: "muted" }
];

const equityTimeline: SecurityMasterTimelineEvent[] = [
  { id: "evt-1", label: "Declared", date: "May 01", done: true },
  { id: "evt-2", label: "Ex-date", date: "May 29", done: true },
  { id: "evt-3", label: "Record", date: "May 31", current: true },
  { id: "evt-4", label: "Pay date", date: "Jun 28" },
  { id: "evt-5", label: "Packet", date: "Jun 29" }
];

const equityCorporateActions: SecurityMasterCorporateActionSeed[] = [
  {
    id: "action-1",
    icon: "dividend",
    description: "Quarterly cash dividend",
    announcedOn: "2026-05-01",
    effectiveOn: "2026-06-28",
    amount: "$3.00",
    status: "Confirmed",
    note: "Matches issuer relations and SEC 8-K references. Amount amended from $2.75 at board confirmation.",
    descriptor: {
      corpActId: "ca-gs-div-2026q2-amend",
      canonicalName: "Dividend",
      caevCode: "DVCA",
      displayName: "Cash dividend",
      lifecycleState: "Ex",
      isCancelled: false,
      timeline: [
        { corpActId: "ca-gs-div-2026q2", lifecycleState: "Announced", exDate: "2026-05-29", payDate: "2026-06-28", isAmendment: false },
        { corpActId: "ca-gs-div-2026q2-amend", lifecycleState: "Confirmed", exDate: "2026-05-29", payDate: "2026-06-28", isAmendment: true }
      ]
    }
  },
  {
    id: "action-2",
    icon: "buyback",
    description: "Board-authorized repurchase programme",
    announcedOn: "2026-04-12",
    effectiveOn: "2026-07-01",
    amount: "$5.0B",
    status: "Pending",
    note: "Awaiting final packet annotation from treasury.",
    descriptor: {
      corpActId: "ca-gs-tender-2026",
      canonicalName: "TenderOffer",
      caevCode: "TEND",
      displayName: "Tender offer",
      lifecycleState: "Announced",
      isCancelled: false,
      timeline: [
        { corpActId: "ca-gs-tender-2026", lifecycleState: "Announced", exDate: "2026-07-01", payDate: null, isAmendment: false }
      ]
    }
  },
  {
    id: "action-3",
    icon: "split",
    description: "Historical 2-for-1 reference event",
    announcedOn: "2000-05-18",
    effectiveOn: "2000-06-02",
    amount: "2:1",
    status: "Complete",
    note: "Retained for longitudinal identifier reconciliation.",
    descriptor: {
      corpActId: "ca-gs-split-2000",
      canonicalName: "StockSplit",
      caevCode: "SPLF",
      displayName: "Stock split",
      lifecycleState: "Paid",
      isCancelled: false,
      timeline: [
        { corpActId: "ca-gs-split-2000", lifecycleState: "Confirmed", exDate: "2000-06-02", payDate: "2000-06-02", isAmendment: false }
      ]
    }
  },
  {
    id: "action-4",
    icon: "dividend",
    description: "Special dividend withdrawn by issuer",
    announcedOn: "2026-03-02",
    effectiveOn: "2026-04-10",
    amount: "$1.25",
    status: "Cancelled",
    note: "Issuer withdrew the distribution before record date; retained for audit evidence.",
    descriptor: {
      corpActId: "ca-gs-spec-div-2026-cancel",
      canonicalName: "SpecialDividend",
      caevCode: "DVCA",
      displayName: "Special dividend",
      lifecycleState: "Cancelled",
      isCancelled: true,
      timeline: [
        { corpActId: "ca-gs-spec-div-2026", lifecycleState: "Announced", exDate: "2026-04-10", payDate: "2026-05-01", isAmendment: false },
        { corpActId: "ca-gs-spec-div-2026-cancel", lifecycleState: "Cancelled", exDate: "2026-04-10", payDate: "2026-05-01", isAmendment: true }
      ]
    }
  }
];

const equityFilings: SecurityMasterFilingRow[] = [
  { id: "filing-1", form: "8-K", description: "Dividend declaration and board authorization", meta: "Filed 2026-05-01 · SEC" },
  { id: "filing-2", form: "10-Q", description: "Quarterly operations and capital management update", meta: "Filed 2026-04-24 · SEC" },
  { id: "filing-3", form: "DEF 14A", description: "Annual meeting and shareholder matters", meta: "Filed 2026-03-18 · SEC" }
];

const equityPrintSections: SecurityMasterPrintSection[] = [
  { id: "packet-1", title: "Canonical identifiers", description: "Primary identifiers, aliases, and routing keys with validity windows." },
  { id: "packet-2", title: "Company reference profile", description: "Issuer description, legal facts, SIC, and contact evidence." },
  { id: "packet-3", title: "Corporate actions log", description: "Upcoming and historical events with filing traceability." },
  { id: "packet-4", title: "Operational sign-off", description: "Provider, backfill, and export attestations for workstation handoff." }
];

const equityChecklist: SecurityMasterPrintChecklistItem[] = [
  { id: "check-1", label: "Identifier parity", owner: "Reference Ops", status: "Ready" },
  { id: "check-2", label: "Corporate actions attestation", owner: "Fund Accounting", status: "Ready" },
  { id: "check-3", label: "Packet distribution review", owner: "Reporting", status: "Review" }
];

const equityExports: SecurityMasterExportEvidence[] = [
  {
    id: "export-1",
    title: "Strategy pack",
    detail: "Security profile, aliases, and venue matrix added to the daily strategy packet.",
    destination: "report-pack / strategy"
  },
  {
    id: "export-2",
    title: "Order routing bundle",
    detail: "Primary and alternate tickers aligned for execution route maps.",
    destination: "execution / router-config"
  },
  {
    id: "export-3",
    title: "Accounting evidence",
    detail: "Corporate action packet attached for NAV and ledger review.",
    destination: "accounting / close-support"
  }
];

function makeEquityRecord(overrides: Partial<SecurityMasterRecord> & Pick<SecurityMasterRecord, "securityId" | "displayName" | "ticker" | "market" | "country" | "isin" | "score">): SecurityMasterRecord {
  return {
    issuer: "Goldman Sachs Group Inc",
    status: "Active",
    assetType: "Common stock",
    searchTags: ["goldman", "gs", "bank", "capital markets"],
    titleCode: overrides.ticker,
    subtitle: `${overrides.market} · ${overrides.country}`,
    description: "Canonical security profile aligned to the Meridian browser workstation for search, venue routing, and export evidence.",
    summaryFields: [
      { id: "summary-1", label: "Ticker", value: overrides.ticker },
      { id: "summary-2", label: "Primary venue", value: overrides.market },
      { id: "summary-3", label: "Country", value: overrides.country },
      { id: "summary-4", label: "ISIN", value: overrides.isin },
      { id: "summary-5", label: "Provider pack", value: "3 sources" },
      { id: "summary-6", label: "Last reviewed", value: "11m ago" }
    ],
    identifierSections: baseIdentifierSections.map((section) => ({
      ...section,
      rows: section.rows.map((row) => {
        if (row.id === "isin") {
          return { ...row, value: overrides.isin };
        }
        return row;
      })
    })),
    auditTrail: baseAuditTrail,
    venues: [
      { id: "venue-1", venue: overrides.market, mic: "XNYS", ticker: overrides.ticker, primary: true },
      { id: "venue-2", venue: "BATS Europe", mic: "BATE", ticker: overrides.ticker },
      { id: "venue-3", venue: "Turquoise", mic: "TRQX", ticker: `${overrides.ticker}N` }
    ],
    rationale: baseRationale,
    companyCard: {
      title: "The Goldman Sachs Group, Inc.",
      subtitle: "Global investment banking, securities, and asset-management franchise",
      description: "Meridian tracks Goldman Sachs with venue-aware identifiers, packet-ready corporate action evidence, and export routing tuned for research, accounting, and execution workflows.",
      logoText: "GS"
    },
    companySections: baseCompanySections,
    companyTags: baseCompanyTags,
    companyMetrics: baseCompanyMetrics,
    ownership: baseOwnership,
    upcomingActionLabel: "Next catalyst",
    upcomingActionTitle: "Quarterly dividend packet",
    upcomingActionDescription: "Reference, accounting, and reporting evidence stay aligned ahead of the dividend pay date and packet publication.",
    upcomingActionCountdown: "03",
    upcomingActionCountdownLabel: "days to record date",
    corporateActionStats: [
      { id: "ca-stat-1", label: "Open events", value: "2" },
      { id: "ca-stat-2", label: "Confirmed", value: "1", tone: "success" },
      { id: "ca-stat-3", label: "Amended", value: "1", tone: "warning" },
      { id: "ca-stat-4", label: "Cancelled", value: "1", tone: "warning" }
    ],
    corporateTimeline: equityTimeline,
    corporateActions: equityCorporateActions,
    filings: equityFilings,
    printPacketId: "SM-PACKET-2026-05-31-GS",
    printGeneratedAt: "2026-05-31 09:18 UTC",
    printDistribution: "Data · Reporting · Fund Accounting",
    printSummary: "Print/export evidence packet combines identifiers, venue routing, and corporate actions into a reviewable handoff for downstream workflows.",
    printSections: equityPrintSections,
    printChecklist: equityChecklist,
    exportEvidence: equityExports,
    ...overrides
  };
}

const SECURITY_MASTER_RECORDS: SecurityMasterRecord[] = [
  makeEquityRecord({
    securityId: "gs-common-us",
    displayName: "Goldman Sachs Group Inc",
    ticker: "GS",
    market: "NYSE",
    country: "United States",
    isin: "US38141G1040",
    score: 107.39
  }),
  makeEquityRecord({
    securityId: "gs-common-uk",
    displayName: "Goldman Sachs Group Inc",
    ticker: "GSN",
    market: "Turquoise",
    country: "United Kingdom",
    isin: "US38141GXJ83",
    score: 103.31,
    subtitle: "Turquoise · United Kingdom",
    summaryFields: [
      { id: "summary-1", label: "Ticker", value: "GSN" },
      { id: "summary-2", label: "Primary venue", value: "Turquoise" },
      { id: "summary-3", label: "Country", value: "United Kingdom" },
      { id: "summary-4", label: "ISIN", value: "US38141GXJ83" },
      { id: "summary-5", label: "Provider pack", value: "2 sources" },
      { id: "summary-6", label: "Last reviewed", value: "22m ago" }
    ]
  }),
  {
    ...makeEquityRecord({
      securityId: "gs-bond-de",
      displayName: "Goldman Sachs Group Inc",
      ticker: "GOS",
      market: "XETRA",
      country: "Germany",
      isin: "XS2071624245",
      score: 96.9
    }),
    assetType: "Corporate bond",
    subtitle: "XETRA · Germany",
    titleCode: "GOS 3.625 10/30",
    summaryFields: [
      { id: "summary-1", label: "Ticker", value: "GOS" },
      { id: "summary-2", label: "Primary venue", value: "XETRA" },
      { id: "summary-3", label: "Country", value: "Germany" },
      { id: "summary-4", label: "ISIN", value: "XS2071624245" },
      { id: "summary-5", label: "Coupon", value: "3.625%" },
      { id: "summary-6", label: "Maturity", value: "2030-10-30" }
    ],
    companyCard: {
      title: "Goldman Sachs senior unsecured note",
      subtitle: "Cross-listed fixed-income instrument linked to Goldman Sachs funding curve",
      description: "Bond coverage extends the same canonical identity layer into coupon events, debt package evidence, and execution reference data.",
      logoText: "GS"
    },
    companyMetrics: [
      { id: "market-cap", label: "Issue size", value: "€750M" },
      { id: "employees", label: "Listing venues", value: "3" },
      { id: "ops-risk", label: "Ops risk", value: "Review", tone: "warning" },
      { id: "review-window", label: "Review window", value: "Coupon packet" }
    ],
    upcomingActionLabel: "Next coupon",
    upcomingActionTitle: "Semi-annual coupon checkpoint",
    upcomingActionDescription: "Funding desk and accounting teams need coupon evidence locked before the payment file is exported.",
    upcomingActionCountdown: "11",
    upcomingActionCountdownLabel: "days to coupon date",
    corporateActionStats: [
      { id: "ca-stat-1", label: "Open events", value: "1" },
      { id: "ca-stat-2", label: "Confirmed", value: "1", tone: "success" },
      { id: "ca-stat-3", label: "Needs review", value: "0" },
      { id: "ca-stat-4", label: "Filings linked", value: "2" }
    ],
    corporateTimeline: [
      { id: "evt-1", label: "Declared", date: "Apr 12", done: true },
      { id: "evt-2", label: "Accrual", date: "May 01", done: true },
      { id: "evt-3", label: "Record", date: "Jun 09", current: true },
      { id: "evt-4", label: "Pay date", date: "Jun 20" },
      { id: "evt-5", label: "Packet", date: "Jun 21" }
    ],
    corporateActions: [
      {
        id: "bond-action-1",
        icon: "dividend",
        description: "Semi-annual coupon",
        announcedOn: "2026-04-12",
        effectiveOn: "2026-06-20",
        amount: "€18.13",
        status: "Confirmed",
        note: "Matches paying agent notice and treasury schedule.",
        // Coupon events sit outside the canonical catalog, so the read model fails open to the
        // raw event type with no CAEV alignment — this seed exercises the badge-less chip path.
        descriptor: {
          corpActId: "ca-gos-coupon-2026h1",
          canonicalName: "InterestPayment",
          caevCode: null,
          displayName: "InterestPayment",
          lifecycleState: "Confirmed",
          isCancelled: false,
          timeline: [
            { corpActId: "ca-gos-coupon-2026h1", lifecycleState: "Confirmed", exDate: "2026-06-09", payDate: "2026-06-20", isAmendment: false }
          ]
        }
      },
      {
        id: "bond-action-2",
        icon: "merger",
        description: "Debt programme documentation update",
        announcedOn: "2026-03-28",
        effectiveOn: "2026-04-15",
        amount: "Docs",
        status: "Complete",
        note: "Historical programme amendment retained for evidence.",
        descriptor: {
          corpActId: "ca-gos-namechange-2026",
          canonicalName: "NameChange",
          caevCode: "CHAN",
          displayName: "Name change",
          lifecycleState: "Ex",
          isCancelled: false,
          timeline: [
            { corpActId: "ca-gos-namechange-2026", lifecycleState: "Confirmed", exDate: "2026-04-15", payDate: null, isAmendment: false }
          ]
        }
      }
    ],
    filings: [
      { id: "filing-1", form: "Offering Memo", description: "Base prospectus supplement and coupon schedule", meta: "Updated 2026-04-12 · ECB filing" },
      { id: "filing-2", form: "Agent Notice", description: "Paying agent coupon confirmation", meta: "Updated 2026-05-29 · Clearstream" }
    ],
    printPacketId: "SM-PACKET-2026-06-09-GOS",
    printDistribution: "Data · Treasury Ops · Fund Accounting",
    printSummary: "Debt packet focuses on coupon evidence, paying agent notices, and route-safe identifiers."
  },
  makeEquityRecord({
    securityId: "gs-common-it",
    displayName: "Goldman Sachs Group Inc",
    ticker: "GS",
    market: "EuroTLX",
    country: "Italy",
    isin: "US38141G1040",
    score: 65.6,
    subtitle: "EuroTLX · Italy",
    summaryFields: [
      { id: "summary-1", label: "Ticker", value: "GS" },
      { id: "summary-2", label: "Primary venue", value: "EuroTLX" },
      { id: "summary-3", label: "Country", value: "Italy" },
      { id: "summary-4", label: "ISIN", value: "US38141G1040" },
      { id: "summary-5", label: "Provider pack", value: "2 sources" },
      { id: "summary-6", label: "Last reviewed", value: "34m ago" }
    ]
  }),
  {
    ...makeEquityRecord({
      securityId: "gs-pref-us",
      displayName: "Goldman Sachs Group Inc",
      ticker: "GS.PR.A",
      market: "NYSE",
      country: "United States",
      isin: "US38141G7617",
      score: 58.2
    }),
    assetType: "Preferred share",
    subtitle: "NYSE · Series A preferred",
    summaryFields: [
      { id: "summary-1", label: "Ticker", value: "GS.PR.A" },
      { id: "summary-2", label: "Primary venue", value: "NYSE" },
      { id: "summary-3", label: "Country", value: "United States" },
      { id: "summary-4", label: "ISIN", value: "US38141G7617" },
      { id: "summary-5", label: "Coupon type", value: "Floating" },
      { id: "summary-6", label: "Last reviewed", value: "46m ago" }
    ],
    upcomingActionTitle: "Preferred dividend notice",
    upcomingActionDescription: "Series A dividend packet and floating-rate reference must be attested before export to investor reporting.",
    printPacketId: "SM-PACKET-2026-05-31-GS-PRA"
  }
];

const scoreFormatter = new Intl.NumberFormat("en-US", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2
});

const CORPORATE_ACTION_LIFECYCLE_STOPS: ReadonlyArray<{ id: SecurityMasterLifecycleStopId; label: string; rank: number }> = [
  { id: "announced", label: "Announced", rank: 0 },
  { id: "confirmed", label: "Confirmed", rank: 1 },
  { id: "ex", label: "Ex", rank: 2 },
  { id: "paid", label: "Paid", rank: 3 }
];

function lifecycleProgressRank(state: string): number {
  switch (state) {
    case "Announced":
      return 0;
    case "Confirmed":
      return 1;
    case "Ex":
      return 2;
    case "Paid":
      return 3;
    default:
      // Cancelled and unknown states contribute no forward progress on the four-stop track.
      return -1;
  }
}

export function buildCorporateActionChipState(descriptor: CorporateActionDescriptor): SecurityMasterCorporateActionChip {
  return {
    label: descriptor.displayName,
    caevCode: descriptor.caevCode,
    cancelled: descriptor.isCancelled,
    ariaLabel: [
      descriptor.displayName,
      descriptor.caevCode ? `CAEV ${descriptor.caevCode}` : null,
      descriptor.isCancelled ? "Cancelled" : null
    ].filter((part): part is string => part !== null).join(", ")
  };
}

export function buildCorporateActionLifecycleState(descriptor: CorporateActionDescriptor): SecurityMasterCorporateActionLifecycle {
  const tip = descriptor.timeline.length > 0 ? descriptor.timeline[descriptor.timeline.length - 1] : null;
  // A cancelled action keeps the progress it reached before cancellation, so derive its filled
  // stops from the stored write-side chain states instead of the resolved (Cancelled) state.
  const reachedRank = descriptor.isCancelled
    ? descriptor.timeline.reduce((rank, entry) => Math.max(rank, lifecycleProgressRank(entry.lifecycleState)), -1)
    : lifecycleProgressRank(descriptor.lifecycleState);

  return {
    state: descriptor.lifecycleState,
    cancelled: descriptor.isCancelled,
    amended: descriptor.timeline.some((entry) => entry.isAmendment),
    stops: CORPORATE_ACTION_LIFECYCLE_STOPS.map((stop) => ({
      id: stop.id,
      label: stop.label,
      date: stop.id === "ex" ? tip?.exDate ?? null : stop.id === "paid" ? tip?.payDate ?? null : null,
      reached: stop.rank <= reachedRank,
      current: !descriptor.isCancelled && stop.rank === reachedRank
    })),
    entries: descriptor.timeline.map((entry) => ({
      corpActId: entry.corpActId,
      label: entry.isAmendment ? "Amended" : "Original terms",
      detail: entry.payDate ? `Ex ${entry.exDate} · Pay ${entry.payDate}` : `Ex ${entry.exDate}`,
      amended: entry.isAmendment
    }))
  };
}

export function buildCorporateActionRowState(
  seed: SecurityMasterCorporateActionSeed,
  expanded: boolean
): SecurityMasterCorporateActionRow {
  return {
    id: seed.id,
    chip: buildCorporateActionChipState(seed.descriptor),
    icon: seed.icon,
    description: seed.description,
    announcedOn: seed.announcedOn,
    effectiveOn: seed.effectiveOn,
    amount: seed.amount,
    status: seed.status,
    note: seed.note,
    expanded,
    toggleLabel: `${expanded ? "Collapse" : "Expand"} lifecycle timeline for ${seed.description}`,
    lifecycle: buildCorporateActionLifecycleState(seed.descriptor)
  };
}

export function buildSecurityMasterWorkspaceState({
  query,
  selectedSecurityId,
  activeTab,
  statusFilter,
  expandedCorporateActionIds = []
}: {
  query: string;
  selectedSecurityId: string | null;
  activeTab: SecurityMasterTab;
  statusFilter: SecurityMasterStatusFilter;
  expandedCorporateActionIds?: readonly string[];
}): SecurityMasterWorkspaceState {
  const normalizedQuery = query.trim().toLowerCase();
  const visibleRecords = SECURITY_MASTER_RECORDS.filter((record) => {
    if (statusFilter === "active" && record.status !== "Active") {
      return false;
    }

    if (!normalizedQuery) {
      return true;
    }

    const haystack = [
      record.displayName,
      record.issuer,
      record.ticker,
      record.isin,
      record.market,
      record.country,
      ...record.searchTags
    ].join(" ").toLowerCase();

    return haystack.includes(normalizedQuery);
  }).sort((left, right) => right.score - left.score);

  const selectedRecord = visibleRecords.find((record) => record.securityId === selectedSecurityId) ?? visibleRecords[0] ?? null;
  const resultCountLabel = `${visibleRecords.length} result${visibleRecords.length === 1 ? "" : "s"}`;

  return {
    searchValue: query,
    statusFilter,
    resultCountLabel,
    searchMetaLabel: `${visibleRecords.length} results · 0.184s`,
    statusChipLabel: statusFilter === "active" ? "Status: Active" : "Status: All",
    hasResults: visibleRecords.length > 0,
    emptyState: visibleRecords.length === 0
      ? {
          title: "No matching securities",
          description: "Try a ticker, ISIN, or issuer keyword. Security Master stays in the Data lane and keeps provider, backfill, and export evidence nearby."
        }
      : null,
    results: visibleRecords.map((record) => buildSearchRowState(record, selectedRecord?.securityId ?? null)),
    tabs: buildTabState(activeTab, selectedRecord),
    selectedSecurity: selectedRecord ? buildSelectedSecurityState(selectedRecord, expandedCorporateActionIds) : null
  };
}

function buildSearchRowState(record: SecurityMasterRecord, selectedSecurityId: string | null): SecurityMasterSearchRowState {
  const selected = record.securityId === selectedSecurityId;

  return {
    securityId: record.securityId,
    displayName: record.displayName,
    issuer: record.issuer,
    ticker: record.ticker,
    assetType: record.assetType,
    market: record.market,
    country: record.country,
    isin: record.isin,
    status: record.status,
    statusVariant: record.status === "Active" ? "success" : record.status === "Pending" ? "warning" : "outline",
    scoreText: scoreFormatter.format(record.score),
    scoreWidth: `${Math.max(18, Math.min(100, Math.round((record.score / 110) * 100)))}%`,
    selected,
    ariaLabel: [
      `${selected ? "Selected" : "Open"} ${record.displayName}`,
      `ticker ${record.ticker}`,
      record.assetType,
      `${record.market} ${record.country}`,
      `ISIN ${record.isin}`,
      `score ${scoreFormatter.format(record.score)}`
    ].join(". ")
  };
}

function buildTabState(activeTab: SecurityMasterTab, record: SecurityMasterRecord | null): SecurityMasterTabState[] {
  if (!record) {
    return [];
  }

  return [
    { id: "overview", label: "Overview", badge: `${record.identifierSections.length}`, panelId: "security-master-panel-overview", selected: activeTab === "overview" },
    { id: "company", label: "Company", badge: `${record.companySections.length}`, panelId: "security-master-panel-company", selected: activeTab === "company" },
    { id: "corporate-actions", label: "Corporate actions", badge: `${record.corporateActions.length}`, panelId: "security-master-panel-corporate-actions", selected: activeTab === "corporate-actions" },
    { id: "print", label: "Print / export", badge: `${record.printSections.length}`, panelId: "security-master-panel-print", selected: activeTab === "print" }
  ];
}

function buildSelectedSecurityState(
  record: SecurityMasterRecord,
  expandedCorporateActionIds: readonly string[]
): SecurityMasterSelectedState {
  return {
    securityId: record.securityId,
    title: record.displayName,
    issuer: record.issuer,
    titleCode: record.titleCode,
    subtitle: record.subtitle,
    description: record.description,
    status: record.status,
    statusVariant: record.status === "Active" ? "success" : record.status === "Pending" ? "warning" : "outline",
    assetType: record.assetType,
    scoreText: scoreFormatter.format(record.score),
    summaryFields: record.summaryFields,
    identifierSections: record.identifierSections,
    auditTrail: record.auditTrail,
    venues: record.venues,
    rationale: record.rationale,
    companyCard: record.companyCard,
    companySections: record.companySections,
    companyTags: record.companyTags,
    companyMetrics: record.companyMetrics,
    ownership: record.ownership,
    upcomingActionLabel: record.upcomingActionLabel,
    upcomingActionTitle: record.upcomingActionTitle,
    upcomingActionDescription: record.upcomingActionDescription,
    upcomingActionCountdown: record.upcomingActionCountdown,
    upcomingActionCountdownLabel: record.upcomingActionCountdownLabel,
    corporateActionStats: record.corporateActionStats,
    corporateTimeline: record.corporateTimeline,
    corporateActions: record.corporateActions.map((seed) =>
      buildCorporateActionRowState(seed, expandedCorporateActionIds.includes(seed.id))),
    filings: record.filings,
    printPacketId: record.printPacketId,
    printGeneratedAt: record.printGeneratedAt,
    printDistribution: record.printDistribution,
    printSummary: record.printSummary,
    printSections: record.printSections,
    printChecklist: record.printChecklist,
    exportEvidence: record.exportEvidence
  };
}
