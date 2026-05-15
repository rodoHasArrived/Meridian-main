import { useCallback, useMemo, useState } from "react";

export type StrategyLegKind =
  | "long-call"
  | "short-call"
  | "long-put"
  | "short-put"
  | "long-stock"
  | "short-stock";

export type StrategyLegDirection = "Long" | "Short";
export type StrategyLegInstrument = "Call" | "Put" | "Stock";

export interface StrategyLeg {
  id: string;
  kind: StrategyLegKind;
  label: string;
  direction: StrategyLegDirection;
  instrument: StrategyLegInstrument;
  quantity: number;
  strike: number;
  premium: number;
}

export interface StrategyLegPaletteEntry {
  kind: StrategyLegKind;
  label: string;
  description: string;
  badge: string;
  defaults: Omit<StrategyLeg, "id" | "label">;
}

export interface PayoffPoint {
  price: number;
  pnl: number;
}

export interface PayoffPolylinePoint extends PayoffPoint {
  x: number;
  y: number;
}

export interface PayoffChartViewModel {
  width: number;
  height: number;
  paddingLeft: number;
  paddingRight: number;
  paddingTop: number;
  paddingBottom: number;
  points: PayoffPolylinePoint[];
  zeroLineY: number | null;
  spotLineX: number | null;
  spotPrice: number;
  axisLabels: {
    minPrice: string;
    maxPrice: string;
    minPnl: string;
    maxPnl: string;
  };
  breakEvenPrices: number[];
  maxProfit: number;
  maxLoss: number;
  netDebit: number;
  caption: string;
  ariaLabel: string;
  isEmpty: boolean;
}

export interface ParticipationSliceViewModel {
  legId: string;
  label: string;
  direction: StrategyLegDirection;
  instrument: StrategyLegInstrument;
  notional: number;
  share: number;
  sharePercent: string;
  barWidth: string;
  tone: "long" | "short";
  detail: string;
}

export interface ParticipationViewModel {
  totalNotional: number;
  totalNotionalLabel: string;
  longNotional: number;
  shortNotional: number;
  netDirection: "Long" | "Short" | "Flat";
  netDirectionLabel: string;
  slices: ParticipationSliceViewModel[];
  isEmpty: boolean;
}

export interface DesignerSummaryMetric {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface StrategyDesignerCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  confirmationPending?: boolean;
}

export interface StrategyDesignerSpotPriceFieldViewModel {
  label: string;
  value: string;
  min: number;
  step: string;
  inputMode: "decimal";
  ariaLabel: string;
}

export interface StrategyLegDetailFieldViewModel {
  id: string;
  label: string;
  value: string;
  tone: "default" | "muted" | "success" | "warning" | "danger";
}

export interface StrategyLegDetailEmptyStateViewModel {
  title: string;
  description: string;
  ariaLabel: string;
}

export interface StrategyLegDetailViewModel {
  id: string;
  ariaLabel: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: "success" | "warning" | "outline";
  fields: StrategyLegDetailFieldViewModel[];
}

export type StrategyBuilderCellKind =
  | "visual"
  | "formula"
  | "code"
  | "governance"
  | "options-payoff"
  | "state"
  | "concurrent"
  | "universe-builder"
  | "trade";

export type StrategyBuilderCellPurpose =
  | "universe"
  | "filter"
  | "rank"
  | "risk"
  | "backtest"
  | "governance"
  | "options-payoff"
  | "state"
  | "concurrent"
  | "trade";
export type StrategyBuilderMessageSeverity = "error" | "warning" | "info";

export interface StrategyBuilderFieldCatalogItem {
  fieldId: string;
  label: string;
  source: string;
  dataSet: string;
  typeName: string;
  description: string;
  isEnabled: boolean;
  disabledReason: string | null;
  synonyms: string[];
}

export interface StrategyBuilderFieldCatalogSearchState {
  inputId: string;
  label: string;
  ariaLabel: string;
  placeholder: string;
  helperId: string;
  helperText: string;
  describedBy: string;
  resultsLabel: string;
  resultCountText: string;
  emptyState: {
    title: string;
    description: string;
    ariaLabel: string;
  } | null;
}

export interface StrategyBuilderCell {
  cellId: string;
  label: string;
  kind: StrategyBuilderCellKind;
  purpose: StrategyBuilderCellPurpose;
  source: string;
  fieldRefs: string[];
  parameters?: Record<string, string>;
}

export interface StateCellParameters {
  entryCondition?: string;
  exitCondition: string;
  stateLabel: string;
  isTerminal?: string;
}

export type ConcurrentCellSemantics = "all-pass" | "any-pass" | "first-wins";

export interface ConcurrentCellParameters {
  branchIds: string;
  semantics: ConcurrentCellSemantics;
}

export interface UniverseBuilderCellParameters {
  assetClass: string;
  includeRules: string;
  excludeRules?: string;
  minSize?: string;
  maxSize?: string;
}

export type TradeCellInstrumentType = "Equity" | "Option" | "Future" | "ETF";
export type TradeCellDirection = "Buy" | "Sell" | "SellShort" | "BuyToCover";
export type TradeCellSizingMethod = "FixedShares" | "FixedNotional" | "PercentAUM" | "EqualWeight";

export interface TradeCellParameters {
  instrument: TradeCellInstrumentType;
  direction: TradeCellDirection;
  sizingMethod: TradeCellSizingMethod;
  priceConstraint?: string;
  sizingValue?: string;
}

export function getStateCellParams(cell: StrategyBuilderCell): StateCellParameters | null {
  if (cell.kind !== "state" || !cell.parameters) return null;
  const exitCondition = cell.parameters["exitCondition"];
  const stateLabel = cell.parameters["stateLabel"];
  if (!exitCondition || !stateLabel) return null;
  return {
    entryCondition: cell.parameters["entryCondition"],
    exitCondition,
    stateLabel,
    isTerminal: cell.parameters["isTerminal"]
  };
}

export function getConcurrentCellParams(cell: StrategyBuilderCell): ConcurrentCellParameters | null {
  if (cell.kind !== "concurrent" || !cell.parameters) return null;
  const branchIds = cell.parameters["branchIds"];
  const semantics = cell.parameters["semantics"] as ConcurrentCellSemantics | undefined;
  const validSemantics: ConcurrentCellSemantics[] = ["all-pass", "any-pass", "first-wins"];
  if (!branchIds || !semantics || !validSemantics.includes(semantics)) return null;
  return { branchIds, semantics };
}

export function getUniverseBuilderCellParams(cell: StrategyBuilderCell): UniverseBuilderCellParameters | null {
  if (cell.kind !== "universe-builder" || !cell.parameters) return null;
  const assetClass = cell.parameters["assetClass"];
  const includeRules = cell.parameters["includeRules"];
  if (!assetClass || !includeRules) return null;
  return {
    assetClass,
    includeRules,
    excludeRules: cell.parameters["excludeRules"],
    minSize: cell.parameters["minSize"],
    maxSize: cell.parameters["maxSize"]
  };
}

export function getTradeCellParams(cell: StrategyBuilderCell): TradeCellParameters | null {
  if (cell.kind !== "trade" || !cell.parameters) return null;
  const instrument = cell.parameters["instrument"] as TradeCellInstrumentType | undefined;
  const direction = cell.parameters["direction"] as TradeCellDirection | undefined;
  const sizingMethod = cell.parameters["sizingMethod"] as TradeCellSizingMethod | undefined;
  const validInstruments: TradeCellInstrumentType[] = ["Equity", "Option", "Future", "ETF"];
  const validDirections: TradeCellDirection[] = ["Buy", "Sell", "SellShort", "BuyToCover"];
  const validSizing: TradeCellSizingMethod[] = ["FixedShares", "FixedNotional", "PercentAUM", "EqualWeight"];
  if (
    !instrument || !validInstruments.includes(instrument) ||
    !direction || !validDirections.includes(direction) ||
    !sizingMethod || !validSizing.includes(sizingMethod)
  ) return null;
  return {
    instrument,
    direction,
    sizingMethod,
    priceConstraint: cell.parameters["priceConstraint"],
    sizingValue: cell.parameters["sizingValue"]
  };
}

export interface StrategyBuilderTransition {
  transitionId: string;
  fromCellId: string;
  toCellId: string;
  kind: "next" | "loop" | "branch";
  condition: string;
  maxIterations?: number | null;
  rationale?: string | null;
}

export interface StrategyBuilderDocument {
  documentId: string;
  name: string;
  description: string;
  version: string;
  datasetReference: string;
  universe: string[];
  cells: StrategyBuilderCell[];
  transitions: StrategyBuilderTransition[];
  updatedAt: string;
}

export interface StrategyBuilderTemplate {
  templateId: string;
  name: string;
  description: string;
  category: string;
  tags: string[];
  document: StrategyBuilderDocument;
  loadCommand: StrategyDesignerCommandViewModel;
}

export interface StrategyBuilderValidationMessage {
  code: string;
  severity: StrategyBuilderMessageSeverity;
  targetId: string;
  message: string;
}

export interface StrategyBuilderFieldChipViewModel {
  fieldId: string;
  label: string;
  isEnabled: boolean;
  disabledReason: string | null;
  statusLabel: string;
}

export interface StrategyBuilderCellViewModel extends StrategyBuilderCell {
  isSelected: boolean;
  status: "ready" | "warning" | "blocked";
  statusLabel: string;
  disabledReason: string | null;
  fieldChips: StrategyBuilderFieldChipViewModel[];
  selectButtonAriaLabel: string;
  detailPanelId: string;
}

export interface StrategyBuilderTransitionViewModel extends StrategyBuilderTransition {
  label: string;
  status: "ready" | "warning" | "blocked";
  statusLabel: string;
  detail: string;
}

export interface StrategyBuilderRunTraceEntryViewModel {
  stepId: string;
  label: string;
  status: "ready" | "warning" | "blocked" | "complete";
  detail: string;
  cellId: string | null;
  isSelectedCell: boolean;
}

export interface StrategyBuilderBacktestPanelViewModel {
  statusLabel: string;
  proofSummary: string;
  datasetFingerprint: string;
  runCommand: StrategyDesignerCommandViewModel;
  routeActions: Array<{ id: string; label: string; href: string; method: "GET" | "POST" }>;
}

export interface StrategyBuilderCellDetailViewModel {
  id: string;
  title: string;
  eyebrow: string;
  description: string;
  source: string;
  fields: StrategyLegDetailFieldViewModel[];
  ariaLabel: string;
}

