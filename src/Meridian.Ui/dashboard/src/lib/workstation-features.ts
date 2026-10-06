import { ACCOUNTING_NAVIGATION_ITEMS } from "@/lib/accounting-navigation";
import { WORKSTATION_ROUTE_CATALOG, workspacePath, type WorkstationRouteKey } from "@/lib/workspace";
import type { WorkspaceKey } from "@/types";

export interface WorkstationFeatureDefinition {
  id: string;
  label: string;
  description: string;
  route: string;
  /** Search aliases stay separate from the purpose shown to the operator. */
  keywords: readonly string[];
  paletteLabel?: string;
  match?: "exact" | "prefix";
}

function feature(
  routeKey: WorkstationRouteKey,
  label: string,
  description: string,
  keywords: readonly string[] = [],
  options: Partial<Pick<WorkstationFeatureDefinition, "id" | "paletteLabel" | "match">> = {}
): WorkstationFeatureDefinition {
  const route = WORKSTATION_ROUTE_CATALOG[routeKey];
  return { id: route.slice(1).replaceAll("/", "-"), label, description, keywords, route, ...options };
}

type FeaturePurpose = Pick<WorkstationFeatureDefinition, "description" | "keywords" | "paletteLabel"> & { id?: string };

// Accounting keeps its lifecycle groups and active detail-route ownership in its
// existing browse model. Both navigation and search consume those same routes.
const ACCOUNTING_FEATURE_PURPOSES: Record<string, FeaturePurpose> = {
  today: { description: "Review today's accounting work, close blockers, and next actions.", keywords: ["daily close", "accounting overview"] },
  "operations-continuity": { description: "Follow the close checklist, handoffs, decisions, and retained evidence.", keywords: ["close workflow", "handoff", "month end"] },
  "close-calendar": { description: "Review accounting periods, close dates, and upcoming deadlines.", keywords: ["period close", "month end", "deadlines"] },
  ledger: { description: "Explore ledger balances, trial balance, and the records behind each amount.", keywords: ["general ledger", "trial balance", "account balances"] },
  adjustments: { description: "Review and prepare journal entries and accounting adjustments.", keywords: ["journal entries", "debits credits", "adjustment"] },
  "capital-accounts": { description: "Review investor capital balances and retained capital-account activity.", keywords: ["investor balances", "capital statements"] },
  "capital-calls": { description: "Review capital calls, contributions, and funding obligations.", keywords: ["investor funding", "contributions", "commitments"] },
  "security-master": { description: "Review reference-data coverage, identifier conflicts, and trusted instruments.", keywords: ["instrument reference data", "security identifiers", "assets"] },
  "statement-import": { description: "Upload a statement, preview its records, and validate it before import.", keywords: ["import statement", "upload statement", "bank statement", "broker statement"] },
  casework: { id: "accounting-reconciliation", paletteLabel: "Reconciliation breaks", description: "Match source activity, investigate differences, and review reconciliation sign-off.", keywords: ["casework", "match transactions", "account differences", "reconcile accounts"] },
  "external-gl": { id: "accounting-external-gl-reconciliation", paletteLabel: "External GL reconciliation", description: "Compare Meridian balances with a connected accounting system before posting or export.", keywords: ["external general ledger", "accounting system balances", "quickbooks"] },
  exceptions: { description: "Investigate accounting exceptions and the blockers that need attention.", keywords: ["errors", "issues", "breaks", "blockers"] },
  approvals: { description: "Review controlled decisions awaiting approval and their supporting evidence.", keywords: ["approval inbox", "pending review", "sign off"] },
  "entity-setup": { description: "Set up entities and the fund structure used by accounting workflows.", keywords: ["fund setup", "entity structure", "new entity"] },
  configure: { description: "Configure ledger books, accounts, mappings, and posting rules.", keywords: ["accounting setup", "chart of accounts", "posting rules", "ledger books"] }
};

