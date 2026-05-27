import {
  StrategyFormulaWorkbench,
  type StrategyFormulaCell,
  type StrategyFormulaField,
  type StrategyFormulaSuggestion
} from "@/components/meridian/strategy-formula-workbench";

const STRATEGY_FORMULA_FIELDS: StrategyFormulaField[] = [
  {
    fieldId: "PRICE",
    label: "Last price",
    category: "Market",
    typeName: "decimal",
    description: "Latest trusted price from the active Meridian provider route.",
    example: "192.34"
  },
  {
    fieldId: "MOMENTUM_63D",
    label: "63-day momentum",
    category: "Market",
    typeName: "percent",
    description: "Rolling return over the latest 63 trading sessions.",
    example: "8.4%"
  },
  {
    fieldId: "VOLATILITY_20D",
    label: "20-day volatility",
    category: "Risk",
    typeName: "percent",
    description: "Realized volatility over the latest 20 trading sessions.",
    example: "18.2%"
  },
  {
    fieldId: "PORTFOLIO_WEIGHT",
    label: "Portfolio weight",
    category: "Portfolio",
    typeName: "percent",
    description: "Current position weight from portfolio and run snapshots.",
    example: "4.5%"
  },
  {
    fieldId: "RATING",
    label: "Credit rating",
    category: "Security Master",
    typeName: "string",
    description: "Normalized credit-quality bucket from trusted reference data.",
    example: "A"
  },
  {
    fieldId: "NEXT_COUPON_DATE",
    label: "Next coupon date",
    category: "Security Master",
    typeName: "date",
    description: "Next scheduled fixed-income coupon date.",
    example: "2026-08-15"
  }
];

const STRATEGY_FORMULA_SUGGESTIONS: StrategyFormulaSuggestion[] = [
  {
    suggestionId: "momentum-score",
    expression: "MOMENTUM_63D - VOLATILITY_20D",
    description: "Simple momentum score adjusted for recent realized risk.",
    tone: "calculation",
    triggers: ["score", "momentum", "volatility"]
  },
  {
    suggestionId: "risk-cap",
    expression: "If PORTFOLIO_WEIGHT > 0.05 Then Block",
    description: "Prevent orders that would exceed a five percent sleeve weight.",
    tone: "risk",
    triggers: ["risk", "weight", "block"]
  },
  {
    suggestionId: "investment-grade",
    expression: "RATING >= \"BBB\"",
    description: "Filter to investment-grade reference-data coverage.",
    tone: "filter",
    triggers: ["rating", "credit", "filter"]
  },
  {
    suggestionId: "equal-weight",
    expression: "TargetWeight = 1 / SelectedCount",
    description: "Allocate selected names with equal target weights.",
    tone: "allocation",
    triggers: ["weight", "allocation", "target"]
  }
];

const STRATEGY_FORMULA_CELLS: StrategyFormulaCell[] = [
  {
    cellId: "A1",
    label: "Universe filter",
    mode: "visual",
    source: "RATING >= \"BBB\"",
    status: "Ready"
  },
  {
    cellId: "A2",
    label: "Signal score",
    mode: "formula",
    source: "MOMENTUM_63D - VOLATILITY_20D",
    status: "Ready"
  },
  {
    cellId: "A3",
    label: "Risk cap",
    mode: "code",
    source: "If PORTFOLIO_WEIGHT > 0.05 Then Block",
    status: "Review"
  }
];

export function StrategyFormulaWorkbenchScreen() {
  return (
    <StrategyFormulaWorkbench
      title="Formula Workbench"
      description="A Meridian-native strategy authoring surface inspired by searchable field pickers, formula autocomplete, and cell-based strategy construction."
      fields={STRATEGY_FORMULA_FIELDS}
      suggestions={STRATEGY_FORMULA_SUGGESTIONS}
      initialCells={STRATEGY_FORMULA_CELLS}
    />
  );
}