export interface StrategyBuilderWorkbenchViewModel {
  document: StrategyBuilderDocument;
  metrics: DesignerSummaryMetric[];
  fieldCatalog: StrategyBuilderFieldCatalogItem[];
  fieldSearch: string;
  fieldSearchState: StrategyBuilderFieldCatalogSearchState;
  filteredFields: StrategyBuilderFieldCatalogItem[];
  formulaSuggestions: StrategyBuilderFieldCatalogItem[];
  cells: StrategyBuilderCellViewModel[];
  transitions: StrategyBuilderTransitionViewModel[];
  templates: StrategyBuilderTemplate[];
  selectedCellId: string | null;
  selectedCellDetail: StrategyBuilderCellDetailViewModel | null;
  trace: StrategyBuilderRunTraceEntryViewModel[];
  validationMessages: StrategyBuilderValidationMessage[];
  validationSummary: string;
  liveRegionMessage: string;
  backtest: StrategyBuilderBacktestPanelViewModel;
  setFieldSearch: (value: string) => void;
  selectCell: (cellId: string | null) => void;
  loadTemplate: (templateId: string) => void;
}

export interface StrategyCanvasLegViewModel extends StrategyLeg {
  isSelected: boolean;
  directionTone: "success" | "warning";
  isOption: boolean;
  detailPanelId: string;
  fieldIds: {
    direction: string;
    quantity: string;
    strike: string;
    premium: string;
  };
  containerAriaLabel: string;
  selectButtonLabel: string;
  selectButtonAriaLabel: string;
  selectButtonAriaControls: string;
  selectButtonAriaExpanded: boolean;
  removeButtonAriaLabel: string;
  directionAriaLabel: string;
  quantityAriaLabel: string;
  strikeFieldLabel: string;
  strikeAriaLabel: string;
  premiumAriaLabel: string;
  premiumUnavailableAriaLabel: string;
  moveUpCommand: StrategyDesignerCommandViewModel;
  moveDownCommand: StrategyDesignerCommandViewModel;
}

export interface StrategyDesignerViewModel {
  workbench: StrategyBuilderWorkbenchViewModel;
  legs: StrategyLeg[];
  canvasLegs: StrategyCanvasLegViewModel[];
  palette: StrategyLegPaletteEntry[];
  spotPrice: number;
  spotPriceField: StrategyDesignerSpotPriceFieldViewModel;
  updateSpotPriceDraft: (value: string) => void;
  commitSpotPriceDraft: (value?: string) => void;
  setSpotPrice: (price: number) => void;
  addLegFromPalette: (kind: StrategyLegKind) => string;
  removeLeg: (id: string) => void;
  updateLeg: (id: string, patch: Partial<Pick<StrategyLeg, "quantity" | "strike" | "premium" | "direction">>) => void;
  reorderLeg: (sourceId: string, targetId: string) => void;
  clearCanvas: () => void;
  loadSample: () => void;
  clearCanvasCommand: StrategyDesignerCommandViewModel;
  loadSampleCommand: StrategyDesignerCommandViewModel;
  addLongCallCommand: StrategyDesignerCommandViewModel;
  canvasTitle: string;
  payoff: PayoffChartViewModel;
  participation: ParticipationViewModel;
  metrics: DesignerSummaryMetric[];
  emptyStateMessage: string;
  selectedLegId: string | null;
  selectLeg: (id: string | null) => void;
  selectedLeg: StrategyLeg | null;
  selectedLegDetailPanelId: string;
  selectedLegDetail: StrategyLegDetailViewModel | null;
  selectedLegDetailEmptyState: StrategyLegDetailEmptyStateViewModel;
}

export const STRATEGY_DESIGNER_SELECTED_LEG_DETAIL_PANEL_ID = "strategy-designer-selected-leg-detail";
export const STRATEGY_BUILDER_SELECTED_CELL_DETAIL_PANEL_ID = "strategy-builder-selected-cell-detail";

const STRATEGY_BUILDER_ENDPOINTS = {
  templates: "/api/workstation/strategy/designer/templates",
  fieldCatalog: "/api/workstation/strategy/designer/field-catalog",
  drafts: "/api/workstation/strategy/designer/drafts",
  validate: "/api/workstation/strategy/designer/validate",
  preview: "/api/workstation/strategy/designer/preview",
  runBacktest: "/api/workstation/strategy/designer/run-backtest"
} as const;

const STRATEGY_BUILDER_FIELD_CATALOG: StrategyBuilderFieldCatalogItem[] = [
  {
    fieldId: "PRICE",
    label: "Price",
    source: "Provider historical bars / live quotes",
    dataSet: "market-data",
    typeName: "decimal",
    description: "Canonical last or close price resolved through Meridian providers.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["close", "last", "bar.close"]
  },
  {
    fieldId: "MOMENTUM_63D",
    label: "63-day momentum",
    source: "Provider historical bars",
    dataSet: "market-data",
    typeName: "decimal",
    description: "Return over the last 63 trading sessions.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["return", "trend"]
  },
  {
    fieldId: "VOLATILITY_20D",
    label: "20-day volatility",
    source: "Provider historical bars",
    dataSet: "market-data",
    typeName: "decimal",
    description: "Realized volatility over the last 20 trading sessions.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["risk", "stdev"]
  },
  {
    fieldId: "RATING",
    label: "Rating",
    source: "Security Master fixed-income reference",
    dataSet: "security-master",
    typeName: "string",
    description: "Normalized credit rating bucket.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["credit", "quality"]
  },
  {
    fieldId: "YIELD",
    label: "Yield",
    source: "Security Master fixed-income reference",
    dataSet: "security-master",
    typeName: "decimal",
    description: "Mapped yield input for carry screens.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["carry", "income"]
  },
  {
    fieldId: "SPREAD",
    label: "Spread",
    source: "Security Master fixed-income reference",
    dataSet: "security-master",
    typeName: "decimal",
    description: "Mapped spread input for credit screens.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["credit", "oas"]
  },
  {
    fieldId: "DURATION",
    label: "Duration",
    source: "Security Master fixed-income reference",
    dataSet: "security-master",
    typeName: "decimal",
    description: "Effective duration input.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["interest-rate", "risk"]
  },
  {
    fieldId: "OPTION_DELTA",
    label: "Option delta",
    source: "Options chain",
    dataSet: "options",
    typeName: "decimal",
    description: "Option-chain greeks mapped through Meridian option reference data.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["greeks", "options"]
  },
  {
    fieldId: "PORTFOLIO_WEIGHT",
    label: "Portfolio weight",
    source: "Portfolio/run data",
    dataSet: "portfolio",
    typeName: "decimal",
    description: "Weight from portfolio snapshots and strategy run data.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["allocation", "portfolio"]
  },
  {
    fieldId: "AMX_PRIVATE_SCORE",
    label: "AMX private score",
    source: "Unmapped AMX vocabulary",
    dataSet: "unmapped",
    typeName: "decimal",
    description: "Prototype-only field kept visible for traceability.",
    isEnabled: false,
    disabledReason: "No Meridian canonical source is wired for AMX_PRIVATE_SCORE in v1.",
    synonyms: ["amx", "private"]
  }
];

const DEFAULT_STRATEGY_BUILDER_DOCUMENT: StrategyBuilderDocument = {
  documentId: "equity-momentum-breakout",
  name: "Equity momentum breakout",
  description: "Universe, momentum formula, governance guard, and proof run for liquid equities.",
  version: "1",
  datasetReference: "provider-bars/equities/daily",
  universe: ["SPY", "QQQ", "AAPL", "MSFT"],
  cells: [
    {
      cellId: "liquid-universe",
      label: "Liquid equity universe",
      kind: "visual",
      purpose: "universe",
      source: "PRICE > 20 && volumeAvg20d > 1000000",
      fieldRefs: ["PRICE"]
    },
    {
      cellId: "momentum-score",
      label: "Momentum score",
      kind: "formula",
      purpose: "rank",
      source: "MOMENTUM_63D - VOLATILITY_20D",
      fieldRefs: ["MOMENTUM_63D", "VOLATILITY_20D"]
    },
    {
      cellId: "risk-guard",
      label: "Risk guard",
      kind: "governance",
      purpose: "risk",
      source: "VOLATILITY_20D < 0.30",
      fieldRefs: ["VOLATILITY_20D"]
    },
    {
      cellId: "proof-run",
      label: "Backtest proof",
      kind: "code",
      purpose: "backtest",
      source: "rebalance weekly, record run trace, attach dataset fingerprint",
      fieldRefs: ["PRICE", "PORTFOLIO_WEIGHT"]
    }
  ],
  transitions: [
    { transitionId: "t1", fromCellId: "liquid-universe", toCellId: "momentum-score", kind: "next", condition: "universe ready" },
    { transitionId: "t2", fromCellId: "momentum-score", toCellId: "risk-guard", kind: "next", condition: "rank complete" },
    { transitionId: "t3", fromCellId: "risk-guard", toCellId: "proof-run", kind: "next", condition: "risk accepted" }
  ],
  updatedAt: "2026-05-15T00:00:00Z"
};