/** Primary destinations shared by the sidebar and local feature search. */
export const WORKSPACE_NAVIGATION_FEATURES: Readonly<Record<WorkspaceKey, readonly WorkstationFeatureDefinition[]>> = {
  trading: [
    feature("trading", "Overview", "Review trading activity and the next controlled action.", [], { match: "exact" }),
    feature("tradingOrders", "Orders", "Review trading orders and their execution status.", ["order status", "executions", "fills"]),
    feature("tradingPositions", "Positions", "Inspect trading positions and current exposure.", ["holdings", "open positions"]),
    feature("tradingRisk", "Risk", "Review trading risk and the limits affecting execution.", ["risk limits", "exposure", "buying power"]),
    feature("tradingReadiness", "Readiness", "Review paper cockpit blockers, operator work items, and promotion evidence.", ["ready to trade", "trading blockers", "paper trading checks"], { paletteLabel: "Readiness console" })
  ],
  portfolio: [
    feature("portfolio", "Overview", "Review portfolio holdings, exposure, and performance.", [], { match: "exact" }),
    feature("portfolioAttribution", "Attribution", "Explore what contributed to portfolio performance.", ["performance attribution", "returns", "profit and loss"]),
    feature("portfolioAssetDetail", "Asset detail", "Inspect an asset's positions, activity, and supporting records.", ["asset positions", "holding details"]),
    feature("portfolioBrokerageSync", "Brokerage sync", "Review household brokerage account sync posture and recovery actions.", ["sync broker", "brokerage account", "account synchronization"]),
    feature("portfolioCashLadder", "Cash ladder", "Review projected cash flows and upcoming funding needs.", ["cash forecast", "cash flow", "liquidity", "funding schedule"]),
    feature("portfolioFamilyOffice", "Family office", "Review family net worth, entity ownership, asset-class exposure, commitments, breaks, and stale valuations.", ["net worth", "household", "family assets"]),
    feature("portfolioLoanBook", "Loan book", "Review lending exposure, loan balances, and servicing activity.", ["loans", "loan portfolio", "lending", "debt"])
  ],
  accounting: ACCOUNTING_NAVIGATION_ITEMS.map((item) => {
    const purpose = ACCOUNTING_FEATURE_PURPOSES[item.id];
    return { ...item, ...purpose, id: purpose.id ?? `accounting-${item.id}` };
  }),
  reporting: [
    feature("reporting", "Overview", "Review reporting work and the outputs needing attention.", [], { match: "exact" }),
    feature("reportingLibrary", "Report Library", "Find a report template and choose the report you need.", ["available reports", "report templates", "find report"]),
    feature("reportingScheduled", "Scheduled Reports", "Review recurring reports and their delivery schedules.", ["automated reports", "recurring reports", "report schedule"]),
    feature("reportingRunParameters", "Run Report", "Choose a report, set its parameters, and review it before running.", ["run a report", "generate report", "create report"]),
    feature("reportingOperationsRecord", "Operations record", "Trace source records through accounting evidence and report-pack publication.", ["source to report", "audit trail", "operations workflow"]),
    feature("reportingReportPacks", "Report packs", "Open approval-ready report packet review and governed outputs.", ["report packet", "review reports", "delivery evidence"]),
    feature("reportingEvidence", "Evidence", "Inspect packet completeness, stale evidence, and lineage.", ["audit evidence", "provenance", "report lineage"], { paletteLabel: "Evidence workbench" }),
    feature("reportingExports", "Exports", "Run on-demand reports and review generated export run posture.", ["download reports", "export report", "report files"])
  ],
  strategy: [
    feature("strategy", "Overview", "Review strategy runs, comparisons, and research progress.", [], { match: "exact" }),
    feature("strategyDesigner", "Designer", "Build and review a strategy definition before testing it.", ["design strategy", "create strategy", "strategy builder"]),
    feature("strategyCoveredCall", "Covered call", "Configure covered-call chain preview, run backtests, and review payoff evidence.", ["covered call backtest", "options strategy", "payoff"], { paletteLabel: "Covered call backtest" }),
    feature("strategyPromotions", "Promotions", "Review the evidence gates for advancing a tested strategy to paper trading.", ["strategy promotion", "paper trading approval", "promote strategy"]),
    feature("strategyLab", "Strategy Lab", "Test a strategy with reproducible inputs and review the results.", ["backtest", "test strategy", "research strategies"]),
    feature("strategyQuantLab", "Quant Lab", "Run scripts with parameter hints, templates, plots, and metrics.", ["research scripts", "quantitative research", "python"]),
    feature("strategyRunLedger", "Run Ledger Explorer", "Inspect a strategy run's simulated ledger, trial balance, and journal evidence.", ["backtest ledger", "simulation journals", "strategy accounting"])
  ],
  data: [
    feature("data", "Overview", "Review data collection, provider health, and data quality.", [], { match: "exact" }),
    feature("dataImport", "Import data", "Load a file, preview its records, and validate the import before committing it to a governed data workflow.", ["upload data", "import file", "csv import"]),
    feature("dataProviders", "Providers", "Review provider catalog, onboarding posture, connection health, and routing evidence.", ["data provider health", "data connections", "connection status"]),
    feature("dataQuotes", "Market data", "Inspect quotes, trades, depth, charts, and staged tickets.", ["live quotes", "prices", "market charts", "ticker"], { paletteLabel: "Live quotes" }),
    feature("dataOperations", "Ingestion operations", "Preview, trigger, and review historical data backfill jobs.", ["historical prices", "price history", "historical data", "backfill", "ingestion operations"], { id: "data-backfills", paletteLabel: "Backfill queues" }),
    feature("dataAssurance", "Storage assurance", "Review stored-data integrity, coverage, and assurance checks.", ["data quality", "storage checks", "data integrity"]),
    feature("dataExports", "Exports", "Review and export retained market data for downstream use.", ["download data", "export market data", "data files"]),
    feature("dataQuery", "SQL query", "Query retained data with SQL and inspect the returned records.", ["sql", "query data", "database query"])
  ],
  settings: [
    feature("settings", "Overview", "Review workstation preferences, access, and connection setup.", [], { match: "exact" }),
    feature("settingsPreferences", "Preferences", "Adjust workstation preferences and display behavior.", ["appearance", "display settings", "personal preferences"]),
    feature("settingsAccess", "Access", "Review the controls governing operator access.", ["permissions", "roles", "users", "operator access"]),
    feature("settingsProviders", "Provider Connections", "Connect and verify data and brokerage providers.", ["connect provider", "connect broker", "provider setup", "credentials"]),
    feature("settingsAccountingSystems", "Accounting Systems", "Configure the connected accounting system and review its integration setup.", ["connect accounting", "accounting integration", "external gl setup"]),
    feature("settingsDiagnostics", "Diagnostics", "Inspect workstation health and find recovery information.", ["troubleshoot", "system health", "recovery", "errors"])
  ]
};

