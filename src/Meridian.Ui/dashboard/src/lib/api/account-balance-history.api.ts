import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export interface AccountBalanceHistory {
  accountId: string; asOfUtc: string; isTruncated: boolean;
  points: { snapshotId: string; accountId: string; asOfDate: string; recordedAtUtc: string; currency: string; source: string; cashBalance: string; securitiesMarketValue: string | null }[];
}

export async function getAccountBalanceHistory(accountId: string, options: ApiRequestOptions = {}) {
  const response = await apiGetJson<AccountBalanceHistory>(UI_API_ROUTES.WorkstationAccountBalanceHistory.replace("{accountId}", encodeURIComponent(accountId)), { ...options, allowDevelopmentFallback: false });
  const decimal = /^-?\d+(?:\.\d+)?$/;
  if (response.accountId?.toLowerCase() !== accountId.toLowerCase() || !Array.isArray(response.points)
    || !Number.isFinite(Date.parse(response.asOfUtc)) || typeof response.isTruncated !== "boolean"
    || response.points.some((point) => point.accountId?.toLowerCase() !== accountId.toLowerCase()
      || !point.currency?.trim() || !point.source?.trim() || !/^\d{4}-\d{2}-\d{2}$/.test(point.asOfDate)
      || !Number.isFinite(Date.parse(point.asOfDate)) || !Number.isFinite(Date.parse(point.recordedAtUtc))
      || typeof point.cashBalance !== "string" || !decimal.test(point.cashBalance)
      || point.securitiesMarketValue !== null && (typeof point.securitiesMarketValue !== "string" || !decimal.test(point.securitiesMarketValue)))) {
    throw new Error("Account balance history has incomplete evidence or does not belong to the selected account.");
  }
  return response;
}