const STRATEGY_BUILDER_TEMPLATES: StrategyBuilderTemplate[] = [
  {
    templateId: "equity-momentum-breakout",
    name: "Equity momentum breakout",
    description: "Formula autocomplete, transition map, governance cell, and proof run.",
    category: "Equities",
    tags: ["formula", "backtest", "governance"],
    document: DEFAULT_STRATEGY_BUILDER_DOCUMENT,
    loadCommand: {
      label: "Load",
      ariaLabel: "Load Equity momentum breakout template",
      disabled: false,
      disabledReason: null
    }
  },
  {
    templateId: "investment-grade-income",
    name: "Investment-grade income screen",
    description: "AMX-style fixed-income fields mapped into Security Master and provider bars.",
    category: "Fixed income",
    tags: ["field catalog", "fixed income", "rank"],
    document: {
      documentId: "investment-grade-income",
      name: "Investment-grade income screen",
      description: "Fixed-income carry screen using canonical Meridian field mappings.",
      version: "1",
      datasetReference: "security-master/fixed-income + provider-bars/daily",
      universe: ["AGG", "LQD", "VCIT"],
      cells: [
        { cellId: "ig-universe", label: "Investment-grade universe", kind: "visual", purpose: "universe", source: "RATING >= BBB", fieldRefs: ["RATING"] },
        { cellId: "carry-rank", label: "Carry rank", kind: "formula", purpose: "rank", source: "(YIELD - SPREAD) / max(DURATION, 1)", fieldRefs: ["YIELD", "SPREAD", "DURATION"] },
        { cellId: "income-risk", label: "Duration guard", kind: "governance", purpose: "risk", source: "DURATION <= 7", fieldRefs: ["DURATION"] },
        { cellId: "income-proof", label: "Backtest proof", kind: "code", purpose: "backtest", source: "rebalance monthly by carry-rank", fieldRefs: ["PRICE"] }
      ],
      transitions: [
        { transitionId: "t1", fromCellId: "ig-universe", toCellId: "carry-rank", kind: "next", condition: "universe ready" },
        { transitionId: "t2", fromCellId: "carry-rank", toCellId: "income-risk", kind: "next", condition: "rank complete" },
        { transitionId: "t3", fromCellId: "income-risk", toCellId: "income-proof", kind: "next", condition: "risk accepted" }
      ],
      updatedAt: "2026-05-15T00:00:00Z"
    },
    loadCommand: {
      label: "Load",
      ariaLabel: "Load Investment-grade income screen template",
      disabled: false,
      disabledReason: null
    }
  },
  {
    templateId: "options-payoff",
    name: "Options payoff template",
    description: "Preserves the existing option-leg payoff model inside the Strategy Builder.",
    category: "Options",
    tags: ["options", "payoff", "template"],
    document: {
      documentId: "options-payoff",
      name: "Options payoff template",
      description: "Existing option payoff panel carried into the broader Strategy Builder.",
      version: "1",
      datasetReference: "options-chain + provider-quotes",
      universe: ["SPY"],
      cells: [
        { cellId: "option-context", label: "Option chain context", kind: "visual", purpose: "universe", source: "underlying == SPY && expiry <= 45d", fieldRefs: ["PRICE", "OPTION_DELTA"] },
        { cellId: "payoff-panel", label: "Options payoff panel", kind: "options-payoff", purpose: "options-payoff", source: "bull call spread payoff sample", fieldRefs: ["OPTION_DELTA", "PRICE"] },
        { cellId: "review-proof", label: "Review proof", kind: "governance", purpose: "governance", source: "attach payoff, run trace, and no-live-trading acknowledgement", fieldRefs: ["PORTFOLIO_WEIGHT"] }
      ],
      transitions: [
        { transitionId: "t1", fromCellId: "option-context", toCellId: "payoff-panel", kind: "next", condition: "option context ready" },
        { transitionId: "t2", fromCellId: "payoff-panel", toCellId: "review-proof", kind: "next", condition: "payoff reviewed" }
      ],
      updatedAt: "2026-05-15T00:00:00Z"
    },
    loadCommand: {
      label: "Load",
      ariaLabel: "Load Options payoff template",
      disabled: false,
      disabledReason: null
    }
  },
  {
    templateId: "state-machine-strategy",
    name: "State machine strategy",
    description: "Named execution states with entry/exit conditions — model a strategy as a lifecycle state machine.",
    category: "Advanced",
    tags: ["state", "lifecycle", "conditional"],
    document: {
      documentId: "state-machine-strategy",
      name: "State machine strategy",
      description: "Strategy execution modelled as a state machine with named checkpoints.",
      version: "1",
      datasetReference: "provider-bars/equities/daily",
      universe: ["SPY", "QQQ"],
      cells: [
        {
          cellId: "watching-state",
          label: "Watching",
          kind: "state",
          purpose: "state",
          source: "initial monitoring phase",
          fieldRefs: ["MOMENTUM_63D"],
          parameters: { stateLabel: "Watching", exitCondition: "MOMENTUM_63D > 0.05", entryCondition: "PORTFOLIO_WEIGHT == 0", isTerminal: "false" }
        },
        {
          cellId: "holding-state",
          label: "Holding",
          kind: "state",
          purpose: "state",
          source: "active position phase",
          fieldRefs: ["MOMENTUM_63D", "VOLATILITY_20D"],
          parameters: { stateLabel: "Holding", exitCondition: "MOMENTUM_63D < 0 || VOLATILITY_20D > 0.35", isTerminal: "false" }
        },
        {
          cellId: "exiting-state",
          label: "Exiting",
          kind: "state",
          purpose: "state",
          source: "position wind-down phase",
          fieldRefs: ["PORTFOLIO_WEIGHT"],
          parameters: { stateLabel: "Exiting", exitCondition: "PORTFOLIO_WEIGHT == 0", isTerminal: "true" }
        },
        { cellId: "state-risk-guard", label: "State risk guard", kind: "governance", purpose: "risk", source: "VOLATILITY_20D < 0.40", fieldRefs: ["VOLATILITY_20D"] }
      ],
      transitions: [
        { transitionId: "t1", fromCellId: "watching-state", toCellId: "holding-state", kind: "next", condition: "exit: MOMENTUM_63D > 0.05" },
        { transitionId: "t2", fromCellId: "holding-state", toCellId: "exiting-state", kind: "next", condition: "exit: MOMENTUM_63D < 0 || VOLATILITY_20D > 0.35" },
        { transitionId: "t3", fromCellId: "exiting-state", toCellId: "state-risk-guard", kind: "next", condition: "position closed" }
      ],
      updatedAt: "2026-05-15T00:00:00Z"
    },
    loadCommand: {
      label: "Load",
      ariaLabel: "Load State machine strategy template",
      disabled: false,
      disabledReason: null
    }
  },
  {
    templateId: "concurrent-branch-strategy",
    name: "Concurrent branch strategy",
    description: "Parallel evaluation point — multiple signal branches run simultaneously with configurable pass semantics.",
    category: "Advanced",
    tags: ["concurrent", "parallel", "multi-signal"],
    document: {
      documentId: "concurrent-branch-strategy",
      name: "Concurrent branch strategy",
      description: "Parallel signal evaluation: equity momentum and volatility screens run concurrently.",
      version: "1",
      datasetReference: "provider-bars/equities/daily",
      universe: ["SPY", "QQQ", "AAPL"],
      cells: [
        { cellId: "momentum-branch", label: "Momentum signal", kind: "formula", purpose: "filter", source: "MOMENTUM_63D > 0.05", fieldRefs: ["MOMENTUM_63D"] },
        { cellId: "volatility-branch", label: "Volatility filter", kind: "formula", purpose: "filter", source: "VOLATILITY_20D < 0.30", fieldRefs: ["VOLATILITY_20D"] },
        {
          cellId: "concurrent-gate",
          label: "Concurrent signal gate",
          kind: "concurrent",
          purpose: "concurrent",
          source: "parallel branch evaluation",
          fieldRefs: [],
          parameters: { branchIds: "momentum-branch,volatility-branch", semantics: "all-pass" }
        },
        { cellId: "concurrent-risk", label: "Risk guard", kind: "governance", purpose: "risk", source: "all concurrent branches satisfied", fieldRefs: ["PORTFOLIO_WEIGHT"] }
      ],
      transitions: [
        { transitionId: "t1", fromCellId: "momentum-branch", toCellId: "concurrent-gate", kind: "next", condition: "branch ready" },
        { transitionId: "t2", fromCellId: "volatility-branch", toCellId: "concurrent-gate", kind: "next", condition: "branch ready" },
        { transitionId: "t3", fromCellId: "concurrent-gate", toCellId: "concurrent-risk", kind: "next", condition: "gate resolved" }
      ],
      updatedAt: "2026-05-15T00:00:00Z"
    },
    loadCommand: {
      label: "Load",
      ariaLabel: "Load Concurrent branch strategy template",
      disabled: false,
      disabledReason: null
    }
  },
  {
    templateId: "structured-universe-strategy",
    name: "Structured universe strategy",
    description: "Explicit asset class, include/exclude rules, and size constraints for disciplined universe construction.",
    category: "Universe",
    tags: ["universe-builder", "structured", "equity"],
    document: {
      documentId: "structured-universe-strategy",
      name: "Structured universe strategy",
      description: "Universe-builder cell with explicit asset class rules and size bounds.",
      version: "1",
      datasetReference: "provider-bars/equities/daily",
      universe: ["SPY"],
      cells: [
        {
          cellId: "equity-universe",
          label: "Liquid equity universe",
          kind: "universe-builder",
          purpose: "universe",
          source: "structured universe definition",
          fieldRefs: ["PRICE", "MOMENTUM_63D"],
          parameters: { assetClass: "Equity", includeRules: "PRICE > 10, MOMENTUM_63D > 0", excludeRules: "VOLATILITY_20D > 0.40", minSize: "20", maxSize: "100" }
        },
        { cellId: "universe-rank", label: "Momentum rank", kind: "formula", purpose: "rank", source: "MOMENTUM_63D - VOLATILITY_20D", fieldRefs: ["MOMENTUM_63D", "VOLATILITY_20D"] },
        { cellId: "universe-risk", label: "Universe risk guard", kind: "governance", purpose: "risk", source: "VOLATILITY_20D < 0.30", fieldRefs: ["VOLATILITY_20D"] }
      ],
      transitions: [
        { transitionId: "t1", fromCellId: "equity-universe", toCellId: "universe-rank", kind: "next", condition: "universe built" },
        { transitionId: "t2", fromCellId: "universe-rank", toCellId: "universe-risk", kind: "next", condition: "rank complete" }
      ],
      updatedAt: "2026-05-15T00:00:00Z"
    },
    loadCommand: {
      label: "Load",
      ariaLabel: "Load Structured universe strategy template",
      disabled: false,
      disabledReason: null
    }
  },
  {
    templateId: "trade-intent-strategy",
    name: "Trade intent strategy",
    description: "Explicit trade definition cell — instrument type, direction, sizing method, and price constraint.",
    category: "Execution",
    tags: ["trade", "execution", "sizing"],
    document: {
      documentId: "trade-intent-strategy",
      name: "Trade intent strategy",
      description: "Strategy with an explicit trade cell defining instrument, direction, and sizing.",
      version: "1",
      datasetReference: "provider-bars/equities/daily",
      universe: ["SPY", "QQQ"],
      cells: [
        { cellId: "trade-universe", label: "Liquid equity filter", kind: "visual", purpose: "universe", source: "PRICE > 20", fieldRefs: ["PRICE"] },
        { cellId: "trade-rank", label: "Momentum score", kind: "formula", purpose: "rank", source: "MOMENTUM_63D - VOLATILITY_20D", fieldRefs: ["MOMENTUM_63D", "VOLATILITY_20D"] },
        {
          cellId: "buy-equities",
          label: "Buy equities",
          kind: "trade",
          purpose: "trade",
          source: "execute buy order on ranked universe",
          fieldRefs: [],
          parameters: { instrument: "Equity", direction: "Buy", sizingMethod: "EqualWeight", priceConstraint: "VWAP", sizingValue: "0.05" }
        },
        { cellId: "trade-risk", label: "Execution risk guard", kind: "governance", purpose: "risk", source: "VOLATILITY_20D < 0.30 && PORTFOLIO_WEIGHT <= 0.10", fieldRefs: ["VOLATILITY_20D", "PORTFOLIO_WEIGHT"] }
      ],
      transitions: [
        { transitionId: "t1", fromCellId: "trade-universe", toCellId: "trade-rank", kind: "next", condition: "universe ready" },
        { transitionId: "t2", fromCellId: "trade-rank", toCellId: "buy-equities", kind: "next", condition: "rank complete" },
        { transitionId: "t3", fromCellId: "buy-equities", toCellId: "trade-risk", kind: "next", condition: "trade submitted" }
      ],
      updatedAt: "2026-05-15T00:00:00Z"
    },
    loadCommand: {
      label: "Load",
      ariaLabel: "Load Trade intent strategy template",
      disabled: false,
      disabledReason: null
    }
  }
];

