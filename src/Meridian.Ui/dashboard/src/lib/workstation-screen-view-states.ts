import { VIEW_STATE_ENVELOPE_VERSION, type ViewStateEnvelope } from "@/lib/view-state-envelope";

export const WORKSTATION_SCREEN_VIEW_STATE_SCREENS = {
  reportingOperationsRecord: "reporting-operations-record",
  tradingBlotter: "trading-blotter",
  portfolioWorkstation: "portfolio-workstation",
  accountingReconciliation: "accounting-reconciliation",
  dataWorkstation: "data-workstation",
  settingsWorkstation: "settings-workstation"
} as const;

export interface ReportingOperationsRecordViewState {
  selectedStepId: string;
}

export interface TradingBlotterViewState {
  activeTable: "positions" | "orders" | "fills" | "guardrails";
  selectedId: string | null;
}

export interface PortfolioWorkstationViewState {
  selectedKind: "holding" | "run" | "brokerage-account" | "brokerage-position";
  selectedId: string | null;
}

export interface AccountingReconciliationViewState {
  selectedRunId: string | null;
  selectedBreakId: string | null;
}

export interface DataWorkstationViewState {
  workstream: "overview" | "providers" | "operations" | "assurance" | "backfills" | "exports" | "query";
  selectedId: string | null;
}

export interface SettingsWorkstationViewState {
  workstream: "access" | "providers" | "diagnostics" | "feature-coverage";
  selectedId: string | null;
}

export type WorkstationScreenViewStateScreen =
  (typeof WORKSTATION_SCREEN_VIEW_STATE_SCREENS)[keyof typeof WORKSTATION_SCREEN_VIEW_STATE_SCREENS];

export type WorkstationScreenViewStateByScreen = {
  [WORKSTATION_SCREEN_VIEW_STATE_SCREENS.reportingOperationsRecord]: ReportingOperationsRecordViewState;
  [WORKSTATION_SCREEN_VIEW_STATE_SCREENS.tradingBlotter]: TradingBlotterViewState;
  [WORKSTATION_SCREEN_VIEW_STATE_SCREENS.portfolioWorkstation]: PortfolioWorkstationViewState;
  [WORKSTATION_SCREEN_VIEW_STATE_SCREENS.accountingReconciliation]: AccountingReconciliationViewState;
  [WORKSTATION_SCREEN_VIEW_STATE_SCREENS.dataWorkstation]: DataWorkstationViewState;
  [WORKSTATION_SCREEN_VIEW_STATE_SCREENS.settingsWorkstation]: SettingsWorkstationViewState;
};

export function buildWorkstationScreenViewStateEnvelope<Screen extends WorkstationScreenViewStateScreen>(
  screen: Screen,
  state: WorkstationScreenViewStateByScreen[Screen]
): ViewStateEnvelope {
  return {
    v: VIEW_STATE_ENVELOPE_VERSION,
    screen,
    state: { ...state } as Record<string, unknown>
  };
}
