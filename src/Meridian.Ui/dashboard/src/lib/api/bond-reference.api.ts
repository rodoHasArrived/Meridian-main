import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { BondIssuerLadderPageQuery, BondMaturityLadderPageQuery, BondReferencePage } from "@/types/bond-reference.types";

function query(values: Record<string, string | number | null | undefined>): string {
  const parts = Object.entries(values)
    .filter(([, value]) => value !== undefined && value !== null && value !== "")
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  return parts.length > 0 ? `?${parts.join("&")}` : "";
}

/** Keyset page of one issuer's bonds; pass the previous page's cursor to continue. */
export function getBondIssuerLadderPage(request: BondIssuerLadderPageQuery, options: ApiRequestOptions = {}): Promise<BondReferencePage> {
  if (!request.issuerName.trim()) return Promise.reject(new Error("Enter an issuer name."));
  return apiGetJson<BondReferencePage>(
    `${UI_API_ROUTES.ReferenceDataBondIssuerLadderPage}${query({ issuerName: request.issuerName.trim(), pageSize: request.pageSize, cursor: request.cursor, asOf: request.asOf })}`,
    { ...options, allowDevelopmentFallback: false });
}

/** Keyset page of bonds maturing in [from, to]; pass the previous page's cursor to continue. */
export function getBondMaturityLadderPage(request: BondMaturityLadderPageQuery, options: ApiRequestOptions = {}): Promise<BondReferencePage> {
  if (!request.from || !request.to) return Promise.reject(new Error("Enter the maturity window start and end dates."));
  return apiGetJson<BondReferencePage>(
    `${UI_API_ROUTES.ReferenceDataBondMaturityLadderPage}${query({ from: request.from, to: request.to, pageSize: request.pageSize, cursor: request.cursor, asOf: request.asOf })}`,
    { ...options, allowDevelopmentFallback: false });
}