const PALETTE: StrategyLegPaletteEntry[] = [
  {
    kind: "long-call",
    label: "Long Call",
    description: "Buy a call. Profits as price rises above strike + premium.",
    badge: "Bullish",
    defaults: {
      kind: "long-call",
      direction: "Long",
      instrument: "Call",
      quantity: 1,
      strike: 100,
      premium: 4
    }
  },
  {
    kind: "short-call",
    label: "Short Call",
    description: "Sell a call. Collects premium; loses if price climbs above strike.",
    badge: "Bearish",
    defaults: {
      kind: "short-call",
      direction: "Short",
      instrument: "Call",
      quantity: 1,
      strike: 110,
      premium: 2
    }
  },
  {
    kind: "long-put",
    label: "Long Put",
    description: "Buy a put. Profits as price falls below strike - premium.",
    badge: "Bearish",
    defaults: {
      kind: "long-put",
      direction: "Long",
      instrument: "Put",
      quantity: 1,
      strike: 100,
      premium: 4
    }
  },
  {
    kind: "short-put",
    label: "Short Put",
    description: "Sell a put. Collects premium; loses if price drops below strike.",
    badge: "Bullish",
    defaults: {
      kind: "short-put",
      direction: "Short",
      instrument: "Put",
      quantity: 1,
      strike: 90,
      premium: 2
    }
  },
  {
    kind: "long-stock",
    label: "Long Stock",
    description: "Buy underlying shares for linear exposure.",
    badge: "Bullish",
    defaults: {
      kind: "long-stock",
      direction: "Long",
      instrument: "Stock",
      quantity: 100,
      strike: 100,
      premium: 0
    }
  },
  {
    kind: "short-stock",
    label: "Short Stock",
    description: "Short underlying shares for inverse linear exposure.",
    badge: "Bearish",
    defaults: {
      kind: "short-stock",
      direction: "Short",
      instrument: "Stock",
      quantity: 100,
      strike: 100,
      premium: 0
    }
  }
];

const SAMPLE_LEGS: StrategyLeg[] = [
  {
    id: "sample-long-call",
    kind: "long-call",
    label: "Long Call · 100",
    direction: "Long",
    instrument: "Call",
    quantity: 1,
    strike: 100,
    premium: 4
  },
  {
    id: "sample-short-call",
    kind: "short-call",
    label: "Short Call · 110",
    direction: "Short",
    instrument: "Call",
    quantity: 1,
    strike: 110,
    premium: 2
  }
];

const PAYOFF_WIDTH = 640;
const PAYOFF_HEIGHT = 220;
const PAYOFF_PADDING_LEFT = 56;
const PAYOFF_PADDING_RIGHT = 16;
const PAYOFF_PADDING_TOP = 12;
const PAYOFF_PADDING_BOTTOM = 28;
const PAYOFF_SAMPLE_COUNT = 121;

export function buildEmptyStrategyDesignerSnapshot(): {
  legs: StrategyLeg[];
  palette: StrategyLegPaletteEntry[];
} {
  return { legs: [], palette: PALETTE };
}

export function getStrategyDesignerPalette(): StrategyLegPaletteEntry[] {
  return PALETTE;
}

export function getStrategyDesignerSampleLegs(): StrategyLeg[] {
  return SAMPLE_LEGS.map((leg) => ({ ...leg }));
}

export function getStrategyBuilderFieldCatalog(): StrategyBuilderFieldCatalogItem[] {
  return STRATEGY_BUILDER_FIELD_CATALOG.map((field) => ({
    ...field,
    synonyms: [...field.synonyms]
  }));
}

export function getStrategyBuilderTemplates(): StrategyBuilderTemplate[] {
  return STRATEGY_BUILDER_TEMPLATES.map((template) => ({
    ...template,
    tags: [...template.tags],
    document: cloneStrategyBuilderDocument(template.document),
    loadCommand: { ...template.loadCommand }
  }));
}

export function loadStrategyBuilderTemplate(templateId: string): StrategyBuilderDocument {
  const template = STRATEGY_BUILDER_TEMPLATES.find((item) => item.templateId === templateId) ?? STRATEGY_BUILDER_TEMPLATES[0];
  return cloneStrategyBuilderDocument(template.document);
}

export function filterStrategyBuilderFields(
  query: string,
  fields: StrategyBuilderFieldCatalogItem[] = STRATEGY_BUILDER_FIELD_CATALOG
): StrategyBuilderFieldCatalogItem[] {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return fields;
  }

  return fields.filter((field) =>
    [
      field.fieldId,
      field.label,
      field.source,
      field.dataSet,
      field.description,
      ...field.synonyms
    ].some((value) => value.toLowerCase().includes(normalized))
  );
}

export function buildFormulaSuggestions(
  query: string,
  fields: StrategyBuilderFieldCatalogItem[] = STRATEGY_BUILDER_FIELD_CATALOG
): StrategyBuilderFieldCatalogItem[] {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return fields.filter((field) => field.isEnabled).slice(0, 6);
  }

  return fields
    .filter((field) => field.isEnabled)
    .filter((field) =>
      field.fieldId.toLowerCase().startsWith(normalized) ||
      field.label.toLowerCase().includes(normalized) ||
      field.synonyms.some((synonym) => synonym.toLowerCase().includes(normalized))
    )
    .slice(0, 6);
}

export function validateStrategyBuilderDocument(
  document: StrategyBuilderDocument,
  fieldCatalog: StrategyBuilderFieldCatalogItem[] = STRATEGY_BUILDER_FIELD_CATALOG
): StrategyBuilderValidationMessage[] {
  const messages: StrategyBuilderValidationMessage[] = [];
  const fieldMap = new Map(fieldCatalog.map((field) => [field.fieldId, field]));

  if (!document.datasetReference.trim()) {
    messages.push({
      code: "DatasetRequired",
      severity: "error",
      targetId: document.documentId,
      message: "A dataset reference is required before preview or backtest."
    });
  }

  if (document.universe.length === 0) {
    messages.push({
      code: "UniverseRequired",
      severity: "error",
      targetId: document.documentId,
      message: "At least one symbol or universe selector is required."
    });
  }

  if (document.cells.length === 0) {
    messages.push({
      code: "CellsRequired",
      severity: "error",
      targetId: document.documentId,
      message: "At least one strategy cell is required."
    });
  }

  for (const cell of document.cells) {
    if (!cell.source.trim()) {
      messages.push({
        code: "CellSourceRequired",
        severity: "error",
        targetId: cell.cellId,
        message: `${cell.label} needs a formula, visual rule, or code source.`
      });
    }

    messages.push(...validateCellKindParameters(cell));

    for (const ref of cell.fieldRefs) {
      const field = fieldMap.get(ref);
      if (!field) {
        messages.push({
          code: "UnknownField",
          severity: "error",
          targetId: cell.cellId,
          message: `${cell.label} references unknown field ${ref}.`
        });
        continue;
      }

      if (!field.isEnabled) {
        messages.push({
          code: "DisabledField",
          severity: "error",
          targetId: cell.cellId,
          message: `${ref} is visible but disabled: ${field.disabledReason}`
        });
      }
    }
  }

  const cellOrder = new Map(document.cells.map((cell, index) => [cell.cellId, index]));
  for (const transition of document.transitions) {
    const fromIndex = cellOrder.get(transition.fromCellId);
    const toIndex = cellOrder.get(transition.toCellId);
    if (fromIndex === undefined || toIndex === undefined) {
      messages.push({
        code: "TransitionCellMissing",
        severity: "error",
        targetId: transition.transitionId,
        message: `${transition.transitionId} points to a missing cell.`
      });
      continue;
    }

    const isLoop = transition.kind === "loop" || fromIndex >= toIndex;
    if (isLoop && (!transition.maxIterations || transition.maxIterations < 1 || transition.maxIterations > 1000)) {
      messages.push({
        code: "LoopGuardRequired",
        severity: "error",
        targetId: transition.transitionId,
        message: `${transition.transitionId} needs a 1-1000 iteration loop guard.`
      });
    }

    if (isLoop && !transition.rationale?.trim()) {
      messages.push({
        code: "LoopRationaleRequired",
        severity: "error",
        targetId: transition.transitionId,
        message: `${transition.transitionId} needs a bounded-loop rationale.`
      });
    }
  }

  if (!document.cells.some((cell) => cell.purpose === "risk" || cell.kind === "governance")) {
    messages.push({
      code: "RiskGuardRecommended",
      severity: "warning",
      targetId: document.documentId,
      message: "Add a risk or governance cell before promotion review."
    });
  }

  return messages;
}

function validateCellKindParameters(cell: StrategyBuilderCell): StrategyBuilderValidationMessage[] {
  const messages: StrategyBuilderValidationMessage[] = [];
  const label = cell.label || cell.cellId;

  if (cell.kind === "state") {
    if (!cell.parameters?.["exitCondition"]) {
      messages.push({ code: "StateCellExitRequired", severity: "error", targetId: cell.cellId, message: `${label} (state) must define an exitCondition parameter.` });
    }
    if (!cell.parameters?.["stateLabel"]) {
      messages.push({ code: "StateCellLabelRequired", severity: "error", targetId: cell.cellId, message: `${label} (state) must define a stateLabel parameter.` });
    }
  }

  if (cell.kind === "concurrent") {
    if (!cell.parameters?.["branchIds"]) {
      messages.push({ code: "ConcurrentCellBranchesRequired", severity: "error", targetId: cell.cellId, message: `${label} (concurrent) must define a branchIds parameter listing the parallel cell IDs.` });
    }
    const validSemantics: ConcurrentCellSemantics[] = ["all-pass", "any-pass", "first-wins"];
    const sem = cell.parameters?.["semantics"];
    if (!sem || !validSemantics.includes(sem as ConcurrentCellSemantics)) {
      messages.push({ code: "ConcurrentCellSemanticsRequired", severity: "error", targetId: cell.cellId, message: `${label} (concurrent) semantics must be one of: all-pass, any-pass, first-wins.` });
    }
  }

  if (cell.kind === "universe-builder") {
    if (!cell.parameters?.["assetClass"]) {
      messages.push({ code: "UniverseBuilderAssetClassRequired", severity: "error", targetId: cell.cellId, message: `${label} (universe-builder) must define an assetClass parameter.` });
    }
    if (!cell.parameters?.["includeRules"]) {
      messages.push({ code: "UniverseBuilderIncludeRulesRequired", severity: "error", targetId: cell.cellId, message: `${label} (universe-builder) must define at least one includeRules parameter.` });
    }
  }

  if (cell.kind === "trade") {
    const validInstruments: TradeCellInstrumentType[] = ["Equity", "Option", "Future", "ETF"];
    const validDirections: TradeCellDirection[] = ["Buy", "Sell", "SellShort", "BuyToCover"];
    const validSizing: TradeCellSizingMethod[] = ["FixedShares", "FixedNotional", "PercentAUM", "EqualWeight"];
    const instr = cell.parameters?.["instrument"] as TradeCellInstrumentType | undefined;
    const dir = cell.parameters?.["direction"] as TradeCellDirection | undefined;
    const sizing = cell.parameters?.["sizingMethod"] as TradeCellSizingMethod | undefined;
    if (!instr || !validInstruments.includes(instr)) {
      messages.push({ code: "TradeCellInstrumentRequired", severity: "error", targetId: cell.cellId, message: `${label} (trade) instrument must be Equity, Option, Future, or ETF.` });
    }
    if (!dir || !validDirections.includes(dir)) {
      messages.push({ code: "TradeCellDirectionRequired", severity: "error", targetId: cell.cellId, message: `${label} (trade) direction must be Buy, Sell, SellShort, or BuyToCover.` });
    }
    if (!sizing || !validSizing.includes(sizing)) {
      messages.push({ code: "TradeCellSizingRequired", severity: "error", targetId: cell.cellId, message: `${label} (trade) sizingMethod must be FixedShares, FixedNotional, PercentAUM, or EqualWeight.` });
    }
  }

  return messages;
}

