import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { STRATEGY_DESIGNER_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import {
  buildFieldCatalogSearchState,
  buildCanvasLegViewModels,
  buildFormulaSuggestions,
  buildLegFromPalette,
  buildDesignerSummaryMetrics,
  buildParticipationViewModel,
  buildPayoffChartViewModel,
  buildPayoffSeries,
  buildStrategyBuilderWorkbenchViewModel,
  buildSelectedLegDetailEmptyState,
  buildSelectedLegDetailViewModel,
  computeLegPayoff,
  computePortfolioPayoff,
  filterStrategyBuilderFields,
  findBreakEvenPrices,
  getStrategyBuilderFieldCatalog,
  getStrategyBuilderTemplates,
  getStateCellParams,
  getTradeCellParams,
  getConcurrentCellParams,
  getUniverseBuilderCellParams,
  getStrategyDesignerPalette,
  getStrategyDesignerSampleLegs,
  loadStrategyBuilderTemplate,
  reorderLegs,
  useStrategyDesignerViewModel,
  validateStrategyBuilderDocument,
  type StrategyBuilderCell,
  type StrategyLeg
} from "@/screens/strategy-designer-screen.view-model";

const longCall100: StrategyLeg = {
  id: "leg-1",
  kind: "long-call",
  label: "Long Call 100",
  direction: "Long",
  instrument: "Call",
  quantity: 1,
  strike: 100,
  premium: 4
};

const shortCall110: StrategyLeg = {
  id: "leg-2",
  kind: "short-call",
  label: "Short Call 110",
  direction: "Short",
  instrument: "Call",
  quantity: 1,
  strike: 110,
  premium: 2
};

const longStock: StrategyLeg = {
  id: "stock-1",
  kind: "long-stock",
  label: "Long Stock",
  direction: "Long",
  instrument: "Stock",
  quantity: 100,
  strike: 100,
  premium: 0
};

describe("strategy designer view-model", () => {
  it("filters the Strategy Builder field catalog and preserves disabled AMX reasons", () => {
    const fields = getStrategyBuilderFieldCatalog();
    const filtered = filterStrategyBuilderFields("amx", fields);

    expect(filtered.map((field) => field.fieldId)).toContain("AMX_PRIVATE_SCORE");
    expect(filtered[0].isEnabled).toBe(false);
    expect(filtered[0].disabledReason).toContain("No Meridian canonical source");
  });

  it("builds formula suggestions from enabled canonical fields only", () => {
    const suggestions = buildFormulaSuggestions("mom", getStrategyBuilderFieldCatalog());

    expect(suggestions.map((field) => field.fieldId)).toContain("MOMENTUM_63D");
    expect(suggestions.some((field) => field.fieldId === "AMX_PRIVATE_SCORE")).toBe(false);
  });

  it("validates transition loops and disabled field references in the Strategy Builder document", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const invalid = {
      ...document,
      cells: [
        ...document.cells,
        {
          cellId: "amx-score",
          label: "Prototype AMX score",
          kind: "formula" as const,
          purpose: "rank" as const,
          source: "AMX_PRIVATE_SCORE > 0.8",
          fieldRefs: ["AMX_PRIVATE_SCORE"]
        }
      ],
      transitions: [
        ...document.transitions,
        { transitionId: "bad-loop", fromCellId: "proof-run", toCellId: "momentum-score", kind: "loop" as const, condition: "rebalance" }
      ]
    };

    const messages = validateStrategyBuilderDocument(invalid);

    expect(messages.map((message) => message.code)).toEqual(
      expect.arrayContaining(["DisabledField", "LoopGuardRequired", "LoopRationaleRequired"])
    );
  });

  it("uses control-cell wording for Strategy Builder promotion-review warnings", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const withoutRiskGuard = {
      ...document,
      cells: document.cells.filter((cell) => cell.purpose !== "risk" && cell.kind !== "governance")
    };

    const messages = validateStrategyBuilderDocument(withoutRiskGuard);
    const warning = messages.find((message) => message.code === "RiskGuardRecommended");

    expect(warning?.message).toBe("Add a risk or control cell before promotion review.");
    expect(warning?.message).not.toContain("governance cell");
  });

  it("builds selected cell, trace, template, and backtest state in the workbench view-model", () => {
    const document = loadStrategyBuilderTemplate("investment-grade-income");
    const vm = buildStrategyBuilderWorkbenchViewModel({
      document,
      selectedCellId: "carry-rank",
      fieldSearch: "duration"
    });

    expect(getStrategyBuilderTemplates().map((template) => template.templateId)).toContain("options-payoff");
    expect(getStrategyBuilderTemplates()).toContainEqual(
      expect.objectContaining({
        templateId: "equity-momentum-breakout",
        description: "Formula autocomplete, transition map, risk/control cell, and proof run.",
        tags: expect.not.arrayContaining(["governance"])
      })
    );
    expect(loadStrategyBuilderTemplate("equity-momentum-breakout").description).toContain("risk/control guard");
    expect(loadStrategyBuilderTemplate("equity-momentum-breakout").description).not.toContain("governance guard");
    expect(vm.selectedCellDetail?.title).toBe("Carry rank");
    expect(vm.trace.find((step) => step.cellId === "carry-rank")?.isSelectedCell).toBe(true);
    expect(vm.filteredFields.map((field) => field.fieldId)).toContain("DURATION");
    expect(vm.fieldSearchState).toMatchObject({
      inputId: "strategy-builder-field-catalog-search",
      label: "Search field catalog",
      ariaLabel: "Search Strategy Builder field catalog",
      emptyState: null
    });
    expect(vm.fieldSearchState.resultCountText).toContain('match "duration"');
    expect(vm.backtest.runCommand.disabled).toBe(false);
    expect(vm.backtest.routeActions).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          id: "templates",
          href: STRATEGY_DESIGNER_API_ENDPOINTS.templates,
          method: "GET",
          isBrowserNavigable: true,
          interactionLabel: "Open"
        }),
        expect.objectContaining({
          id: "run-backtest",
          href: STRATEGY_DESIGNER_API_ENDPOINTS.runBacktest,
          method: "POST",
          isBrowserNavigable: false,
          interactionLabel: "Reference"
        })
      ])
    );
  });

  it("derives field catalog search empty state copy from the view model", () => {
    const state = buildFieldCatalogSearchState("missing", 0, getStrategyBuilderFieldCatalog().length);

    expect(state.describedBy).toBe("strategy-builder-field-catalog-search-help strategy-builder-field-catalog-search-status");
    expect(state.resultCountText).toContain('0 fields match "missing"');
    expect(state.emptyState).toEqual({
      title: "No matching fields",
      description: 'No field catalog entries match "missing". Clear the filter to inspect mapped and disabled fields.',
      ariaLabel: "Strategy Builder field catalog empty state"
    });
  });

  it("marks backtest proof disabled when Strategy Builder validation is blocked", () => {
    const document = {
      ...loadStrategyBuilderTemplate("equity-momentum-breakout"),
      datasetReference: ""
    };

    const vm = buildStrategyBuilderWorkbenchViewModel({ document });

    expect(vm.backtest.statusLabel).toBe("Blocked");
    expect(vm.backtest.runCommand.disabled).toBe(true);
    expect(vm.backtest.runCommand.disabledReason).toContain("blocking issue");
  });

  it("returns six palette entries with stable kinds", () => {
    const palette = getStrategyDesignerPalette();
    expect(palette).toHaveLength(6);
    expect(palette.map((entry) => entry.kind)).toEqual([
      "long-call",
      "short-call",
      "long-put",
      "short-put",
      "long-stock",
      "short-stock"
    ]);
  });

  it("computes long call payoff: intrinsic - premium scaled by quantity and direction", () => {
    expect(computeLegPayoff(longCall100, 90)).toBeCloseTo(-4, 6);
    expect(computeLegPayoff(longCall100, 100)).toBeCloseTo(-4, 6);
    expect(computeLegPayoff(longCall100, 104)).toBeCloseTo(0, 6);
    expect(computeLegPayoff(longCall100, 120)).toBeCloseTo(16, 6);
  });

  it("computes short call payoff as the negative of long call", () => {
    expect(computeLegPayoff(shortCall110, 110)).toBeCloseTo(2, 6);
    expect(computeLegPayoff(shortCall110, 115)).toBeCloseTo(-3, 6);
  });

  it("computes long stock payoff linearly", () => {
    expect(computeLegPayoff(longStock, 110)).toBeCloseTo(1000, 6);
    expect(computeLegPayoff(longStock, 90)).toBeCloseTo(-1000, 6);
  });

  it("aggregates portfolio payoff across legs (bull call spread)", () => {
    const legs = [longCall100, shortCall110];
    expect(computePortfolioPayoff(legs, 90)).toBeCloseTo(-4 + 2, 6);
    expect(computePortfolioPayoff(legs, 110)).toBeCloseTo(6 + 2, 6);
    expect(computePortfolioPayoff(legs, 120)).toBeCloseTo(16 + -8, 6);
  });

  it("builds payoff chart view-model with polyline points spanning leg strikes", () => {
    const payoff = buildPayoffChartViewModel([longCall100, shortCall110], 100);
    expect(payoff.isEmpty).toBe(false);
    expect(payoff.points.length).toBeGreaterThan(50);
    expect(payoff.points[0].x).toBeCloseTo(payoff.paddingLeft, 1);
    expect(payoff.points[payoff.points.length - 1].x).toBeCloseTo(
      payoff.width - payoff.paddingRight,
      1
    );
    expect(payoff.maxProfit).toBeGreaterThan(0);
    expect(payoff.maxLoss).toBeLessThan(0);
    expect(payoff.breakEvenPrices.length).toBeGreaterThanOrEqual(1);
  });

  it("marks empty payoff view-model when there are no legs", () => {
    const payoff = buildPayoffChartViewModel([], 100);
    expect(payoff.isEmpty).toBe(true);
    expect(payoff.points).toEqual([]);
    expect(payoff.breakEvenPrices).toEqual([]);
    expect(payoff.caption).toContain("Add a leg");
  });

  it("derives spot-price field feedback from invalid commits", () => {
    const { result } = renderHook(() => useStrategyDesignerViewModel(getStrategyDesignerSampleLegs()));

    expect(result.current.spotPriceField).toMatchObject({
      id: "strategy-designer-spot-price",
      helperId: "strategy-designer-spot-price-help",
      feedbackId: "strategy-designer-spot-price-feedback",
      describedBy: "strategy-designer-spot-price-help",
      invalid: false,
      feedbackMessage: null
    });

    act(() => {
      result.current.commitSpotPriceDraft("-3");
    });

    expect(result.current.spotPriceField.value).toBe("100");
    expect(result.current.spotPriceField.invalid).toBe(true);
    expect(result.current.spotPriceField.describedBy).toBe(
      "strategy-designer-spot-price-help strategy-designer-spot-price-feedback"
    );
    expect(result.current.spotPriceField.feedbackMessage).toBe(
      "Enter a non-negative spot price. Restored $100.00 for payoff sampling."
    );

    act(() => {
      result.current.updateSpotPriceDraft("125");
    });

    expect(result.current.spotPriceField.invalid).toBe(false);
    expect(result.current.spotPriceField.feedbackMessage).toBeNull();
  });

  it("locates break-even price near the long-call strike + premium", () => {
    const series = buildPayoffSeries([longCall100], 100);
    const crossings = findBreakEvenPrices(series);
    expect(crossings.length).toBeGreaterThanOrEqual(1);
    expect(crossings[0]).toBeGreaterThan(103);
    expect(crossings[0]).toBeLessThan(105);
  });

  it("builds participation view-model with normalised notional shares", () => {
    const participation = buildParticipationViewModel([longCall100, shortCall110], 100);
    expect(participation.isEmpty).toBe(false);
    expect(participation.slices).toHaveLength(2);
    const total = participation.slices.reduce((sum, slice) => sum + slice.share, 0);
    expect(total).toBeCloseTo(1, 5);
    const long = participation.slices.find((s) => s.legId === "leg-1");
    const short = participation.slices.find((s) => s.legId === "leg-2");
    expect(long?.tone).toBe("long");
    expect(short?.tone).toBe("short");
  });

  it("computes net direction from notional weights", () => {
    const longBias = buildParticipationViewModel([longCall100, longStock], 100);
    expect(longBias.netDirection).toBe("Long");
    expect(longBias.netDirectionLabel).toContain("Long-biased");

    const balanced = buildParticipationViewModel(
      [
        { ...longCall100, id: "a" },
        { ...longCall100, id: "b", direction: "Short" }
      ],
      100
    );
    expect(balanced.netDirection).toBe("Flat");
  });

  it("summarizes designer metrics with tones derived from payoff numbers", () => {
    const payoff = buildPayoffChartViewModel([longCall100, shortCall110], 100);
    const participation = buildParticipationViewModel([longCall100, shortCall110], 100);
    const metrics = buildDesignerSummaryMetrics(payoff, participation);
    expect(metrics.map((m) => m.id)).toEqual([
      "max-profit",
      "max-loss",
      "net-debit",
      "net-direction"
    ]);
    expect(metrics[0].tone).toBe("success");
    expect(metrics[1].tone).toBe("danger");
  });

  it("falls back to default-toned placeholders when there are no legs", () => {
    const payoff = buildPayoffChartViewModel([], 100);
    const participation = buildParticipationViewModel([], 100);
    const metrics = buildDesignerSummaryMetrics(payoff, participation);
    expect(metrics.every((metric) => metric.tone === "default")).toBe(true);
    expect(metrics[0].value).toBe("—");
  });

  it("reorders legs by removing source and inserting at target position", () => {
    const legs = [longCall100, shortCall110, longStock];
    const reordered = reorderLegs(legs, longStock.id, longCall100.id);
    expect(reordered.map((leg) => leg.id)).toEqual([longStock.id, longCall100.id, shortCall110.id]);
  });

  it("returns input unchanged when reorder ids are equal or missing", () => {
    const legs = [longCall100, shortCall110];
    expect(reorderLegs(legs, longCall100.id, longCall100.id)).toBe(legs);
    expect(reorderLegs(legs, "missing", longCall100.id)).toBe(legs);
  });

  it("builds palette legs with unique ids and ordinal-aware labels", () => {
    const palette = getStrategyDesignerPalette();
    const entry = palette[0];
    const first = buildLegFromPalette(entry, []);
    const second = buildLegFromPalette(entry, [first]);
    expect(first.id).not.toBe(second.id);
    expect(first.kind).toBe(entry.kind);
    expect(second.label).toContain("(2)");
  });

  it("provides a sample strategy with two legs", () => {
    const sample = getStrategyDesignerSampleLegs();
    expect(sample).toHaveLength(2);
    expect(sample[0].direction).toBe("Long");
    expect(sample[1].direction).toBe("Short");
  });

  it("projects canvas leg selection and field accessibility state", () => {
    const rows = buildCanvasLegViewModels([longCall100, longStock], longStock.id);

    expect(rows[0]).toMatchObject({
      id: "leg-1",
      isSelected: false,
      selectButtonLabel: "Select",
      directionTone: "success",
      isOption: true,
      strikeFieldLabel: "Strike",
      fieldIds: {
        direction: "strategy-leg-leg-1-direction",
        quantity: "strategy-leg-leg-1-quantity",
        strike: "strategy-leg-leg-1-strike",
        premium: "strategy-leg-leg-1-premium"
      },
      detailPanelId: "strategy-designer-selected-leg-detail",
      selectButtonAriaControls: "strategy-designer-selected-leg-detail",
      selectButtonAriaExpanded: false,
      premiumAriaLabel: "Premium for Long Call 100",
      moveUpCommand: {
        label: "Move up",
        ariaLabel: "Move Long Call 100 up",
        disabled: true,
        disabledReason: "Long Call 100 is already the first leg."
      },
      moveDownCommand: {
        label: "Move down",
        ariaLabel: "Move Long Call 100 down",
        disabled: false,
        disabledReason: null
      }
    });
    expect(rows[0].containerAriaLabel).toContain("leg 1 of 2");
    expect(rows[1]).toMatchObject({
      id: "stock-1",
      isSelected: true,
      selectButtonLabel: "Selected",
      isOption: false,
      strikeFieldLabel: "Entry price",
      selectButtonAriaControls: "strategy-designer-selected-leg-detail",
      selectButtonAriaExpanded: true,
      premiumUnavailableAriaLabel: "Premium not applicable for Long Stock",
      moveUpCommand: {
        label: "Move up",
        ariaLabel: "Move Long Stock up",
        disabled: false,
        disabledReason: null
      },
      moveDownCommand: {
        label: "Move down",
        ariaLabel: "Move Long Stock down",
        disabled: true,
        disabledReason: "Long Stock is already the last leg."
      }
    });
  });

  it("builds selected-leg detail from payoff, break-even, and editable leg state", () => {
    const detail = buildSelectedLegDetailViewModel(longCall100, 100, 2);

    expect(detail).toMatchObject({
      id: "strategy-designer-selected-leg-detail",
      ariaLabel: "Selected strategy leg detail for Long Call 100",
      eyebrow: "Selected leg",
      title: "Long Call 100",
      subtitle: "Long Call · 2 legs on canvas",
      statusLabel: "Long exposure",
      statusVariant: "success"
    });
    expect(detail?.description).toContain("payoff at spot is −$4.00");
    expect(detail?.fields).toEqual([
      { id: "leg-id", label: "Leg ID", value: "leg-1", tone: "muted" },
      { id: "quantity", label: "Quantity", value: "1", tone: "default" },
      { id: "strike", label: "Strike", value: "$100.00", tone: "default" },
      { id: "premium", label: "Premium", value: "$4.00", tone: "default" },
      { id: "break-even", label: "Break-even", value: "$104.00", tone: "default" },
      { id: "payoff-at-spot", label: "Payoff at spot", value: "−$4.00", tone: "danger" }
    ]);
  });

  it("keeps selected-leg detail empty state specific to empty and populated canvases", () => {
    expect(buildSelectedLegDetailViewModel(null, 100, 0)).toBeNull();
    expect(buildSelectedLegDetailEmptyState(0)).toMatchObject({
      title: "No legs on canvas",
      ariaLabel: "Strategy leg detail empty state"
    });
    expect(buildSelectedLegDetailEmptyState(2)).toMatchObject({
      title: "No leg selected",
      description: "Select a canvas leg to inspect payoff contribution, break-even, and editable parameters."
    });
  });

  it("requires confirmation before clearing a populated canvas", () => {
    const { result } = renderHook(() => useStrategyDesignerViewModel([longCall100, shortCall110]));

    expect(result.current.clearCanvasCommand).toMatchObject({
      label: "Clear canvas",
      ariaLabel: "Clear strategy canvas. Press again to confirm removing 2 legs.",
      confirmationPending: false
    });

    act(() => {
      result.current.clearCanvas();
    });

    expect(result.current.legs).toHaveLength(2);
    expect(result.current.clearCanvasCommand).toMatchObject({
      label: "Confirm clear",
      ariaLabel: "Confirm clear strategy canvas and remove 2 legs",
      confirmationPending: true
    });

    act(() => {
      result.current.clearCanvas();
    });

    expect(result.current.legs).toHaveLength(0);
    expect(result.current.clearCanvasCommand).toMatchObject({
      disabled: true,
      disabledReason: "No strategy legs to clear.",
      confirmationPending: false
    });
  });

  it("clears pending canvas confirmation when the operator edits the strategy", () => {
    const { result } = renderHook(() => useStrategyDesignerViewModel([longCall100]));

    act(() => {
      result.current.clearCanvas();
    });
    expect(result.current.clearCanvasCommand.confirmationPending).toBe(true);

    act(() => {
      result.current.addLegFromPalette("long-put");
    });

    expect(result.current.legs).toHaveLength(2);
    expect(result.current.clearCanvasCommand).toMatchObject({
      label: "Clear canvas",
      confirmationPending: false
    });
  });

  it("validates state cell requires exitCondition and stateLabel parameters", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const invalid = {
      ...document,
      cells: [
        ...document.cells,
        {
          cellId: "monitoring-state",
          label: "Monitoring state",
          kind: "state" as const,
          purpose: "state" as const,
          source: "entry phase",
          fieldRefs: [],
          parameters: { stateLabel: "Monitoring" }
        } satisfies StrategyBuilderCell
      ]
    };

    const messages = validateStrategyBuilderDocument(invalid);
    const codes = messages.map((m) => m.code);

    expect(codes).toContain("StateCellExitRequired");
    expect(codes).not.toContain("StateCellLabelRequired");
  });

  it("treats whitespace-only state parameters as missing", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const invalid = {
      ...document,
      cells: [
        ...document.cells,
        {
          cellId: "monitoring-state",
          label: "Monitoring state",
          kind: "state" as const,
          purpose: "state" as const,
          source: "entry phase",
          fieldRefs: [],
          parameters: { stateLabel: "   ", exitCondition: "   " }
        } satisfies StrategyBuilderCell
      ]
    };

    const codes = validateStrategyBuilderDocument(invalid).map((m) => m.code);

    expect(codes).toContain("StateCellExitRequired");
    expect(codes).toContain("StateCellLabelRequired");
  });

  it("validates concurrent cell requires valid semantics", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const invalid = {
      ...document,
      cells: [
        ...document.cells,
        {
          cellId: "parallel-check",
          label: "Parallel check",
          kind: "concurrent" as const,
          purpose: "concurrent" as const,
          source: "parallel evaluation",
          fieldRefs: [],
          parameters: { branchIds: "cell-a,cell-b", semantics: "bad-value" }
        } satisfies StrategyBuilderCell
      ]
    };

    const messages = validateStrategyBuilderDocument(invalid);

    expect(messages.map((m) => m.code)).toContain("ConcurrentCellSemanticsRequired");
  });

  it("validates concurrent cell branch IDs resolve to existing cells", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const invalid = {
      ...document,
      cells: [
        ...document.cells,
        {
          cellId: "parallel-check",
          label: "Parallel check",
          kind: "concurrent" as const,
          purpose: "concurrent" as const,
          source: "parallel evaluation",
          fieldRefs: [],
          parameters: { branchIds: "rank,missing-cell", semantics: "all-pass" }
        } satisfies StrategyBuilderCell
      ]
    };

    const codes = validateStrategyBuilderDocument(invalid).map((m) => m.code);

    expect(codes).toContain("ConcurrentCellBranchMissing");
  });

  it("passes validation for a fully populated universe-builder cell", () => {
    const document = loadStrategyBuilderTemplate("structured-universe-strategy");
    const messages = validateStrategyBuilderDocument(document);
    const errors = messages.filter((m) => m.severity === "error");

    expect(errors).toHaveLength(0);
  });

  it("validates universe-builder rule field references and size constraints", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const invalid = {
      ...document,
      cells: [
        ...document.cells,
        {
          cellId: "ub",
          label: "Universe builder",
          kind: "universe-builder" as const,
          purpose: "universe" as const,
          source: "build universe",
          fieldRefs: ["PRICE"],
          parameters: {
            assetClass: "Equity",
            includeRules: "UNKNOWN_FIELD > 10",
            excludeRules: "VOLATILITY_20D > 0.2",
            minSize: "101",
            maxSize: "100"
          }
        } satisfies StrategyBuilderCell
      ]
    };

    const codes = validateStrategyBuilderDocument(invalid).map((m) => m.code);

    expect(codes).toContain("UnknownField");
    expect(codes).toContain("UniverseBuilderSizeRangeInvalid");
  });

  it("validates trade cell requires instrument, direction, and sizingMethod", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const invalid = {
      ...document,
      cells: [
        ...document.cells,
        {
          cellId: "trade-cell",
          label: "Buy equities",
          kind: "trade" as const,
          purpose: "trade" as const,
          source: "execute trade",
          fieldRefs: [],
          parameters: {}
        } satisfies StrategyBuilderCell
      ]
    };

    const messages = validateStrategyBuilderDocument(invalid);
    const codes = messages.map((m) => m.code);

    expect(codes).toContain("TradeCellInstrumentRequired");
    expect(codes).toContain("TradeCellDirectionRequired");
    expect(codes).toContain("TradeCellSizingRequired");
  });

  it("requires numeric sizingValue for fixed trade sizing methods", () => {
    const document = loadStrategyBuilderTemplate("equity-momentum-breakout");
    const invalid = {
      ...document,
      cells: [
        ...document.cells,
        {
          cellId: "trade-cell",
          label: "Buy equities",
          kind: "trade" as const,
          purpose: "trade" as const,
          source: "execute trade",
          fieldRefs: [],
          parameters: {
            instrument: "equity",
            direction: "buy",
            sizingMethod: "FixedShares",
            sizingValue: "not-a-number"
          }
        } satisfies StrategyBuilderCell
      ]
    };

    const messages = validateStrategyBuilderDocument(invalid);
    const codes = messages.map((m) => m.code);

    expect(codes).toContain("TradeCellSizingValueRequired");
    expect(codes).not.toContain("TradeCellInstrumentRequired");
    expect(codes).not.toContain("TradeCellDirectionRequired");
  });

  it("typed parameter extractors return null for wrong kind or missing parameters", () => {
    const formulaCell: StrategyBuilderCell = {
      cellId: "c",
      label: "C",
      kind: "formula",
      purpose: "rank",
      source: "PRICE",
      fieldRefs: []
    };

    expect(getStateCellParams(formulaCell)).toBeNull();
    expect(getTradeCellParams(formulaCell)).toBeNull();
    expect(getConcurrentCellParams(formulaCell)).toBeNull();
    expect(getUniverseBuilderCellParams(formulaCell)).toBeNull();
  });

  it("typed extractors canonicalize concurrent and trade enums case-insensitively", () => {
    const concurrentCell: StrategyBuilderCell = {
      cellId: "concurrent",
      label: "Concurrent",
      kind: "concurrent",
      purpose: "concurrent",
      source: "parallel",
      fieldRefs: [],
      parameters: { branchIds: "a,b", semantics: "ALL-PASS" }
    };
    const tradeCell: StrategyBuilderCell = {
      cellId: "trade",
      label: "Trade",
      kind: "trade",
      purpose: "trade",
      source: "trade",
      fieldRefs: [],
      parameters: { instrument: "equity", direction: "buy", sizingMethod: "equalweight" }
    };

    expect(getConcurrentCellParams(concurrentCell)?.semantics).toBe("all-pass");
    expect(getTradeCellParams(tradeCell)?.instrument).toBe("Equity");
    expect(getTradeCellParams(tradeCell)?.direction).toBe("Buy");
    expect(getTradeCellParams(tradeCell)?.sizingMethod).toBe("EqualWeight");
  });

  it("getStateCellParams returns null when required keys are absent", () => {
    const stateCell: StrategyBuilderCell = {
      cellId: "s",
      label: "S",
      kind: "state",
      purpose: "state",
      source: "phase",
      fieldRefs: [],
      parameters: { stateLabel: "Watching" }
    };

    expect(getStateCellParams(stateCell)).toBeNull();
  });

  it("four new cell-kind templates appear in the template gallery", () => {
    const templates = getStrategyBuilderTemplates();
    const ids = templates.map((t) => t.templateId);

    expect(ids).toContain("state-machine-strategy");
    expect(ids).toContain("concurrent-branch-strategy");
    expect(ids).toContain("structured-universe-strategy");
    expect(ids).toContain("trade-intent-strategy");
  });

  it("buildStrategyBuilderWorkbenchViewModel surfaces typed trade parameters in inspector fields", () => {
    const document = loadStrategyBuilderTemplate("trade-intent-strategy");
    const tradeCell = document.cells.find((c) => c.kind === "trade");
    const vm = buildStrategyBuilderWorkbenchViewModel({
      document,
      selectedCellId: tradeCell?.cellId
    });

    const fields = vm.selectedCellDetail?.fields ?? [];

    expect(fields.some((f) => f.id === "instrument")).toBe(true);
    expect(fields.some((f) => f.id === "direction")).toBe(true);
    expect(fields.some((f) => f.id === "sizing-method")).toBe(true);
  });

  it("buildStrategyBuilderWorkbenchViewModel surfaces state cell parameters in inspector", () => {
    const document = loadStrategyBuilderTemplate("state-machine-strategy");
    const stateCell = document.cells.find((c) => c.kind === "state");
    const vm = buildStrategyBuilderWorkbenchViewModel({
      document,
      selectedCellId: stateCell?.cellId
    });

    const fields = vm.selectedCellDetail?.fields ?? [];

    expect(fields.some((f) => f.id === "state-label")).toBe(true);
    expect(fields.some((f) => f.id === "exit-condition")).toBe(true);
  });

  it("cloneStrategyBuilderDocument deep-copies parameters so mutations do not leak", () => {
    const document = loadStrategyBuilderTemplate("trade-intent-strategy");
    const tradeCell = document.cells.find((c) => c.kind === "trade")!;
    tradeCell.parameters!["instrument"] = "Future";
    tradeCell.parameters!["sizingValue"] = "999";
    expect(tradeCell.parameters?.["instrument"]).toBe("Future");
    expect(tradeCell.parameters?.["sizingValue"]).toBe("999");

    const original = loadStrategyBuilderTemplate("trade-intent-strategy");
    expect(original.cells.find((c) => c.kind === "trade")?.parameters?.["instrument"]).toBe("Equity");
    expect(original.cells.find((c) => c.kind === "trade")?.parameters?.["sizingValue"]).toBe("0.05");
  });
});
