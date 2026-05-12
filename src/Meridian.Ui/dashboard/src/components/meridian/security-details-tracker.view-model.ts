import type { EntitySummaryField } from "@/components/meridian/ui-kit-primitives";

export interface SecurityLot {
  lotId: string;
  tradeDate: string;
  quantity: number;
  price: number;
  fees: number;
  note: string;
}

export interface LotDraftInput {
  tradeDate: string;
  quantity: string;
  price: string;
  fees: string;
  note: string;
}

export interface LotsTrackerMetricViewModel {
  id: string;
  label: string;
  value: string;
  tone?: "success" | "danger";
}

export interface LotsTrackerRowViewModel {
  lotId: string;
  tradeDateLabel: string;
  quantityLabel: string;
  priceLabel: string;
  feesLabel: string;
  costLabel: string;
  noteLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  removeAriaLabel: string;
  detailPanelId: string;
  selected: boolean;
  expanded: boolean;
}

export interface LotsTrackerDetailViewModel {
  panelId: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "danger";
  fields: EntitySummaryField[];
  ariaLabel: string;
}

export interface LotsTrackerAddCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
}

export interface LotsTrackerViewModel {
  title: string;
  description: string;
  addCommand: LotsTrackerAddCommandViewModel;
  metrics: LotsTrackerMetricViewModel[];
  rows: LotsTrackerRowViewModel[];
  selectedLotId: string | null;
  selectedDetail: LotsTrackerDetailViewModel | null;
  tableLabel: string;
  tableCaption: string;
  emptyText: string;
}

export interface BuildLotsTrackerViewModelInput {
  securityId: string;
  currency: string | null;
  lots: readonly SecurityLot[];
  marketPriceOverride: number | null;
  draft: LotDraftInput;
  selectedLotId: string | null;
}

export function buildLotsTrackerViewModel({
  securityId,
  currency,
  lots,
  marketPriceOverride,
  draft,
  selectedLotId
}: BuildLotsTrackerViewModelInput): LotsTrackerViewModel {
  const selectedLot = lots.find((lot) => lot.lotId === selectedLotId) ?? lots[0] ?? null;
  const resolvedSelectedLotId = selectedLot?.lotId ?? null;
  const detailPanelId = `security-lots-detail-${stableDomId(securityId)}`;
  const total = buildLotTotals(lots, marketPriceOverride);
  const rows = lots.map((lot) => buildLotRow(lot, currency, securityId, detailPanelId, lot.lotId === resolvedSelectedLotId));

  return {
    title: "Lots tracker",
    description: `Record purchase lots for ${securityId} to track quantity, cost basis, and unrealised P/L. Lots are stored locally per security.`,
    addCommand: buildAddCommand(draft),
    metrics: [
      { id: "quantity", label: "Total quantity", value: formatNumber(total.qty) },
      { id: "average-cost", label: "Average cost", value: formatCurrency(total.avgCost, currency) },
      { id: "total-cost", label: "Total cost (incl. fees)", value: formatCurrency(total.totalCostWithFees, currency) },
      {
        id: "unrealised-pnl",
        label: "Unrealised P/L",
        value: total.unrealisedPnl == null ? "-" : formatCurrency(total.unrealisedPnl, currency),
        tone: total.unrealisedPnl == null ? undefined : total.unrealisedPnl >= 0 ? "success" : "danger"
      }
    ],
    rows,
    selectedLotId: resolvedSelectedLotId,
    selectedDetail: selectedLot ? buildLotDetail(selectedLot, currency, securityId, detailPanelId) : null,
    tableLabel: `Lots for ${securityId}`,
    tableCaption: `Recorded purchase lots for ${securityId}`,
    emptyText: "No lots recorded yet. Add a lot above to start tracking cost basis."
  };
}

