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
}

export interface StrategyDesignerSpotPriceFieldViewModel {
  label: string;
  value: string;
  min: number;
  step: string;
  inputMode: "decimal";
  ariaLabel: string;
}

export interface StrategyCanvasLegViewModel extends StrategyLeg {
  isSelected: boolean;
  directionTone: "success" | "warning";
  isOption: boolean;
  fieldIds: {
    direction: string;
    quantity: string;
    strike: string;
    premium: string;
  };
  containerAriaLabel: string;
  selectButtonLabel: string;
  selectButtonAriaLabel: string;
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
}

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
      fieldIds: {
        direction: `${fieldIdPrefix}-direction`,
        quantity: `${fieldIdPrefix}-quantity`,
        strike: `${fieldIdPrefix}-strike`,
        premium: `${fieldIdPrefix}-premium`
      },
      containerAriaLabel: `${leg.label}, ${leg.direction} ${leg.instrument}, leg ${ordinal} of ${legs.length}`,
      selectButtonLabel: isSelected ? "Selected" : "Select",
      selectButtonAriaLabel: `${isSelected ? "Selected" : "Select"} ${leg.label}`,
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

  const setSpotPrice = useCallback((price: number) => {
    if (!Number.isFinite(price) || price < 0) return;
    setSpotPriceState(price);
    setSpotPriceDraft(price.toString());
  }, []);

  const updateSpotPriceDraft = useCallback((value: string) => {
    setSpotPriceDraft(value);
  }, []);

  const commitSpotPriceDraft = useCallback((value?: string) => {
    const nextDraft = value ?? spotPriceDraft;
    const parsed = Number.parseFloat(nextDraft);
    if (Number.isFinite(parsed) && parsed >= 0) {
      setSpotPriceState(parsed);
      setSpotPriceDraft(parsed.toString());
      return;
    }

    setSpotPriceDraft(spotPrice.toString());
  }, [spotPrice, spotPriceDraft]);

  const addLegFromPalette = useCallback((kind: StrategyLegKind): string => {
    const entry = PALETTE.find((item) => item.kind === kind);
    if (!entry) return "";

    const next = buildLegFromPalette(entry, legs);
    setLegs((current) => [...current, next]);
    setSelectedLegId(next.id);
    return next.id;
  }, [legs]);

  const removeLeg = useCallback((id: string) => {
    setLegs((current) => current.filter((leg) => leg.id !== id));
    setSelectedLegId((current) => (current === id ? null : current));
  }, []);

  const updateLeg = useCallback(
    (id: string, patch: Partial<Pick<StrategyLeg, "quantity" | "strike" | "premium" | "direction">>) => {
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
    []
  );

  const reorderLeg = useCallback((sourceId: string, targetId: string) => {
    setLegs((current) => reorderLegs(current, sourceId, targetId));
  }, []);

  const clearCanvas = useCallback(() => {
    setLegs([]);
    setSelectedLegId(null);
  }, []);

  const loadSample = useCallback(() => {
    const sample = getStrategyDesignerSampleLegs();
    setLegs(sample);
    setSelectedLegId(sample[0]?.id ?? null);
  }, []);

  const selectLeg = useCallback((id: string | null) => {
    setSelectedLegId(id);
  }, []);

  const payoff = useMemo(() => buildPayoffChartViewModel(legs, spotPrice), [legs, spotPrice]);
  const participation = useMemo(() => buildParticipationViewModel(legs, spotPrice), [legs, spotPrice]);
  const metrics = useMemo(() => buildDesignerSummaryMetrics(payoff, participation), [payoff, participation]);
  const canvasLegs = useMemo(() => buildCanvasLegViewModels(legs, selectedLegId), [legs, selectedLegId]);
  const selectedLeg = useMemo(
    () => legs.find((leg) => leg.id === selectedLegId) ?? null,
    [legs, selectedLegId]
  );
  const clearCanvasDisabled = legs.length === 0;

  return {
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
      label: "Clear canvas",
      ariaLabel: "Clear strategy canvas",
      disabled: clearCanvasDisabled,
      disabledReason: clearCanvasDisabled ? "No strategy legs to clear." : null
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
    selectedLeg
  };
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