export function buildStrategyBuilderWorkbenchViewModel({
  document = DEFAULT_STRATEGY_BUILDER_DOCUMENT,
  selectedCellId = document.cells[0]?.cellId ?? null,
  fieldSearch = "",
  setFieldSearch = () => undefined,
  selectCell = () => undefined,
  loadTemplate = () => undefined
}: {
  document?: StrategyBuilderDocument;
  selectedCellId?: string | null;
  fieldSearch?: string;
  setFieldSearch?: (value: string) => void;
  selectCell?: (cellId: string | null) => void;
  loadTemplate?: (templateId: string) => void;
} = {}): StrategyBuilderWorkbenchViewModel {
  const catalog = getStrategyBuilderFieldCatalog();
  const fieldMap = new Map(catalog.map((field) => [field.fieldId, field]));
  const validationMessages = validateStrategyBuilderDocument(document, catalog);
  const errorCount = validationMessages.filter((message) => message.severity === "error").length;
  const selectedCell = document.cells.find((cell) => cell.cellId === selectedCellId) ?? document.cells[0] ?? null;
  const selectedId = selectedCell?.cellId ?? null;
  const cells = buildStrategyBuilderCellViewModels(document.cells, selectedId, fieldMap, validationMessages);
  const trace = buildStrategyBuilderTrace(document, selectedId, validationMessages);
  const datasetFingerprint = buildStrategyBuilderDatasetFingerprint(document);
  const filteredFields = filterStrategyBuilderFields(fieldSearch, catalog);
  const validationSummary = errorCount > 0
    ? `${errorCount} blocking issue${errorCount === 1 ? "" : "s"}`
    : validationMessages.length > 0
      ? `${validationMessages.length} advisory issue${validationMessages.length === 1 ? "" : "s"}`
      : "Ready for preview and backtest";

  return {
    document,
    metrics: [
      {
        id: "builder-cells",
        label: "Cells",
        value: document.cells.length.toString(),
        detail: `${document.transitions.length} transition${document.transitions.length === 1 ? "" : "s"}`,
        tone: "default"
      },
      {
        id: "builder-fields",
        label: "Mapped fields",
        value: new Set(document.cells.flatMap((cell) => cell.fieldRefs)).size.toString(),
        detail: "Canonical Meridian sources",
        tone: "success"
      },
      {
        id: "builder-validation",
        label: "Validation",
        value: errorCount > 0 ? "Blocked" : "Ready",
        detail: validationSummary,
        tone: errorCount > 0 ? "danger" : validationMessages.length > 0 ? "warning" : "success"
      },
      {
        id: "builder-proof",
        label: "Proof lane",
        value: "Backtest",
        detail: "No live trading from builder v1",
        tone: "default"
      }
    ],
    fieldCatalog: catalog,
    fieldSearch,
    fieldSearchState: buildFieldCatalogSearchState(fieldSearch, filteredFields.length, catalog.length),
    filteredFields,
    formulaSuggestions: buildFormulaSuggestions(fieldSearch, catalog),
    cells,
    transitions: buildStrategyBuilderTransitionViewModels(document, validationMessages),
    templates: getStrategyBuilderTemplates(),
    selectedCellId: selectedId,
    selectedCellDetail: selectedCell ? buildStrategyBuilderCellDetail(selectedCell, fieldMap) : null,
    trace,
    validationMessages,
    validationSummary,
    liveRegionMessage: `${document.name}. ${validationSummary}. ${selectedCell ? `${selectedCell.label} selected.` : "No cell selected."}`,
    backtest: {
      statusLabel: errorCount > 0 ? "Blocked" : "Ready",
      proofSummary: errorCount > 0
        ? "Fix designer validation before QuantScript preview or backtest execution."
        : "Generated QuantScript will run through the existing Quant Lab and strategy run ledger.",
      datasetFingerprint,
      runCommand: {
        label: "Run backtest proof",
        ariaLabel: errorCount > 0 ? `Run backtest proof blocked: ${validationSummary}` : "Run backtest proof through Quant Lab",
        disabled: errorCount > 0,
        disabledReason: errorCount > 0 ? validationSummary : null
      },
      routeActions: [
        { id: "templates", label: "Templates", href: STRATEGY_BUILDER_ENDPOINTS.templates, method: "GET" },
        { id: "field-catalog", label: "Field catalog", href: STRATEGY_BUILDER_ENDPOINTS.fieldCatalog, method: "GET" },
        { id: "validate", label: "Validate", href: STRATEGY_BUILDER_ENDPOINTS.validate, method: "POST" },
        { id: "preview", label: "Preview", href: STRATEGY_BUILDER_ENDPOINTS.preview, method: "POST" },
        { id: "run-backtest", label: "Run backtest", href: STRATEGY_BUILDER_ENDPOINTS.runBacktest, method: "POST" }
      ]
    },
    setFieldSearch,
    selectCell,
    loadTemplate
  };
}

export function buildFieldCatalogSearchState(
  fieldSearch: string,
  visibleCount: number,
  totalCount: number
): StrategyBuilderFieldCatalogSearchState {
  const normalized = fieldSearch.trim();
  const resultWord = visibleCount === 1 ? "field" : "fields";
  const resultCountText = normalized.length > 0
    ? `${visibleCount} ${resultWord} match "${normalized}" out of ${totalCount}.`
    : `${visibleCount} ${resultWord} available in the Strategy Builder catalog.`;

  return {
    inputId: "strategy-builder-field-catalog-search",
    label: "Search field catalog",
    ariaLabel: "Search Strategy Builder field catalog",
    placeholder: "Search fields",
    helperId: "strategy-builder-field-catalog-search-help",
    helperText: "Filter by field ID, label, source, dataset, type, or synonym.",
    describedBy: "strategy-builder-field-catalog-search-help strategy-builder-field-catalog-search-status",
    resultsLabel: "Strategy Builder field catalog results",
    resultCountText,
    emptyState: visibleCount === 0
      ? {
        title: "No matching fields",
        description: normalized.length > 0
          ? `No field catalog entries match "${normalized}". Clear the filter to inspect mapped and disabled fields.`
          : "No field catalog entries are available for this strategy document.",
        ariaLabel: "Strategy Builder field catalog empty state"
      }
      : null
  };
}

export function computeLegPayoff(leg: StrategyLeg, spotAtExpiry: number): number {
  const directionSign = leg.direction === "Long" ? 1 : -1;
  if (leg.instrument === "Stock") {
    return (spotAtExpiry - leg.strike) * leg.quantity * directionSign;
  }
  if (leg.instrument === "Call") {
    const intrinsic = Math.max(0, spotAtExpiry - leg.strike);
    return (intrinsic - leg.premium) * leg.quantity * directionSign;
  }
  const intrinsic = Math.max(0, leg.strike - spotAtExpiry);
  return (intrinsic - leg.premium) * leg.quantity * directionSign;
}

export function computePortfolioPayoff(legs: StrategyLeg[], spotAtExpiry: number): number {
  return legs.reduce((acc, leg) => acc + computeLegPayoff(leg, spotAtExpiry), 0);
}

export function buildPayoffSeries(legs: StrategyLeg[], spotPrice: number): PayoffPoint[] {
  if (legs.length === 0) return [];
  const anchorStrikes = legs.map((leg) => leg.strike).filter((value) => Number.isFinite(value));
  const referencePrice = anchorStrikes.length
    ? anchorStrikes.reduce((a, b) => a + b, 0) / anchorStrikes.length
    : spotPrice;
  const minStrike = Math.min(...anchorStrikes, spotPrice);
  const maxStrike = Math.max(...anchorStrikes, spotPrice);
  const range = Math.max(20, (maxStrike - minStrike) * 1.4, referencePrice * 0.4);
  const minPrice = Math.max(0, referencePrice - range);
  const maxPrice = referencePrice + range;
  const step = (maxPrice - minPrice) / (PAYOFF_SAMPLE_COUNT - 1);
  const points: PayoffPoint[] = [];
  for (let i = 0; i < PAYOFF_SAMPLE_COUNT; i += 1) {
    const price = minPrice + step * i;
    points.push({ price, pnl: computePortfolioPayoff(legs, price) });
  }
  return points;
}

export function findBreakEvenPrices(points: PayoffPoint[]): number[] {
  const crossings: number[] = [];
  for (let i = 1; i < points.length; i += 1) {
    const prev = points[i - 1];
    const next = points[i];
    if (prev.pnl === 0) {
      crossings.push(prev.price);
      continue;
    }
    if ((prev.pnl < 0 && next.pnl > 0) || (prev.pnl > 0 && next.pnl < 0)) {
      const denom = next.pnl - prev.pnl;
      const t = denom === 0 ? 0 : -prev.pnl / denom;
      crossings.push(prev.price + (next.price - prev.price) * t);
    }
  }
  return crossings;
}

