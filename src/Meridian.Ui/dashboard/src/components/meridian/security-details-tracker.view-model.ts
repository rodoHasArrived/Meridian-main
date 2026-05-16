import type { EntitySummaryField } from "@/components/meridian/ui-kit-primitives";
import type { SecurityIdentityDrillIn, SecurityMasterEntry, TradingParameters } from "@/types";

export type SecurityDetailFieldKind = "text" | "number" | "date" | "select" | "boolean";

export interface SecurityDetailFieldDef {
  key: string;
  label: string;
  kind: SecurityDetailFieldKind;
  options?: readonly string[];
  placeholder?: string;
  derive?: (ctx: SecurityDetailContext) => string | null | undefined;
  format?: (value: string) => string;
}

export interface SecurityDetailGroupDef {
  id: string;
  title: string;
  fields: readonly SecurityDetailFieldDef[];
}

export interface SecurityDetailContext {
  entry: SecurityMasterEntry | null;
  identity: SecurityIdentityDrillIn | null;
  tradingParameters: TradingParameters | null;
}

export interface SecurityDetailFieldViewModel {
  def: SecurityDetailFieldDef;
  key: string;
  label: string;
  value: string;
  displayValue: string;
  isOverridden: boolean;
  overrideValue: string | undefined;
}

export interface SecurityDetailGroupViewModel {
  id: string;
  title: string;
  fields: SecurityDetailFieldViewModel[];
}

export interface SecurityDetailsViewModel {
  assetClass: string | null;
  assetClassVisibilityKey: string | null;
  groups: SecurityDetailGroupViewModel[];
  overrideCount: number;
  hiddenOverrideCount: number;
}

export interface BuildSecurityDetailsViewModelInput {
  entry: SecurityMasterEntry | null;
  identity: SecurityIdentityDrillIn | null;
  tradingParameters: TradingParameters | null;
  overrides: Record<string, string>;
}

const RATING_OPTIONS = [
  "AAA", "AA+", "AA", "AA-", "A+", "A", "A-",
  "BBB+", "BBB", "BBB-", "BB+", "BB", "BB-",
  "B+", "B", "B-", "CCC+", "CCC", "CCC-", "CC", "C", "D",
  "NR"
] as const;

const MOODYS_RATING_OPTIONS = [
  "Aaa", "Aa1", "Aa2", "Aa3", "A1", "A2", "A3",
  "Baa1", "Baa2", "Baa3", "Ba1", "Ba2", "Ba3",
  "B1", "B2", "B3", "Caa1", "Caa2", "Caa3", "Ca", "C",
  "NR"
] as const;

const COUPON_TYPE_OPTIONS = [
  "Fixed", "Floating", "Variable", "Step-Up", "Zero Coupon", "Inflation-Linked", "PIK"
] as const;

const COUPON_FREQUENCY_OPTIONS = [
  "Annual", "Semi-Annual", "Quarterly", "Monthly", "At Maturity", "Irregular"
] as const;

const DAY_COUNT_OPTIONS = [
  "30/360", "30E/360", "ACT/360", "ACT/365", "ACT/ACT", "NL/365"
] as const;

const PAY_CONVENTION_OPTIONS = [
  "Following", "Modified Following", "Preceding", "Modified Preceding", "Unadjusted"
] as const;

const ASSET_CLASS_OPTIONS = [
  "Equity", "FixedIncome", "Future", "Option", "Fund", "Currency", "Commodity", "Crypto", "Index"
] as const;

const yesNo = ["Yes", "No"] as const;

function pickIdentifier(identity: SecurityIdentityDrillIn | null, kind: string): string | null {
  if (!identity) return null;
  const exact = identity.identifiers.find((i) => i.kind.toLowerCase() === kind.toLowerCase());
  if (exact) return exact.value;
  const alias = identity.aliases.find((a) => a.aliasKind.toLowerCase() === kind.toLowerCase());
  return alias?.aliasValue ?? null;
}