/** Existing deep-link views and useful wired routes outside the primary rail. */
const FEATURE_SHORTCUTS: readonly WorkstationFeatureDefinition[] = [
  { id: "data-watchlist", label: "Watchlist", description: "Add symbols and starter packs from the Market Data desk watchlist view.", keywords: ["watch list", "follow symbols", "add ticker"], route: `${WORKSTATION_ROUTE_CATALOG.dataQuotes}?view=watchlist` },
  { id: "data-alerts", label: "Price alerts", description: "Create local quote-threshold alerts from the Market Data desk alerts view.", keywords: ["price notification", "quote alert", "price threshold"], route: `${WORKSTATION_ROUTE_CATALOG.dataQuotes}?view=alerts` },
  { id: "strategy-formula-workbench", label: "Formula Workbench", description: "Author cell-based strategy formulas from the Quant Lab formulas tab.", keywords: ["strategy formulas"], route: `${WORKSTATION_ROUTE_CATALOG.strategyQuantLab}?view=formulas` },
  feature("settingsAlpacaProviderGuidedSetup", "Alpaca guided setup", "Configure and verify paper credentials before reviewing advanced runtime evidence.", ["connect alpaca", "alpaca credentials", "paper account setup"], { id: "settings-provider-setup" }),
  feature("reportingReportBuilder", "Report Builder", "Design a governed report and review its output configuration.", ["build report", "custom report", "report design"]),
  feature("reportingRunStatus", "Report Run Status", "Review report jobs, completed runs, and failures needing attention.", ["report queue", "report progress", "report history"]),
  feature("reportingGovernance", "Reporting Governance", "Review report access, approval, lifecycle, and audit controls.", ["report permissions", "report approvals", "report audit"])
];

export const WORKSTATION_SEARCH_FEATURES: readonly WorkstationFeatureDefinition[] = [
  ...Object.entries(WORKSPACE_NAVIGATION_FEATURES).flatMap(([workspaceKey, features]) =>
    features.filter((entry) => entry.route !== workspacePath(workspaceKey as WorkspaceKey))
  ),
  ...FEATURE_SHORTCUTS
];