export function buildPayoffChartViewModel(legs: StrategyLeg[], spotPrice: number): PayoffChartViewModel {
  const points = buildPayoffSeries(legs, spotPrice);
  if (points.length === 0) {
    return {
      width: PAYOFF_WIDTH,
      height: PAYOFF_HEIGHT,
      paddingLeft: PAYOFF_PADDING_LEFT,
      paddingRight: PAYOFF_PADDING_RIGHT,
      paddingTop: PAYOFF_PADDING_TOP,
      paddingBottom: PAYOFF_PADDING_BOTTOM,
      points: [],
      zeroLineY: null,
      spotLineX: null,
      spotPrice,
      axisLabels: {
        minPrice: "—",
        maxPrice: "—",
        minPnl: "—",
        maxPnl: "—"
      },
      breakEvenPrices: [],
      maxProfit: 0,
      maxLoss: 0,
      netDebit: 0,
      caption: "Add a leg to render the payoff curve.",
      ariaLabel: "Empty payoff chart",
      isEmpty: true
    };
  }
  const prices = points.map((p) => p.price);
  const pnls = points.map((p) => p.pnl);
  const minPrice = Math.min(...prices);
  const maxPrice = Math.max(...prices);
  const minPnl = Math.min(...pnls);
  const maxPnl = Math.max(...pnls);
  const pnlRange = Math.max(1, maxPnl - minPnl);
  const priceRange = Math.max(0.001, maxPrice - minPrice);
  const plotWidth = PAYOFF_WIDTH - PAYOFF_PADDING_LEFT - PAYOFF_PADDING_RIGHT;
  const plotHeight = PAYOFF_HEIGHT - PAYOFF_PADDING_TOP - PAYOFF_PADDING_BOTTOM;

  const polyline: PayoffPolylinePoint[] = points.map((point) => ({
    ...point,
    x: PAYOFF_PADDING_LEFT + ((point.price - minPrice) / priceRange) * plotWidth,
    y:
      PAYOFF_PADDING_TOP +
      plotHeight -
      ((point.pnl - minPnl) / pnlRange) * plotHeight
  }));

  const zeroLineY =
    minPnl <= 0 && maxPnl >= 0
      ? PAYOFF_PADDING_TOP + plotHeight - ((0 - minPnl) / pnlRange) * plotHeight
      : null;

  const spotLineX =
    spotPrice >= minPrice && spotPrice <= maxPrice
      ? PAYOFF_PADDING_LEFT + ((spotPrice - minPrice) / priceRange) * plotWidth
      : null;

  const netDebit = legs.reduce((acc, leg) => {
    if (leg.instrument === "Stock") return acc;
    const sign = leg.direction === "Long" ? 1 : -1;
    return acc + sign * leg.premium * leg.quantity;
  }, 0);

  const breakEvenPrices = findBreakEvenPrices(points);

  return {
    width: PAYOFF_WIDTH,
    height: PAYOFF_HEIGHT,
    paddingLeft: PAYOFF_PADDING_LEFT,
    paddingRight: PAYOFF_PADDING_RIGHT,
    paddingTop: PAYOFF_PADDING_TOP,
    paddingBottom: PAYOFF_PADDING_BOTTOM,
    points: polyline,
    zeroLineY,
    spotLineX,
    spotPrice,
    axisLabels: {
      minPrice: formatPrice(minPrice),
      maxPrice: formatPrice(maxPrice),
      minPnl: formatPnl(minPnl),
      maxPnl: formatPnl(maxPnl)
    },
    breakEvenPrices,
    maxProfit: maxPnl,
    maxLoss: minPnl,
    netDebit,
    caption: buildPayoffCaption(maxPnl, minPnl, breakEvenPrices),
    ariaLabel: `Payoff curve with ${legs.length} leg${legs.length === 1 ? "" : "s"}`,
    isEmpty: false
  };
}

export function buildParticipationViewModel(legs: StrategyLeg[], spotPrice: number): ParticipationViewModel {
  if (legs.length === 0) {
    return {
      totalNotional: 0,
      totalNotionalLabel: "$0",
      longNotional: 0,
      shortNotional: 0,
      netDirection: "Flat",
      netDirectionLabel: "Flat exposure",
      slices: [],
      isEmpty: true
    };
  }
  const slices = legs.map((leg) => {
    const reference = leg.instrument === "Stock" ? spotPrice : leg.strike;
    const notional = Math.abs(leg.quantity) * Math.max(0, reference);
    return { leg, notional };
  });
  const totalNotional = slices.reduce((acc, slice) => acc + slice.notional, 0);
  const longNotional = slices
    .filter((s) => s.leg.direction === "Long")
    .reduce((acc, s) => acc + s.notional, 0);
  const shortNotional = totalNotional - longNotional;
  let netDirection: "Long" | "Short" | "Flat" = "Flat";
  if (longNotional > shortNotional) netDirection = "Long";
  else if (shortNotional > longNotional) netDirection = "Short";

  const sliceModels: ParticipationSliceViewModel[] = slices.map(({ leg, notional }) => {
    const share = totalNotional > 0 ? notional / totalNotional : 0;
    return {
      legId: leg.id,
      label: leg.label,
      direction: leg.direction,
      instrument: leg.instrument,
      notional,
      share,
      sharePercent: `${(share * 100).toFixed(1)}%`,
      barWidth: `${(share * 100).toFixed(2)}%`,
      tone: leg.direction === "Long" ? "long" : "short",
      detail: buildSliceDetail(leg)
    };
  });

  return {
    totalNotional,
    totalNotionalLabel: formatCurrency(totalNotional),
    longNotional,
    shortNotional,
    netDirection,
    netDirectionLabel: buildNetDirectionLabel(netDirection, longNotional, shortNotional),
    slices: sliceModels,
    isEmpty: false
  };
}

export function buildDesignerSummaryMetrics(payoff: PayoffChartViewModel, participation: ParticipationViewModel): DesignerSummaryMetric[] {
  if (payoff.isEmpty) {
    return [
      { id: "max-profit", label: "Max profit", value: "—", detail: "Add legs to compute", tone: "default" },
      { id: "max-loss", label: "Max loss", value: "—", detail: "Add legs to compute", tone: "default" },
      { id: "net-debit", label: "Net debit", value: "—", detail: "Add legs to compute", tone: "default" },
      { id: "net-direction", label: "Net direction", value: "Flat", detail: "No exposure", tone: "default" }
    ];
  }
  return [
    {
      id: "max-profit",
      label: "Max profit",
      value: formatPnl(payoff.maxProfit),
      detail: `Top of sampled range`,
      tone: payoff.maxProfit > 0 ? "success" : "default"
    },
    {
      id: "max-loss",
      label: "Max loss",
      value: formatPnl(payoff.maxLoss),
      detail: `Bottom of sampled range`,
      tone: payoff.maxLoss < 0 ? "danger" : "default"
    },
    {
      id: "net-debit",
      label: "Net debit",
      value: formatPnl(payoff.netDebit),
      detail: payoff.netDebit >= 0 ? "Debit paid" : "Credit received",
      tone: payoff.netDebit > 0 ? "warning" : payoff.netDebit < 0 ? "success" : "default"
    },
    {
      id: "net-direction",
      label: "Net direction",
      value: participation.netDirection,
      detail: participation.netDirectionLabel,
      tone:
        participation.netDirection === "Long"
          ? "success"
          : participation.netDirection === "Short"
          ? "warning"
          : "default"
    }
  ];
}

export function buildSelectedLegDetailViewModel(
  leg: StrategyLeg | null,
  spotPrice: number,
  legCount: number
): StrategyLegDetailViewModel | null {
  if (!leg) {
    return null;
  }

  const payoffAtSpot = computeLegPayoff(leg, spotPrice);
  const isOption = leg.instrument !== "Stock";
  const breakEven = isOption
    ? leg.instrument === "Call"
      ? leg.strike + leg.premium
      : Math.max(0, leg.strike - leg.premium)
    : leg.strike;
  const statusVariant = leg.direction === "Long" ? "success" : "warning";
  const exposureLabel = leg.direction === "Long" ? "Long exposure" : "Short exposure";

  return {
    id: STRATEGY_DESIGNER_SELECTED_LEG_DETAIL_PANEL_ID,
    ariaLabel: `Selected strategy leg detail for ${leg.label}`,
    eyebrow: "Selected leg",
    title: leg.label,
    subtitle: `${leg.direction} ${leg.instrument} · ${legCount} leg${legCount === 1 ? "" : "s"} on canvas`,
    description: isOption
      ? `${leg.direction} ${leg.instrument.toLowerCase()} sampled against spot ${formatPrice(spotPrice)}; payoff at spot is ${formatPnl(payoffAtSpot)}.`
      : `${leg.direction} stock exposure uses entry ${formatPrice(leg.strike)} against spot ${formatPrice(spotPrice)}; payoff at spot is ${formatPnl(payoffAtSpot)}.`,
    statusLabel: exposureLabel,
    statusVariant,
    fields: [
      { id: "leg-id", label: "Leg ID", value: leg.id, tone: "muted" },
      { id: "quantity", label: "Quantity", value: formatQuantity(leg.quantity), tone: "default" },
      { id: "strike", label: isOption ? "Strike" : "Entry price", value: formatPrice(leg.strike), tone: "default" },
      { id: "premium", label: "Premium", value: isOption ? formatPrice(leg.premium) : "Not applicable", tone: isOption ? "default" : "muted" },
      { id: "break-even", label: isOption ? "Break-even" : "Reference price", value: formatPrice(breakEven), tone: "default" },
      { id: "payoff-at-spot", label: "Payoff at spot", value: formatPnl(payoffAtSpot), tone: payoffAtSpot > 0 ? "success" : payoffAtSpot < 0 ? "danger" : "muted" }
    ]
  };
}

export function buildSelectedLegDetailEmptyState(legCount: number): StrategyLegDetailEmptyStateViewModel {
  return {
    title: legCount === 0 ? "No legs on canvas" : "No leg selected",
    description: legCount === 0
      ? "Add a leg from the palette or load the sample spread to inspect strategy evidence."
      : "Select a canvas leg to inspect payoff contribution, break-even, and editable parameters.",
    ariaLabel: "Strategy leg detail empty state"
  };
}

export function reorderLegs(legs: StrategyLeg[], sourceId: string, targetId: string): StrategyLeg[] {
  if (sourceId === targetId) return legs;
  const sourceIndex = legs.findIndex((leg) => leg.id === sourceId);
  const targetIndex = legs.findIndex((leg) => leg.id === targetId);
  if (sourceIndex < 0 || targetIndex < 0) return legs;
  const next = legs.slice();
  const [moved] = next.splice(sourceIndex, 1);
  next.splice(targetIndex, 0, moved);
  return next;
}

export function buildLegFromPalette(entry: StrategyLegPaletteEntry, existing: StrategyLeg[]): StrategyLeg {
  const ordinal = existing.filter((leg) => leg.kind === entry.kind).length + 1;
  const id = `${entry.kind}-${ordinal}-${Math.random().toString(36).slice(2, 8)}`;
  const label = `${entry.label}${entry.defaults.instrument === "Stock" ? "" : ` · ${entry.defaults.strike}`}${ordinal > 1 ? ` (${ordinal})` : ""}`;
  return {
    id,
    label,
    ...entry.defaults
  };
}