export const SECURITY_DETAIL_GROUPS: readonly SecurityDetailGroupDef[] = [
  {
    id: "identification",
    title: "Identification",
    fields: [
      { key: "identifier", label: "Identifier", kind: "text", derive: (c) => c.identity?.securityId ?? c.entry?.securityId ?? null },
      { key: "description", label: "Description", kind: "text", derive: (c) => c.identity?.displayName ?? c.entry?.displayName ?? null },
      { key: "ticker", label: "Ticker", kind: "text", derive: (c) => pickIdentifier(c.identity, "Ticker") },
      { key: "isin", label: "ISIN", kind: "text", derive: (c) => pickIdentifier(c.identity, "ISIN") },
      { key: "cusip", label: "CUSIP", kind: "text", derive: (c) => pickIdentifier(c.identity, "CUSIP") },
      { key: "sedol", label: "SEDOL", kind: "text", derive: (c) => pickIdentifier(c.identity, "SEDOL") },
      { key: "figi", label: "FIGI", kind: "text", derive: (c) => pickIdentifier(c.identity, "FIGI") },
      { key: "securityId", label: "Security ID", kind: "text", derive: (c) => c.identity?.securityId ?? c.entry?.securityId ?? null }
    ]
  },
  {
    id: "issuer",
    title: "Issuer & Concentration",
    fields: [
      { key: "issuer", label: "Issuer", kind: "text" },
      { key: "issuerConcentration", label: "Issuer Concentration (%)", kind: "number", placeholder: "0.00" },
      { key: "servicer", label: "Servicer", kind: "text" },
      { key: "series", label: "Series", kind: "text" },
      { key: "shareClass", label: "Share Class", kind: "text" },
      { key: "sharesOutstanding", label: "Shares Outstanding", kind: "number" }
    ]
  },
  {
    id: "classification",
    title: "Classification",
    fields: [
      { key: "assetClass", label: "Asset Class", kind: "select", options: ASSET_CLASS_OPTIONS, derive: (c) => c.identity?.assetClass ?? c.entry?.classification.assetClass ?? null },
      { key: "securityType", label: "Security Type", kind: "text", derive: (c) => c.entry?.classification.subType ?? null },
      { key: "securityTypeCategory", label: "Security Type Category", kind: "text" },
      { key: "marketSector", label: "Market Sector", kind: "text" },
      { key: "industrySector", label: "Industry Sector", kind: "text" },
      { key: "industryGroup", label: "Industry Group", kind: "text" },
      { key: "industrySubgroup", label: "Industry Subgroup", kind: "text" },
      { key: "sicCode", label: "SIC Code", kind: "text" },
      { key: "sicCodeDescription", label: "SIC Code Description", kind: "text" }
    ]
  },
  {
    id: "geography",
    title: "Geography & Currency",
    fields: [
      { key: "countryOfHeadquarters", label: "Country of Headquarters", kind: "text" },
      { key: "countryOfIncorporation", label: "Country of Incorporation", kind: "text" },
      { key: "countryOfIssue", label: "Country of Issue", kind: "text" },
      { key: "countryOfGovGuarantee", label: "Country of Gov Guarantee", kind: "text" },
      { key: "currency", label: "Currency", kind: "text", derive: (c) => c.entry?.economicDefinition.currency ?? null }
    ]
  },
  {
    id: "ratings",
    title: "Ratings",
    fields: [
      { key: "spRating", label: "S&P Rating", kind: "select", options: RATING_OPTIONS },
      { key: "moodysRating", label: "Moody's Rating", kind: "select", options: MOODYS_RATING_OPTIONS },
      { key: "fitchRating", label: "Fitch Rating", kind: "select", options: RATING_OPTIONS },
      { key: "simpleCreditRating", label: "Simple Credit Rating", kind: "select", options: RATING_OPTIONS },
      { key: "impliedRatingsEligible", label: "Implied Ratings Eligible", kind: "select", options: yesNo }
    ]
  },
  {
    id: "coupon",
    title: "Coupon",
    fields: [
      { key: "couponRate", label: "Coupon Rate (%)", kind: "number" },
      { key: "couponType", label: "Coupon Type", kind: "select", options: COUPON_TYPE_OPTIONS },
      { key: "couponFrequency", label: "Coupon Frequency", kind: "select", options: COUPON_FREQUENCY_OPTIONS },
      { key: "couponCurrency", label: "Coupon Currency", kind: "text" },
      { key: "couponCap", label: "Coupon Cap (%)", kind: "number" },
      { key: "couponFloor", label: "Coupon Floor (%)", kind: "number" },
      { key: "couponEffectiveDate", label: "Coupon Effective Date", kind: "date" },
      { key: "couponPayConvention", label: "Coupon Pay Convention", kind: "select", options: PAY_CONVENTION_OPTIONS },
      { key: "couponPayDay", label: "Coupon Pay Day", kind: "text" },
      { key: "couponResetFrequency", label: "Coupon Reset Frequency", kind: "select", options: COUPON_FREQUENCY_OPTIONS },
      { key: "dayCount", label: "Day Count", kind: "select", options: DAY_COUNT_OPTIONS }
    ]
  },
  {
    id: "maturity",
    title: "Maturity & Factor",
    fields: [
      { key: "finalMaturity", label: "Final Maturity", kind: "date" },
      { key: "factor", label: "Factor", kind: "number" },
      { key: "factorEffectiveDate", label: "Factor Effective Date", kind: "date" },
      { key: "sinkable", label: "Sinkable", kind: "select", options: yesNo },
      { key: "sequential", label: "Sequential", kind: "select", options: yesNo },
      { key: "continuouslyCallable", label: "Continuously Callable", kind: "select", options: yesNo },
      { key: "coveredBond", label: "Covered Bond", kind: "select", options: yesNo }
    ]
  },
  {
    id: "pricing",
    title: "Pricing & Risk",
    fields: [
      { key: "marketPrice", label: "Market Price", kind: "number" },
      { key: "yield", label: "Yield (%)", kind: "number" },
      { key: "duration", label: "Duration", kind: "number" },
      { key: "convexity", label: "Convexity", kind: "number" },
      { key: "convexityGroup", label: "Convexity Group", kind: "text" },
      { key: "contractSize", label: "Contract Size", kind: "number" }
    ]
  },
  {
    id: "conversion",
    title: "Conversion (Convertibles)",
    fields: [
      { key: "convertible", label: "Convertible", kind: "select", options: yesNo },
      { key: "conversionOption", label: "Conversion Option", kind: "text" },
      { key: "conversionOptionEndDate", label: "Conversion Option End Date", kind: "date" },
      { key: "conversionPrice", label: "Conversion Price", kind: "number" },
      { key: "conversionRatio", label: "Conversion Ratio", kind: "number" }
    ]
  },
  {
    id: "regulatory",
    title: "Regulatory & Tax",
    fields: [
      { key: "fdicNumber", label: "FDIC Number", kind: "text" },
      { key: "fedTax", label: "Fed Tax", kind: "select", options: ["Taxable", "Tax-Exempt", "AMT"] }
    ]
  }
] as const;

