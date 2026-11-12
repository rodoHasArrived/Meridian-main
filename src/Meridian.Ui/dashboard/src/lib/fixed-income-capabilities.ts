import type { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

/** Source evidence for an operation; these references are not operator input or authorization. */
export interface FixedIncomeTraceSource {
  file: string;
  symbol: string;
}

export interface FixedIncomeOperationTrace {
  id: string;
  label: string;
  service: FixedIncomeTraceSource;
  api: readonly (({ routeKey: keyof typeof UI_API_ROUTES } | { route: string }) & { method: "GET" | "POST" | "PUT"; file: string })[] | null;
  client: FixedIncomeTraceSource | null;
  screenAction: FixedIncomeTraceSource | null;
  /** Exact endpoint policy or an explicit missing operator-policy seam; never a client permission grant. */
  permission: string;
  browserAcceptance: {
    scenario: string;
    supportingTest: string | null;
    browserTest: string | null;
    status: "pending" | "synthetic-verified";
  };
  /** Any remaining gap prevents the operation being declared fully accessible. */
  remainingGaps: readonly string[];
}

export interface FixedIncomeCapabilityTrace {
  id: string;
  label: string;
  operations: readonly FixedIncomeOperationTrace[];
}

const dashboard = "src/Meridian.Ui/dashboard/src";
const endpoints = "src/Meridian.Ui.Shared/Endpoints";
const trace = (file: string, symbol: string): FixedIncomeTraceSource => ({ file, symbol });
const ui = (file: string, symbol: string) => trace(`${dashboard}/${file}`, symbol);
const api = (routeKey: keyof typeof UI_API_ROUTES, method: "GET" | "POST" | "PUT", file: string) =>
  ({ routeKey, method, file: `${endpoints}/${file}` });
const acceptance = (scenario: string, supportingTest: string | null = null, browserTest: string | null = null): FixedIncomeOperationTrace["browserAcceptance"] =>
  ({ scenario, supportingTest: supportingTest ? `${dashboard}/${supportingTest}` : null, browserTest, status: "pending" });
const securityRead = "ViewSecurityMaster or ModifySecurityMaster; workstation tenant scope";
const accountRead = "AdminMaintenance, ManageDirectLending, ViewLedgerReports or ManageLedgerReports; authenticated tenant/company and account-owned book";
const accountWrite = "AdminMaintenance, ManageDirectLending or ManageLedgerReports; authenticated tenant/company, account-owned book and fund-scoped write tenant";

/**
 * Traceable operations associated with the workstation feature catalog. A service or a route alone
 * is not an accessible workflow. Missing seams and unexecuted browser acceptance stay explicit.
 * Component tests support a browser scenario; their existence does not certify that scenario.
 */
export const FIXED_INCOME_CAPABILITIES = [
  {
    id: "bond-analytics-migration", label: "Bond analytics and convention migration",
    operations: [
      {
        id: "bond-analytics", label: "Calculate bond analytics",
        service: trace("src/Meridian.Instruments/FixedIncome/BondAnalyticsService.cs", "BondAnalyticsService"),
        api: [api("ReferenceDataBondAnalytics", "GET", "BondAnalyticsEndpoints.cs")],
        client: ui("lib/api/bond-analytics.api.ts", "getBondAnalytics"),
        screenAction: ui("components/meridian/bond-analytics-panel.tsx", "BondAnalyticsPanel"),
        permission: securityRead,
        browserAcceptance: acceptance("From a selected bond or holding, open analytics, supply dates and price/yield, inspect metrics, payment evidence and blockers, then follow the same deep link after reload.", "components/meridian/bond-analytics-panel.test.tsx", "scripts/dev/verify-fixed-income-access.mjs"),
        remainingGaps: []
      },
      {
        id: "bond-convention-migration", label: "Preview and stage convention migration",
        service: trace("src/Meridian.Instruments/FixedIncome/BondMigrationService.cs", "BondMigrationService"),
        api: [api("ReferenceDataBondMigrationPreview", "POST", "BondMigrationEndpoints.cs"), api("ReferenceDataBondMigrationSubmit", "POST", "BondMigrationEndpoints.cs")],
        client: ui("lib/api/bond-migration.api.ts", "stageBondMigration"),
        screenAction: ui("components/meridian/bond-migration-preview-panel.tsx", "BondMigrationPreviewPanel"),
        permission: "ModifySecurityMaster; workstation tenant scope; staging actor comes from the authenticated request",
        browserAcceptance: acceptance("Open migration for the selected bond, preview explicit convention changes, review differences and stage a justified workbench draft; verify stale terms refuse staging.", "components/meridian/bond-migration-preview-panel.test.tsx", "scripts/dev/verify-fixed-income-access.mjs"),
        remainingGaps: ["Adjusted schedules still require retained calendar IDs; a browsable retained-calendar selector is needed to remove internal-ID preparation."]
      }
    ]
  },
  {
    id: "issuer-maturity-ladders", label: "Issuer and maturity ladders",
    operations: [{
      id: "browse-bond-ladders", label: "Browse issuer and maturity ladders",
      service: trace("src/Meridian.Instruments/FixedIncome/IBondReferenceService.cs", "IBondReferenceService"),
      api: [api("ReferenceDataBondIssuerLadderPage", "GET", "BondReferencePagingEndpoints.cs"), api("ReferenceDataBondMaturityLadderPage", "GET", "BondReferencePagingEndpoints.cs")],
      client: ui("lib/api/bond-reference.api.ts", "getBondIssuerLadderPage"),
      screenAction: ui("components/meridian/bond-reference-ladder-panel.tsx", "BondReferenceLadderPanel"),
      permission: securityRead,
      browserAcceptance: acceptance("Before selecting any security, search an issuer or maturity window, page the ladder and activate a row by keyboard to inspect that exact Security Master instrument.", "components/meridian/bond-reference-ladder-panel.test.tsx", "scripts/dev/verify-fixed-income-access.mjs"),
      remainingGaps: []
    }]
  },
  {
    id: "contractual-cash-flow-scenarios", label: "Contractual cash-flow scenarios",
    operations: [{
      id: "project-and-retain-cash-flows", label: "Preview, retain and replay contractual cash flows",
      service: trace("src/Meridian.Contracts/CashFlows/ContractualCashFlowContracts.cs", "IContractualCashFlowPreviewService"),
      api: [api("SecurityMasterCashFlowProjectionPreview", "POST", "SecurityMasterEndpoints.CashFlowPreview.cs"), api("SecurityMasterCashFlowProjectionRuns", "POST", "ContractualCashFlowRunEndpoints.cs"), api("SecurityMasterCashFlowProjectionRun", "GET", "ContractualCashFlowRunEndpoints.cs")],
      client: ui("lib/api/contractual-cash-flow.api.ts", "retainContractualCashFlowRun"),
      screenAction: ui("components/meridian/contractual-cash-flow-workbench.tsx", "ContractualCashFlowWorkbench"),
      permission: `${securityRead} for reads/preview; ModifySecurityMaster to retain security runs. Loan routes use ViewDirectLending/ManageDirectLending and their fund scope.`,
      browserAcceptance: acceptance("Open a selected asset or loan, choose dates, multiple prepayments and exercise notice dates, preview and retain, then browse retained runs and reproduce the selected result.", "components/meridian/contractual-cash-flow-workbench.test.tsx"),
      remainingGaps: ["Curve/version selection is not exposed by the workbench.", "Retained-run store and API expose save/get-by-ID, with no list seam for browsable history."]
    }]
  },
  {
    id: "portfolio-cash-ladder", label: "Portfolio cash ladder",
    operations: [{
      id: "inspect-cash-ladder-contributions", label: "Inspect contributing flows and retained projection provenance",
      service: trace("src/Meridian.Ui.Shared/Services/PortfolioCashLadderReadService.cs", "PortfolioCashLadderReadService"),
      api: [{ route: "/api/portfolio/cash-ladder", method: "GET", file: `${endpoints}/PortfolioCashLadderEndpoints.cs` }],
      client: ui("lib/api/portfolio-cash-ladder.api.ts", "getPortfolioCashLadder"),
      screenAction: ui("screens/cash-ladder-screen.tsx", "CashLadderScreen"),
      permission: "ViewTrades; GET /api/portfolio/cash-ladder in PortfolioCashLadderEndpoints.cs",
      browserAcceptance: acceptance("Open Portfolio cash ladder, inspect a bucket, open a contributing instrument and its exact retained AssetOperations projection run; inspect terms/source references and any unavailable-run blocker.", "screens/cash-ladder-screen.test.tsx"),
      remainingGaps: ["Source-domain/entity references are not Evidence Vault artifact identities; an artifact link requires an authoritative mapping."]
    }]
  },
  {
    id: "account-forecasts-close-prices", label: "Account forecasts and reviewed close prices",
    operations: [
      {
        id: "account-cash-forecast", label: "Prepare and retain account cash forecasts with dated FX",
        service: trace("src/Meridian.Instruments/AssetOperations/AccountCashForecastService.cs", "AccountCashForecastService"),
        api: [api("LedgerAccountCashForecasts", "GET", "LedgerEndpoints.AccountOversight.cs"), api("LedgerAccountCashForecasts", "POST", "LedgerEndpoints.AccountOversight.cs")],
        client: ui("lib/api/account-oversight.api.ts", "captureCashForecast"),
        screenAction: ui("screens/accounting-screen.account-cash-forecasts.tsx", "AccountCashForecastsPanel"),
        permission: `${accountRead} for history; ${accountWrite} for capture`,
        browserAcceptance: acceptance("From account oversight, select an account-owned book, prepare from a retained snapshot, inspect opening cash, projected flows and dated FX, capture with a rationale and inspect immutable history.", "screens/accounting-screen.account-oversight.test.tsx"),
        remainingGaps: ["First-run discovery of retained opening observations, projection flows and FX requires a shared preparation/source-selection API; snapshot reuse cannot create missing source evidence."]
      },
      {
        id: "review-account-close-prices", label: "Capture and review account close prices",
        service: trace("src/Meridian.Contracts/SecurityMaster/ISecurityMasterPricingService.cs", "ISecurityMasterPricingService"),
        api: [api("LedgerAccountClosePriceSets", "GET", "LedgerEndpoints.AccountOversight.cs"), api("LedgerAccountClosePriceSets", "POST", "LedgerEndpoints.AccountOversight.cs"), api("LedgerAccountClosePriceSetApproval", "POST", "LedgerEndpoints.AccountOversight.cs")],
        client: ui("lib/api/account-oversight.api.ts", "approveClosePriceSet"),
        screenAction: ui("screens/accounting-screen.account-close-prices.tsx", "AccountClosePricesPanel"),
        permission: `${accountRead} for history; ${accountWrite} for capture/review, with existing reviewer checks`,
        browserAcceptance: acceptance("Select the account book and valuation date, inspect price sources and freshness, capture a close-price set, then review it with a distinct authorized reviewer.", "screens/accounting-screen.account-oversight.test.tsx"),
        remainingGaps: []
      }
    ]
  },
  {
    id: "curves-and-pricing-hierarchy", label: "Curve management and pricing-source hierarchy",
    operations: [
      {
        id: "inspect-and-select-curves", label: "Inspect curves and published versions",
        service: trace("src/Meridian.Contracts/SecurityMaster/SecurityCurves.cs", "ISecurityCurveStore"),
        api: [api("SecurityMasterCurves", "GET", "SecurityMasterEndpoints.Curves.cs"), api("SecurityMasterCurveVersions", "GET", "SecurityMasterEndpoints.Curves.cs")],
        client: null, screenAction: null, permission: "ViewSecurityMaster or ModifySecurityMaster to inspect; ModifySecurityMaster to define/publish. Routes require a registered ISecurityCurveStore.",
        browserAcceptance: acceptance("Browse available curves and retained versions, inspect as-of/cutoff/freshness, and choose an exact version as a calculation input; explain missing configuration."),
        remainingGaps: ["No curve inspection/version selection browser client or screen workflow."]
      },
      {
        id: "manage-pricing-source-priority", label: "Inspect and change pricing-source priority",
        service: trace("src/Meridian.Contracts/SecurityMaster/ISecurityMasterPricingService.cs", "ISecurityMasterPricingService"),
        api: [api("SecurityMasterPricingHierarchy", "GET", "SecurityMasterEndpoints.cs"), api("SecurityMasterPricingHierarchy", "PUT", "SecurityMasterEndpoints.cs")],
        client: null, screenAction: null, permission: "ModifySecurityMaster and authenticated actor for hierarchy mutation; existing read endpoint has no route-level RequirePermission declaration.",
        browserAcceptance: acceptance("Inspect selected instrument/account pricing priorities and freshness, preview the effect of a reorder and save through existing modification authority."),
        remainingGaps: ["No source-priority browser client/control or freshness explanation workflow."]
      }
    ]
  },
  {
    id: "bond-settlement-consideration", label: "Settlement consideration",
    operations: [{
      id: "preview-bond-settlement", label: "Prepare settlement consideration",
      service: trace("src/Meridian.FinancialOperations/Ledger/BondSettlementConsiderationService.cs", "BondSettlementConsiderationService"),
      api: [api("ReferenceDataBondSettlementConsideration", "POST", "BondAnalyticsEndpoints.cs")],
      client: null, screenAction: null, permission: securityRead,
      browserAcceptance: acceptance("From a selected holding/trade, prepare clean cost, accrued interest, fees and total cash, inspect reconciliation differences and evidence, without posting."),
      remainingGaps: ["No settlement preparation browser client or holding/trade input assembly and preview."]
    }]
  },
  {
    id: "bond-amortization", label: "Bond amortization",
    operations: [{
      id: "govern-bond-amortization", label: "Preview, review, draft, post and reverse amortization",
      service: trace("src/Meridian.FinancialOperations/Ledger/CanonicalLotAmortizationService.cs", "CanonicalLotAmortizationService"),
      api: null, client: null, screenAction: null,
      permission: "Missing operator API permission binding; implementation must preserve ledger scope, independent review, period locks and immutable posting/reversal controls.",
      browserAcceptance: acceptance("Select supported lots/method and retained evidence, preview, review, create a draft, post through ordinary ledger controls, and reverse using retained original economics."),
      remainingGaps: ["Calculation services have no complete governed operator API/client/browser workflow for supported methods."]
    }]
  },
  {
    id: "factor-paydowns", label: "Factor paydowns",
    operations: [{
      id: "govern-factor-paydown", label: "Prepare and review a factor paydown",
      service: trace("src/Meridian.FinancialOperations/Ledger/FactorPaydownLotInstructionService.cs", "FactorPaydownLotInstructionService"),
      api: [api("LedgerFactorPaydownPreparation", "POST", "LedgerEndpoints.FactorPaydown.cs"), api("LedgerFactorPaydownDrafts", "POST", "LedgerEndpoints.FactorPaydown.cs")],
      client: ui("lib/api/factor-paydown.api.ts", "prepareFactorPaydown"),
      screenAction: ui("screens/accounting-screen.factor-paydown-panel.tsx", "FactorPaydownPanel"),
      permission: "AdminMaintenance, ManageDirectLending or ManageLedgerReports; authenticated tenant/company, ledger mutation authority, exact book/period and independent journal lifecycle controls",
      browserAcceptance: acceptance("Launch from the selected holding and retained factor observation, inspect prefilled evidence and per-lot effects, create a justified draft and retain ordinary review/post controls.", "screens/accounting-screen.factor-paydown-panel.test.tsx"),
      remainingGaps: ["Ledger explorer workbench still requires manually entered scope IDs and factor evidence JSON; contextual holding/observation launch is missing."]
    }]
  },
  {
    id: "provider-bond-discovery", label: "Provider-backed bond discovery",
    operations: [{
      id: "discover-and-stage-bonds", label: "Discover provider bonds and stage Security Master import",
      service: trace("src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaSymbolSearchProvider.cs", "AlpacaSymbolSearchProvider"),
      api: null, client: null, screenAction: null,
      permission: "Missing bond-specific browser discovery/import binding; provider entitlement and ModifySecurityMaster draft authority must remain explicit.",
      browserAcceptance: acceptance("Search provider fixed-income references, inspect provider evidence and duplicate identity matches, stage an import, then use normal Security Master review/publish controls."),
      remainingGaps: ["Adapter supports fixed_income classification below the browser search path; no complete bond search and staged-import operator workflow."]
    }]
  }
] as const satisfies readonly FixedIncomeCapabilityTrace[];

export type FixedIncomeCapabilityId = (typeof FIXED_INCOME_CAPABILITIES)[number]["id"];
