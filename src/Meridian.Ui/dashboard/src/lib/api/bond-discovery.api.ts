import { apiGetJson, previewDataUpload, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export interface BondDiscoveryCandidate {
  symbol: string;
  name: string;
  exchange: string | null;
  assetType: string | null;
  country: string | null;
  currency: string | null;
  source: string | null;
  figi: string | null;
  compositeFigi: string | null;
}

export interface BondDiscoveryResult {
  provider: string;
  providerLabel: string;
  available: boolean;
  unavailableReason: string | null;
  canStage: boolean;
  query: string;
  candidates: BondDiscoveryCandidate[];
  observedAtUtc: string;
}

export function discoverProviderBonds(query: string, options: ApiRequestOptions = {}) {
  return apiGetJson<BondDiscoveryResult>(`${UI_API_ROUTES.ReferenceDataBondDiscovery}?${new URLSearchParams({ query })}`, {
    ...options, allowDevelopmentFallback: false
  });
}

// CSV cells are quoted and formula prefixes neutralized. These are retained candidate facts,
// not a command to create approved terms or invent missing coupon/maturity economics.
function csvCell(value: string | null | undefined): string {
  const text = (value ?? "").replace(/[\r\n]+/g, " ");
  return `"${(/^[=+@\-\t]/.test(text) ? "'" : "") + text.replaceAll('"', '""')}"`;
}

export function stageBondDiscoveryCandidate(
  candidate: BondDiscoveryCandidate,
  observedAtUtc: string,
  reason: string,
  options: ApiRequestOptions = {}
) {
  const header = "asset_id,symbol,asset_name,asset_class,issuer,currency,country,maturity_date,coupon_rate,source_system,figi,composite_figi,exchange,observed_at_utc,review_reason";
  const row = [candidate.figi || `${candidate.source}:${candidate.symbol}`, candidate.symbol, candidate.name,
    "Bond", "", candidate.currency, candidate.country, "", "", candidate.source, candidate.figi,
    candidate.compositeFigi, candidate.exchange, observedAtUtc, reason.trim()].map(csvCell).join(",");
  const file = new File([`${header}\n${row}\n`], "provider-bond-candidate.csv", { type: "text/csv" });
  return previewDataUpload({ templateId: "asset-information", file }, { ...options, allowDevelopmentFallback: false });
}