const ALWAYS_VISIBLE_GROUPS = new Set(["identification", "classification", "geography"]);
const EQUITY_FUND_VISIBLE_FIELDS = new Set([
  "issuer", "shareClass", "sharesOutstanding", "marketPrice", "fedTax"
]);
const FIXED_INCOME_VISIBLE_FIELDS = new Set([
  "issuer", "issuerConcentration", "servicer", "series",
  "spRating", "moodysRating", "fitchRating", "simpleCreditRating", "impliedRatingsEligible",
  "couponRate", "couponType", "couponFrequency", "couponCurrency", "couponCap", "couponFloor",
  "couponEffectiveDate", "couponPayConvention", "couponPayDay", "couponResetFrequency", "dayCount",
  "finalMaturity", "factor", "factorEffectiveDate", "sinkable", "sequential", "continuouslyCallable", "coveredBond",
  "marketPrice", "yield", "duration", "convexity", "convexityGroup",
  "convertible", "conversionOption", "conversionOptionEndDate", "conversionPrice", "conversionRatio",
  "fdicNumber", "fedTax"
]);
const TRADING_ASSET_VISIBLE_FIELDS = new Set(["marketPrice", "contractSize"]);

export function buildSecurityDetailsViewModel({
  entry,
  identity,
  tradingParameters,
  overrides
}: BuildSecurityDetailsViewModelInput): SecurityDetailsViewModel {
  const ctx: SecurityDetailContext = { entry, identity, tradingParameters };
  const assetClass = resolveAssetClass(ctx, overrides);
  const assetClassVisibilityKey = normalizeAssetClass(assetClass);
  const visibleKeys = new Set<string>();
  const groups = SECURITY_DETAIL_GROUPS
    .map((group) => {
      const visibleFields = group.fields
        .filter((field) => isFieldVisible(group, field, assetClassVisibilityKey))
        .map((field) => buildSecurityDetailFieldViewModel(field, ctx, overrides));
      for (const field of visibleFields) {
        visibleKeys.add(field.key);
      }
      return { id: group.id, title: group.title, fields: visibleFields };
    })
    .filter((group) => group.fields.length > 0);

  return {
    assetClass,
    assetClassVisibilityKey,
    groups,
    overrideCount: Object.keys(overrides).length,
    hiddenOverrideCount: Object.keys(overrides).filter((key) => !visibleKeys.has(key)).length
  };
}