export function buildCanvasLegViewModels(
  legs: StrategyLeg[],
  selectedLegId: string | null
): StrategyCanvasLegViewModel[] {
  return legs.map((leg, index) => {
    const isSelected = selectedLegId === leg.id;
    const isOption = leg.instrument !== "Stock";
    const ordinal = index + 1;
    const fieldIdPrefix = `strategy-leg-${slugifyId(leg.id)}`;
    const first = index === 0;
    const last = index === legs.length - 1;
    return {
      ...leg,
      isSelected,
      directionTone: leg.direction === "Long" ? "success" : "warning",
      isOption,
      detailPanelId: STRATEGY_DESIGNER_SELECTED_LEG_DETAIL_PANEL_ID,
      fieldIds: {
        direction: `${fieldIdPrefix}-direction`,
        quantity: `${fieldIdPrefix}-quantity`,
        strike: `${fieldIdPrefix}-strike`,
        premium: `${fieldIdPrefix}-premium`
      },
      containerAriaLabel: `${leg.label}, ${leg.direction} ${leg.instrument}, leg ${ordinal} of ${legs.length}`,
      selectButtonLabel: isSelected ? "Selected" : "Select",
      selectButtonAriaLabel: `${isSelected ? "Selected" : "Select"} ${leg.label}`,
      selectButtonAriaControls: STRATEGY_DESIGNER_SELECTED_LEG_DETAIL_PANEL_ID,
      selectButtonAriaExpanded: isSelected,
      removeButtonAriaLabel: `Remove ${leg.label}`,
      directionAriaLabel: `Direction for ${leg.label}`,
      quantityAriaLabel: `Quantity for ${leg.label}`,
      strikeFieldLabel: isOption ? "Strike" : "Entry price",
      strikeAriaLabel: `${isOption ? "Strike" : "Entry price"} for ${leg.label}`,
      premiumAriaLabel: `Premium for ${leg.label}`,
      premiumUnavailableAriaLabel: `Premium not applicable for ${leg.label}`,
      moveUpCommand: {
        label: "Move up",
        ariaLabel: `Move ${leg.label} up`,
        disabled: first,
        disabledReason: first ? `${leg.label} is already the first leg.` : null
      },
      moveDownCommand: {
        label: "Move down",
        ariaLabel: `Move ${leg.label} down`,
        disabled: last,
        disabledReason: last ? `${leg.label} is already the last leg.` : null
      }
    };
  });
}

export function useStrategyDesignerViewModel(initialLegs: StrategyLeg[] = []): StrategyDesignerViewModel {
  const [legs, setLegs] = useState<StrategyLeg[]>(initialLegs);
  const [spotPrice, setSpotPriceState] = useState<number>(100);
  const [spotPriceDraft, setSpotPriceDraft] = useState<string>("100");
  const [selectedLegId, setSelectedLegId] = useState<string | null>(null);
  const [clearCanvasConfirmationPending, setClearCanvasConfirmationPending] = useState(false);
  const [builderDocument, setBuilderDocument] = useState<StrategyBuilderDocument>(() => cloneStrategyBuilderDocument(DEFAULT_STRATEGY_BUILDER_DOCUMENT));
  const [selectedBuilderCellId, setSelectedBuilderCellId] = useState<string | null>(() => DEFAULT_STRATEGY_BUILDER_DOCUMENT.cells[0]?.cellId ?? null);
  const [builderFieldSearch, setBuilderFieldSearch] = useState<string>("");

  const clearPendingCanvasConfirmation = useCallback(() => {
    setClearCanvasConfirmationPending(false);
  }, []);

  const setSpotPrice = useCallback((price: number) => {
    if (!Number.isFinite(price) || price < 0) return;
    clearPendingCanvasConfirmation();
    setSpotPriceState(price);
    setSpotPriceDraft(price.toString());
  }, [clearPendingCanvasConfirmation]);

  const updateSpotPriceDraft = useCallback((value: string) => {
    clearPendingCanvasConfirmation();
    setSpotPriceDraft(value);
  }, [clearPendingCanvasConfirmation]);

  const commitSpotPriceDraft = useCallback((value?: string) => {
    clearPendingCanvasConfirmation();
    const nextDraft = value ?? spotPriceDraft;
    const parsed = Number.parseFloat(nextDraft);
    if (Number.isFinite(parsed) && parsed >= 0) {
      setSpotPriceState(parsed);
      setSpotPriceDraft(parsed.toString());
      return;
    }

    setSpotPriceDraft(spotPrice.toString());
  }, [clearPendingCanvasConfirmation, spotPrice, spotPriceDraft]);

  const addLegFromPalette = useCallback((kind: StrategyLegKind): string => {
    clearPendingCanvasConfirmation();
    const entry = PALETTE.find((item) => item.kind === kind);
    if (!entry) return "";

    const next = buildLegFromPalette(entry, legs);
    setLegs((current) => [...current, next]);
    setSelectedLegId(next.id);
    return next.id;
  }, [clearPendingCanvasConfirmation, legs]);

  const removeLeg = useCallback((id: string) => {
    clearPendingCanvasConfirmation();
    setLegs((current) => current.filter((leg) => leg.id !== id));
    setSelectedLegId((current) => (current === id ? null : current));
  }, [clearPendingCanvasConfirmation]);

  const updateLeg = useCallback(
    (id: string, patch: Partial<Pick<StrategyLeg, "quantity" | "strike" | "premium" | "direction">>) => {
      clearPendingCanvasConfirmation();
      setLegs((current) =>
        current.map((leg) => {
          if (leg.id !== id) return leg;
          const next: StrategyLeg = {
            ...leg,
            quantity:
              patch.quantity !== undefined && Number.isFinite(patch.quantity) && patch.quantity > 0
                ? patch.quantity
                : leg.quantity,
            strike:
              patch.strike !== undefined && Number.isFinite(patch.strike) && patch.strike >= 0
                ? patch.strike
                : leg.strike,
            premium:
              patch.premium !== undefined && Number.isFinite(patch.premium) && patch.premium >= 0
                ? patch.premium
                : leg.premium,
            direction: patch.direction ?? leg.direction
          };
          return next;
        })
      );
    },
    [clearPendingCanvasConfirmation]
  );

  const reorderLeg = useCallback((sourceId: string, targetId: string) => {
    clearPendingCanvasConfirmation();
    setLegs((current) => reorderLegs(current, sourceId, targetId));
  }, [clearPendingCanvasConfirmation]);

  const clearCanvas = useCallback(() => {
    if (legs.length === 0) {
      setClearCanvasConfirmationPending(false);
      return;
    }

    if (!clearCanvasConfirmationPending) {
      setClearCanvasConfirmationPending(true);
      return;
    }

    setClearCanvasConfirmationPending(false);
    setLegs([]);
    setSelectedLegId(null);
  }, [clearCanvasConfirmationPending, legs.length]);

  const loadSample = useCallback(() => {
    clearPendingCanvasConfirmation();
    const sample = getStrategyDesignerSampleLegs();
    setLegs(sample);
    setSelectedLegId(sample[0]?.id ?? null);
  }, [clearPendingCanvasConfirmation]);

  const selectLeg = useCallback((id: string | null) => {
    clearPendingCanvasConfirmation();
    setSelectedLegId(id);
  }, [clearPendingCanvasConfirmation]);

  const selectBuilderCell = useCallback((cellId: string | null) => {
    setSelectedBuilderCellId(cellId);
  }, []);

  const loadBuilderTemplate = useCallback((templateId: string) => {
    const document = loadStrategyBuilderTemplate(templateId);
    setBuilderDocument(document);
    setSelectedBuilderCellId(document.cells[0]?.cellId ?? null);
    setBuilderFieldSearch("");
  }, []);

  const workbench = useMemo(
    () =>
      buildStrategyBuilderWorkbenchViewModel({
        document: builderDocument,
        selectedCellId: selectedBuilderCellId,
        fieldSearch: builderFieldSearch,
        setFieldSearch: setBuilderFieldSearch,
        selectCell: selectBuilderCell,
        loadTemplate: loadBuilderTemplate
      }),
    [builderDocument, builderFieldSearch, loadBuilderTemplate, selectBuilderCell, selectedBuilderCellId]
  );

  const payoff = useMemo(() => buildPayoffChartViewModel(legs, spotPrice), [legs, spotPrice]);
  const participation = useMemo(() => buildParticipationViewModel(legs, spotPrice), [legs, spotPrice]);
  const metrics = useMemo(() => buildDesignerSummaryMetrics(payoff, participation), [payoff, participation]);
  const canvasLegs = useMemo(() => buildCanvasLegViewModels(legs, selectedLegId), [legs, selectedLegId]);
  const selectedLeg = useMemo(
    () => legs.find((leg) => leg.id === selectedLegId) ?? null,
    [legs, selectedLegId]
  );
  const selectedLegDetail = useMemo(
    () => buildSelectedLegDetailViewModel(selectedLeg, spotPrice, legs.length),
    [legs.length, selectedLeg, spotPrice]
  );
  const selectedLegDetailEmptyState = useMemo(
    () => buildSelectedLegDetailEmptyState(legs.length),
    [legs.length]
  );
  const clearCanvasDisabled = legs.length === 0;
  const clearCanvasLegCountLabel = `${legs.length} leg${legs.length === 1 ? "" : "s"}`;

  return {
    workbench,
    legs,
    canvasLegs,
    palette: PALETTE,
    spotPrice,
    spotPriceField: {
      label: "Spot price",
      value: spotPriceDraft,
      min: 0,
      step: "0.01",
      inputMode: "decimal",
      ariaLabel: "Underlying spot price for payoff sampling"
    },
    updateSpotPriceDraft,
    commitSpotPriceDraft,
    setSpotPrice,
    addLegFromPalette,
    removeLeg,
    updateLeg,
    reorderLeg,
    clearCanvas,
    loadSample,
    clearCanvasCommand: {
      label: clearCanvasConfirmationPending ? "Confirm clear" : "Clear canvas",
      ariaLabel: clearCanvasConfirmationPending
        ? `Confirm clear strategy canvas and remove ${clearCanvasLegCountLabel}`
        : `Clear strategy canvas. Press again to confirm removing ${clearCanvasLegCountLabel}.`,
      disabled: clearCanvasDisabled,
      disabledReason: clearCanvasDisabled ? "No strategy legs to clear." : null,
      confirmationPending: clearCanvasConfirmationPending
    },
    loadSampleCommand: {
      label: "Load sample",
      ariaLabel: "Load sample bull call spread",
      disabled: false,
      disabledReason: null
    },
    addLongCallCommand: {
      label: "Add long call",
      ariaLabel: "Append a default long call leg",
      disabled: false,
      disabledReason: null
    },
    canvasTitle: `Canvas · ${legs.length} leg${legs.length === 1 ? "" : "s"}`,
    payoff,
    participation,
    metrics,
    emptyStateMessage:
      "Drop a leg from the palette onto the canvas, or load the sample bull call spread to get started.",
    selectedLegId,
    selectLeg,
    selectedLeg,
    selectedLegDetailPanelId: STRATEGY_DESIGNER_SELECTED_LEG_DETAIL_PANEL_ID,
    selectedLegDetail,
    selectedLegDetailEmptyState
  };
}

function cloneStrategyBuilderDocument(document: StrategyBuilderDocument): StrategyBuilderDocument {
  return {
    ...document,
    universe: [...document.universe],
    cells: document.cells.map((cell) => ({
      ...cell,
      fieldRefs: [...cell.fieldRefs],
      parameters: cell.parameters ? { ...cell.parameters } : undefined
    })),
    transitions: document.transitions.map((transition) => ({ ...transition }))
  };
}