function buildAddCommand(draft: LotDraftInput): LotsTrackerAddCommandViewModel {
  const quantity = parseDraftNumber(draft.quantity);
  const price = parseDraftNumber(draft.price);
  const fees = draft.fees.trim() === "" ? 0 : parseDraftNumber(draft.fees);
  const disabledReason =
    draft.tradeDate.trim() === ""
      ? "Trade date is required."
      : draft.quantity.trim() === ""
        ? "Quantity is required."
        : quantity === null || quantity === 0
          ? "Quantity must be a non-zero number."
          : draft.price.trim() === ""
            ? "Price is required."
            : price === null || price <= 0
              ? "Price must be greater than zero."
              : fees === null
                ? "Fees must be a number."
                : null;

  return {
    label: "Add lot",
    ariaLabel: disabledReason ? `Add lot unavailable: ${disabledReason}` : "Add lot",
    disabled: disabledReason !== null,
    disabledReason
  };
}

function buildLotRow(
  lot: SecurityLot,
  currency: string | null,
  securityId: string,
  detailPanelId: string,
  selected: boolean
): LotsTrackerRowViewModel {
  const cost = calculateLotCost(lot);
  const noteLabel = lot.note.trim() || "-";
  return {
    lotId: lot.lotId,
    tradeDateLabel: lot.tradeDate,
    quantityLabel: formatNumber(lot.quantity),
    priceLabel: formatCurrency(lot.price, currency),
    feesLabel: formatCurrency(lot.fees, currency),
    costLabel: formatCurrency(cost, currency),
    noteLabel,
    ariaLabel: `${securityId} lot from ${lot.tradeDate}, quantity ${formatNumber(lot.quantity)}, cost ${formatCurrency(cost, currency)}`,
    selectAriaLabel: `Inspect ${securityId} lot from ${lot.tradeDate}`,
    removeAriaLabel: `Remove ${securityId} lot from ${lot.tradeDate}`,
    detailPanelId,
    selected,
    expanded: selected
  };
}

function buildLotDetail(
  lot: SecurityLot,
  currency: string | null,
  securityId: string,
  panelId: string
): LotsTrackerDetailViewModel {
  const cost = calculateLotCost(lot);
  const side = lot.quantity < 0 ? "Short lot" : "Long lot";
  return {
    panelId,
    eyebrow: "Selected lot",
    title: `${securityId} · ${lot.tradeDate}`,
    subtitle: side,
    description: lot.note.trim() || "No operator note recorded for this lot.",
    statusLabel: lot.quantity < 0 ? "Short" : "Long",
    statusBadgeVariant: lot.quantity < 0 ? "danger" : "success",
    fields: [
      { label: "Lot ID", value: lot.lotId },
      { label: "Quantity", value: formatNumber(lot.quantity) },
      { label: "Price", value: formatCurrency(lot.price, currency) },
      { label: "Fees", value: formatCurrency(lot.fees, currency) },
      { label: "Cost basis", value: formatCurrency(cost, currency) },
      { label: "Trade date", value: lot.tradeDate }
    ],
    ariaLabel: `Selected lot detail for ${securityId}`
  };
}

function buildLotTotals(lots: readonly SecurityLot[], marketPriceOverride: number | null) {
  let qty = 0;
  let cost = 0;
  let fees = 0;
  for (const lot of lots) {
    qty += lot.quantity;
    cost += lot.quantity * lot.price;
    fees += lot.fees;
  }
  const totalCostWithFees = cost + fees;
  const avgCost = qty !== 0 ? totalCostWithFees / qty : 0;
  const marketValue = marketPriceOverride != null && Number.isFinite(marketPriceOverride) ? marketPriceOverride * qty : null;
  const unrealisedPnl = marketValue != null ? marketValue - totalCostWithFees : null;
  return { qty, cost, fees, totalCostWithFees, avgCost, marketValue, unrealisedPnl };
}

function calculateLotCost(lot: SecurityLot): number {
  return lot.quantity * lot.price + lot.fees;
}

export function parseLotNumber(value: string): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : 0;
}

function parseDraftNumber(value: string): number | null {
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

export function formatNumber(value: number, fractionDigits = 4): string {
  if (!Number.isFinite(value)) return "-";
  return value.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: fractionDigits });
}

export function formatCurrency(value: number, currency: string | null): string {
  if (!Number.isFinite(value)) return "-";
  const text = value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return currency ? `${text} ${currency}` : text;
}

function stableDomId(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "") || "selected";
}
