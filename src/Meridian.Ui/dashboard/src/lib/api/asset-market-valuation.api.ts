import { getJson, type ApiRequestOptions } from "@/lib/api";
import { isTaxDecimalText } from "@/lib/ledger-tax-results-format";
import { workstationAssetMarketValuationEndpoint, type AssetMarketValuationQuery } from "@/lib/workstation-endpoints";
import type { AssetPositionMarketValuation } from "@/types/asset-market-valuation";

function object(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object" && !Array.isArray(value));
}
function fail(message: string): never {
  throw new Error(message);
}

/** Refuse foreign or precision-losing evidence before an amount reaches the UI. */
export async function getAssetMarketValuation(
  securityId: string,
  query: AssetMarketValuationQuery,
  currency: string,
  options: ApiRequestOptions = {}
): Promise<AssetPositionMarketValuation> {
  if (
    ![securityId, query.positionId, query.ledgerBookId, query.accountId, query.economicStateId].every(
      (id) => typeof id === "string" && id.trim()
    ) ||
    !Number.isSafeInteger(query.economicStateVersion) ||
    query.economicStateVersion < 1
  )
    fail("Retained position, book, account and state/version are required for valuation.");
  if (
    !/^\d{4}-\d{2}-\d{2}$/.test(query.valuationDate) ||
    !Number.isFinite(Date.parse(query.valuationDate)) ||
    new Date(query.valuationDate).toISOString().slice(0, 10) !== query.valuationDate
  )
    fail("A valid valuation date is required.");
  const value: unknown = await getJson(workstationAssetMarketValuationEndpoint(securityId, query), {
    ...options,
    allowDevelopmentFallback: false
  });
  if (!object(value)) fail("Market valuation response is unavailable.");
  for (const [key, expected] of Object.entries({
    securityId,
    positionId: query.positionId,
    ledgerBookId: query.ledgerBookId,
    accountId: query.accountId,
    economicStateId: query.economicStateId,
    economicStateVersion: query.economicStateVersion,
    valuationDate: query.valuationDate
  })) {
    if (value[key] !== expected)
      fail("Market valuation does not match the selected asset, book, account, date and retained state.");
  }
  if (!["Ready", "Partial", "Blocked"].includes(value.status as string)) fail("Unknown market valuation status.");
  if (!["Unspecified", "CurrencyPerUnit", "PercentOfPar"].includes(value.priceUnit as string))
    fail("Unknown market price unit.");
  for (const key of ["quantity", "carryingAmount", "price", "marketValue", "unrealizedPnl"]) {
    if (value[key] !== null && !isTaxDecimalText(value[key]))
      fail("Market valuation amounts must be exact decimal strings.");
  }
  if (
    !Array.isArray(value.blockers) ||
    value.blockers.some(
      (item) =>
        !object(item) ||
        typeof item.code !== "string" ||
        typeof item.reason !== "string" ||
        !["All", "UnrealizedPnl", "MarketValue"].includes(item.metric as string)
    )
  )
    fail("Market valuation blockers are malformed.");
  if (
    value.status === "Ready" &&
    (value.marketValue === null || value.unrealizedPnl === null || value.blockers.length > 0)
  )
    fail("Ready market valuation is missing completed metrics.");
  const hasAmounts = value.marketValue !== null || value.unrealizedPnl !== null;
  if (hasAmounts && value.currency !== currency)
    fail("Market valuation currency does not match the retained economic state.");
  if (
    hasAmounts &&
    (value.status === "Blocked" ||
      value.isStaleFallback === true ||
      /stale/i.test(String(value.freshnessStatus)) ||
      value.blockers.some((item) => item.metric === "All"))
  )
    fail("Blocked or stale market valuation cannot supply amounts.");
  if (value.unrealizedPnl !== null && value.blockers.some((item) => item.metric === "UnrealizedPnl"))
    fail("Unavailable P/L cannot supply an amount.");
  if (value.marketValue !== null && value.blockers.some((item) => item.metric === "MarketValue"))
    fail("Unavailable market value cannot supply an amount.");
  for (const key of ["calculationBasis", "unrealizedPnlBasis", "modelKey", "modelVersion", "freshnessStatus"])
    if (typeof value[key] !== "string") fail("Market valuation explanation is missing.");
  if (
    !Array.isArray(value.diagnostics) ||
    value.diagnostics.some((item) => typeof item !== "string") ||
    !Array.isArray(value.inputs) ||
    value.inputs.some((item) => !object(item) || typeof item.kind !== "string" || typeof item.reference !== "string")
  )
    fail("Market valuation source evidence is malformed.");
  for (const field of ["fundProfileId", "accountingBasis", "positionSide", "selectedSource", "selectionReceiptId"]) {
    if (value[field] !== null && typeof value[field] !== "string") fail("Market valuation source metadata is malformed.");
  }
  for (const field of ["observedAsOf", "knowledgeAsOf"]) {
    if (value[field] !== null && (typeof value[field] !== "string" || !Number.isFinite(Date.parse(value[field] as string)))) fail("Market valuation timestamps are malformed.");
  }
  if (typeof value.currency !== "string" || (value.isStaleFallback !== null && typeof value.isStaleFallback !== "boolean")
    || (value.daysStale !== null && (!Number.isSafeInteger(value.daysStale) || (value.daysStale as number) < 0))
    || (value.positionVersion !== null && (!Number.isSafeInteger(value.positionVersion) || (value.positionVersion as number) < 0))) fail("Market valuation metadata is malformed.");
  if (hasAmounts && (!value.observedAsOf || !value.knowledgeAsOf || value.inputs.length === 0 || !value.modelKey || !value.modelVersion)) fail("Market valuation evidence is incomplete.");
  return value as unknown as AssetPositionMarketValuation;
}