function buildStrategyBuilderCellViewModels(
  cells: StrategyBuilderCell[],
  selectedCellId: string | null,
  fieldMap: Map<string, StrategyBuilderFieldCatalogItem>,
  messages: StrategyBuilderValidationMessage[]
): StrategyBuilderCellViewModel[] {
  return cells.map((cell) => {
    const cellMessages = messages.filter((message) => message.targetId === cell.cellId);
    const hasError = cellMessages.some((message) => message.severity === "error");
    const hasWarning = cellMessages.some((message) => message.severity === "warning");
    const disabledReason = cellMessages.find((message) => message.severity === "error")?.message ?? null;
    return {
      ...cell,
      isSelected: cell.cellId === selectedCellId,
      status: hasError ? "blocked" : hasWarning ? "warning" : "ready",
      statusLabel: hasError ? "Blocked" : hasWarning ? "Review" : "Ready",
      disabledReason,
      fieldChips: cell.fieldRefs.map((fieldRef) => {
        const field = fieldMap.get(fieldRef);
        return {
          fieldId: fieldRef,
          label: field?.label ?? fieldRef,
          isEnabled: field?.isEnabled ?? false,
          disabledReason: field?.disabledReason ?? (field ? null : `${fieldRef} is not in the Meridian field catalog.`),
          statusLabel: field?.isEnabled ? "Mapped" : "Disabled"
        };
      }),
      selectButtonAriaLabel: `${cell.cellId === selectedCellId ? "Selected" : "Select"} ${cell.label}`,
      detailPanelId: STRATEGY_BUILDER_SELECTED_CELL_DETAIL_PANEL_ID
    };
  });
}

function buildStrategyBuilderTransitionViewModels(
  document: StrategyBuilderDocument,
  messages: StrategyBuilderValidationMessage[]
): StrategyBuilderTransitionViewModel[] {
  const cells = new Map(document.cells.map((cell) => [cell.cellId, cell.label]));
  return document.transitions.map((transition) => {
    const transitionMessages = messages.filter((message) => message.targetId === transition.transitionId);
    const hasError = transitionMessages.some((message) => message.severity === "error");
    const from = cells.get(transition.fromCellId) ?? transition.fromCellId;
    const to = cells.get(transition.toCellId) ?? transition.toCellId;
    return {
      ...transition,
      label: `${from} -> ${to}`,
      status: hasError ? "blocked" : transition.kind === "loop" ? "warning" : "ready",
      statusLabel: hasError ? "Blocked" : transition.kind === "loop" ? "Guarded loop" : "Ready",
      detail: transition.kind === "loop"
        ? `${transition.condition}; max ${transition.maxIterations ?? "not set"} iteration${transition.maxIterations === 1 ? "" : "s"}`
        : transition.condition
    };
  });
}

function buildStrategyBuilderTrace(
  document: StrategyBuilderDocument,
  selectedCellId: string | null,
  messages: StrategyBuilderValidationMessage[]
): StrategyBuilderRunTraceEntryViewModel[] {
  const trace: StrategyBuilderRunTraceEntryViewModel[] = [
    {
      stepId: "validate",
      label: "Validate design",
      status: messages.some((message) => message.severity === "error") ? "blocked" : "complete",
      detail: messages.length === 0 ? "All design checks passed." : `${messages.length} validation message${messages.length === 1 ? "" : "s"}.`,
      cellId: null,
      isSelectedCell: false
    }
  ];

  for (const cell of document.cells) {
    const cellMessages = messages.filter((message) => message.targetId === cell.cellId);
    const hasError = cellMessages.some((message) => message.severity === "error");
    trace.push({
      stepId: `cell-${cell.cellId}`,
      label: cell.label,
      status: hasError ? "blocked" : "ready",
      detail: `${cell.kind} cell compiles into ${cell.purpose}; fields ${cell.fieldRefs.join(", ") || "none"}.`,
      cellId: cell.cellId,
      isSelectedCell: cell.cellId === selectedCellId
    });
  }

  trace.push({
    stepId: "quant-script",
    label: "Compile QuantScript",
    status: messages.some((message) => message.severity === "error") ? "blocked" : "ready",
    detail: "Generated source is sent to /api/workstation/strategy/designer/run-backtest.",
    cellId: null,
    isSelectedCell: false
  });

  return trace;
}

function buildKindDescription(cell: StrategyBuilderCell, mappedCount: number): string {
  switch (cell.kind) {
    case "state":
      return `State machine node. Exit: ${cell.parameters?.["exitCondition"] ?? "(not set)"}.`;
    case "concurrent":
      return `Parallel evaluation point (${cell.parameters?.["semantics"] ?? "semantics not set"}).`;
    case "universe-builder":
      return `Structured universe for ${cell.parameters?.["assetClass"] ?? "(asset class not set)"}.`;
    case "trade":
      return `Trade intent: ${cell.parameters?.["direction"] ?? "?"} ${cell.parameters?.["instrument"] ?? "?"} via ${cell.parameters?.["sizingMethod"] ?? "?"}.`;
    default:
      return `${cell.purpose} stage with ${mappedCount} mapped field${mappedCount === 1 ? "" : "s"}.`;
  }
}

function buildKindParameterFields(cell: StrategyBuilderCell): StrategyLegDetailFieldViewModel[] {
  if (cell.kind === "state") {
    const p = getStateCellParams(cell);
    if (!p) return [];
    return [
      { id: "state-label", label: "State label", value: p.stateLabel, tone: "default" },
      { id: "exit-condition", label: "Exit condition", value: p.exitCondition, tone: "default" },
      { id: "entry-condition", label: "Entry condition", value: p.entryCondition ?? "(none)", tone: p.entryCondition ? "default" : "muted" },
      { id: "is-terminal", label: "Terminal state", value: p.isTerminal === "true" ? "Yes" : "No", tone: p.isTerminal === "true" ? "warning" : "muted" }
    ];
  }
  if (cell.kind === "concurrent") {
    const p = getConcurrentCellParams(cell);
    if (!p) return [];
    return [
      { id: "branch-ids", label: "Branch cell IDs", value: p.branchIds, tone: "default" },
      { id: "semantics", label: "Evaluation semantics", value: p.semantics, tone: "default" }
    ];
  }
  if (cell.kind === "universe-builder") {
    const p = getUniverseBuilderCellParams(cell);
    if (!p) return [];
    return [
      { id: "asset-class", label: "Asset class", value: p.assetClass, tone: "default" },
      { id: "include-rules", label: "Include rules", value: p.includeRules, tone: "default" },
      { id: "exclude-rules", label: "Exclude rules", value: p.excludeRules ?? "(none)", tone: p.excludeRules ? "default" : "muted" },
      { id: "size-constraints", label: "Size constraints", value: `min ${p.minSize ?? "—"} / max ${p.maxSize ?? "—"}`, tone: "default" }
    ];
  }
  if (cell.kind === "trade") {
    const p = getTradeCellParams(cell);
    if (!p) return [];
    return [
      { id: "instrument", label: "Instrument type", value: p.instrument, tone: "default" },
      { id: "direction", label: "Direction", value: p.direction, tone: p.direction === "Buy" || p.direction === "BuyToCover" ? "success" : "warning" },
      { id: "sizing-method", label: "Sizing method", value: p.sizingMethod, tone: "default" },
      { id: "price-constraint", label: "Price constraint", value: p.priceConstraint ?? "Market", tone: p.priceConstraint ? "default" : "muted" },
      { id: "sizing-value", label: "Sizing value", value: p.sizingValue ?? "—", tone: "default" }
    ];
  }
  return [];
}

function buildStrategyBuilderCellDetail(
  cell: StrategyBuilderCell,
  fieldMap: Map<string, StrategyBuilderFieldCatalogItem>
): StrategyBuilderCellDetailViewModel {
  const mapped = cell.fieldRefs
    .map((fieldRef) => fieldMap.get(fieldRef))
    .filter((field): field is StrategyBuilderFieldCatalogItem => field !== undefined);

  return {
    id: STRATEGY_BUILDER_SELECTED_CELL_DETAIL_PANEL_ID,
    title: cell.label,
    eyebrow: `${cell.kind} cell`,
    description: buildKindDescription(cell, mapped.length),
    source: cell.source,
    ariaLabel: `Strategy builder cell detail for ${cell.label}`,
    fields: [
      { id: "cell-id", label: "Cell ID", value: cell.cellId, tone: "muted" },
      { id: "purpose", label: "Purpose", value: cell.purpose, tone: "default" },
      { id: "kind", label: "Kind", value: cell.kind, tone: "default" },
      {
        id: "fields",
        label: "Fields",
        value: cell.fieldRefs.length > 0 ? cell.fieldRefs.join(", ") : "None",
        tone: cell.fieldRefs.some((fieldRef) => fieldMap.get(fieldRef)?.isEnabled === false) ? "danger" : "default"
      },
      ...buildKindParameterFields(cell)
    ]
  };
}

function buildStrategyBuilderDatasetFingerprint(document: StrategyBuilderDocument): string {
  const source = [
    document.datasetReference,
    document.universe.join(","),
    document.cells.flatMap((cell) => cell.fieldRefs).sort().join(","),
    document.version
  ].join("|");
  let hash = 0;
  for (let index = 0; index < source.length; index += 1) {
    hash = (Math.imul(31, hash) + source.charCodeAt(index)) | 0;
  }
  return `strategy-design:${Math.abs(hash).toString(16).padStart(8, "0")}`;
}

function buildPayoffCaption(maxProfit: number, maxLoss: number, breakEvenPrices: number[]): string {
  const breakEvenLabel = breakEvenPrices.length
    ? `Break-even ${breakEvenPrices.map(formatPrice).join(", ")}`
    : "No break-even within sampled range";
  return `${breakEvenLabel} · Max ${formatPnl(maxProfit)} / Min ${formatPnl(maxLoss)}`;
}

function buildSliceDetail(leg: StrategyLeg): string {
  if (leg.instrument === "Stock") {
    return `${leg.quantity} shares @ ${formatPrice(leg.strike)}`;
  }
  return `${leg.quantity} contract${leg.quantity === 1 ? "" : "s"} · strike ${formatPrice(leg.strike)} · premium ${formatPrice(leg.premium)}`;
}

function buildNetDirectionLabel(direction: "Long" | "Short" | "Flat", longNotional: number, shortNotional: number): string {
  if (direction === "Flat") return "Balanced long/short notional";
  if (direction === "Long") return `Long-biased by ${formatCurrency(longNotional - shortNotional)} notional`;
  return `Short-biased by ${formatCurrency(shortNotional - longNotional)} notional`;
}

function formatPrice(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return `$${value.toFixed(2)}`;
}

function formatPnl(value: number): string {
  if (!Number.isFinite(value)) return "—";
  const sign = value > 0 ? "+" : value < 0 ? "−" : "";
  return `${sign}$${Math.abs(value).toFixed(2)}`;
}

function formatQuantity(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return value.toLocaleString(undefined, {
    maximumFractionDigits: 4
  });
}

function formatCurrency(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return `$${value.toLocaleString(undefined, { maximumFractionDigits: 0 })}`;
}

function slugifyId(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "") || "leg";
}