function resolveAssetClass(ctx: SecurityDetailContext, overrides: Record<string, string>): string | null {
  return ctx.identity?.assetClass
    ?? ctx.entry?.classification.assetClass
    ?? normalizeBlank(overrides.assetClass);
}

function normalizeBlank(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

function normalizeAssetClass(value: string | null): string | null {
  const normalized = value?.trim().toLowerCase().replace(/[\s_-]+/g, "");
  if (!normalized) return null;
  if (normalized === "fixedincome" || normalized === "fixedincomes" || normalized === "bond" || normalized === "bonds") return "fixedincome";
  if (normalized === "equity" || normalized === "equities" || normalized === "stock" || normalized === "commonstock") return "equity";
  if (normalized === "fund" || normalized === "funds" || normalized === "etf" || normalized === "mutualfund") return "fund";
  if (normalized === "future" || normalized === "futures") return "future";
  if (normalized === "option" || normalized === "options") return "option";
  if (normalized === "commodity" || normalized === "commodities") return "commodity";
  if (normalized === "currency" || normalized === "currencies" || normalized === "fx") return "currency";
  if (normalized === "crypto" || normalized === "cryptocurrency") return "crypto";
  if (normalized === "index" || normalized === "indices") return "index";
  return "unknown";
}

function isFieldVisible(group: SecurityDetailGroupDef, field: SecurityDetailFieldDef, assetClassKey: string | null): boolean {
  if (assetClassKey === null || assetClassKey === "unknown") return true;
  if (ALWAYS_VISIBLE_GROUPS.has(group.id)) return true;
  if (assetClassKey === "equity" || assetClassKey === "fund") {
    return EQUITY_FUND_VISIBLE_FIELDS.has(field.key);
  }
  if (assetClassKey === "fixedincome") {
    return FIXED_INCOME_VISIBLE_FIELDS.has(field.key);
  }
  return TRADING_ASSET_VISIBLE_FIELDS.has(field.key);
}

function buildSecurityDetailFieldViewModel(
  field: SecurityDetailFieldDef,
  ctx: SecurityDetailContext,
  overrides: Record<string, string>
): SecurityDetailFieldViewModel {
  const derived = field.derive?.(ctx) ?? null;
  const override = overrides[field.key];
  const isOverridden = override !== undefined && override !== "";
  const value = isOverridden ? override : (derived ?? "");
  return {
    def: field,
    key: field.key,
    label: field.label,
    value,
    displayValue: value === "" ? "—" : (field.format ? field.format(value) : value),
    isOverridden,
    overrideValue: override
  };
}

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
  removeLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  removeAriaLabel: string;
  removeConfirmationPending: boolean;
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

export type LotsTrackerDraftFieldKey = "tradeDate" | "quantity" | "price" | "fees" | "note";

export interface LotsTrackerDraftFieldViewModel {
  key: LotsTrackerDraftFieldKey;
  id: string;
  label: string;
  type: "date" | "number" | "text";
  step?: string;
  placeholder?: string;
  helperId: string;
  helperText: string;
  errorId: string;
  errorText: string | null;
  invalid: boolean;
  describedBy: string;
}

export type LotsTrackerDraftFieldsViewModel = Record<LotsTrackerDraftFieldKey, LotsTrackerDraftFieldViewModel>;

export interface LotsTrackerDraftStatusViewModel {
  id: string;
  role: "status";
  tone: "neutral" | "success" | "warning";
  text: string;
}

export interface LotsTrackerViewModel {
  title: string;
  description: string;
  formLabel: string;
  draftFields: LotsTrackerDraftFieldsViewModel;
  draftStatus: LotsTrackerDraftStatusViewModel;
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
  pendingRemoveLotId?: string | null;
}

export function buildLotsTrackerViewModel({
  securityId,
  currency,
  lots,
  marketPriceOverride,
  draft,
  selectedLotId,
  pendingRemoveLotId = null
}: BuildLotsTrackerViewModelInput): LotsTrackerViewModel {
  const selectedLot = lots.find((lot) => lot.lotId === selectedLotId) ?? lots[0] ?? null;
  const resolvedSelectedLotId = selectedLot?.lotId ?? null;
  const detailPanelId = `security-lots-detail-${stableDomId(securityId)}`;
  const total = buildLotTotals(lots, marketPriceOverride);
  const addCommand = buildAddCommand(draft);
  const rows = lots.map((lot) => buildLotRow(
    lot,
    currency,
    securityId,
    detailPanelId,
    lot.lotId === resolvedSelectedLotId,
    lot.lotId === pendingRemoveLotId
  ));

  return {
    title: "Lots tracker",
    description: `Record purchase lots for ${securityId} to track quantity, cost basis, and unrealised P/L. Lots are stored locally per security.`,
    formLabel: `Add purchase lot for ${securityId}`,
    draftFields: buildLotsDraftFields(securityId, draft),
    draftStatus: buildLotsDraftStatus(securityId, addCommand.disabledReason),
    addCommand,
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

function buildLotsDraftFields(securityId: string, draft: LotDraftInput): LotsTrackerDraftFieldsViewModel {
  const prefix = `security-lots-${stableDomId(securityId)}-draft`;
  const tradeDateError = draft.tradeDate.trim() === "" ? "Trade date is required." : null;
  const quantity = parseDraftNumber(draft.quantity);
  const quantityError =
    draft.quantity.trim() === ""
      ? "Quantity is required."
      : quantity === null || quantity === 0
        ? "Quantity must be a non-zero number."
        : null;
  const price = parseDraftNumber(draft.price);
  const priceError =
    draft.price.trim() === ""
      ? "Price is required."
      : price === null || price <= 0
        ? "Price must be greater than zero."
        : null;
  const fees = draft.fees.trim() === "" ? 0 : parseDraftNumber(draft.fees);
  const feesError = fees === null ? "Fees must be a number." : null;

  return {
    tradeDate: buildDraftField({
      key: "tradeDate",
      prefix,
      label: "Trade date",
      type: "date",
      helperText: "Required for lot chronology and selected-lot evidence.",
      errorText: tradeDateError
    }),
    quantity: buildDraftField({
      key: "quantity",
      prefix,
      label: "Quantity",
      type: "number",
      step: "any",
      placeholder: "100",
      helperText: "Use positive quantity for long lots and negative quantity for short lots.",
      errorText: quantityError
    }),
    price: buildDraftField({
      key: "price",
      prefix,
      label: "Price",
      type: "number",
      step: "any",
      placeholder: "0.00",
      helperText: "Execution price per unit; must be greater than zero.",
      errorText: priceError
    }),
    fees: buildDraftField({
      key: "fees",
      prefix,
      label: "Fees",
      type: "number",
      step: "any",
      placeholder: "0.00",
      helperText: "Optional fees are included in total cost basis.",
      errorText: feesError
    }),
    note: buildDraftField({
      key: "note",
      prefix,
      label: "Note",
      type: "text",
      placeholder: "Broker, strategy, etc.",
      helperText: "Optional desk context shown in the selected-lot detail panel.",
      errorText: null
    })
  };
}

function buildDraftField({
  key,
  prefix,
  label,
  type,
  step,
  placeholder,
  helperText,
  errorText
}: {
  key: LotsTrackerDraftFieldKey;
  prefix: string;
  label: string;
  type: LotsTrackerDraftFieldViewModel["type"];
  step?: string;
  placeholder?: string;
  helperText: string;
  errorText: string | null;
}): LotsTrackerDraftFieldViewModel {
  const id = `${prefix}-${kebabCase(key)}`;
  const helperId = `${id}-help`;
  const errorId = `${id}-error`;
  return {
    key,
    id,
    label,
    type,
    step,
    placeholder,
    helperId,
    helperText,
    errorId,
    errorText,
    invalid: errorText !== null,
    describedBy: [helperId, errorText ? errorId : null].filter(Boolean).join(" ")
  };
}

function buildLotsDraftStatus(securityId: string, disabledReason: string | null): LotsTrackerDraftStatusViewModel {
  const id = `security-lots-${stableDomId(securityId)}-draft-status`;
  if (!disabledReason) {
    return {
      id,
      role: "status",
      tone: "success",
      text: "Lot draft is ready to add."
    };
  }

  return {
    id,
    role: "status",
    tone: "warning",
    text: `Add lot unavailable: ${disabledReason}`
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
  selected: boolean,
  removeConfirmationPending: boolean
): LotsTrackerRowViewModel {
  const cost = calculateLotCost(lot);
  const noteLabel = lot.note.trim() || "-";
  const removeLabel = removeConfirmationPending ? "Confirm remove" : "Remove";
  return {
    lotId: lot.lotId,
    tradeDateLabel: lot.tradeDate,
    quantityLabel: formatNumber(lot.quantity),
    priceLabel: formatCurrency(lot.price, currency),
    feesLabel: formatCurrency(lot.fees, currency),
    costLabel: formatCurrency(cost, currency),
    noteLabel,
    removeLabel,
    ariaLabel: `${securityId} lot from ${lot.tradeDate}, quantity ${formatNumber(lot.quantity)}, cost ${formatCurrency(cost, currency)}${removeConfirmationPending ? ". Remove confirmation pending." : ""}`,
    selectAriaLabel: `Inspect ${securityId} lot from ${lot.tradeDate}${removeConfirmationPending ? ". Remove confirmation pending." : ""}`,
    removeAriaLabel: removeConfirmationPending
      ? `Confirm remove ${securityId} lot from ${lot.tradeDate}. This deletes the local cost-basis lot.`
      : `Remove ${securityId} lot from ${lot.tradeDate}`,
    removeConfirmationPending,
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

function kebabCase(value: string): string {
  return value.replace(/[A-Z]/g, (match) => `-${match.toLowerCase()}`);
}
