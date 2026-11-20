import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { routeWithParam } from "@/lib/workstation-endpoints";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { AmountPassport, AmountPassportIdentity, AmountPassportScope } from "@/types/amount-passport";

const scopeKeys = ["tenantId", "companyId", "basis", "fundProfileId", "ledgerBookId", "periodId",
  "entityId", "accountId", "organizationId"] as const;
export const AMOUNT_PASSPORT_FRAGMENT_PREFIX = "#amount-passport?";

function passportQuery(identity: AmountPassportIdentity): URLSearchParams {
  const params = new URLSearchParams({ asOf: identity.asOf, revision: identity.revision });
  for (const key of scopeKeys) {
    const value = identity.scope[key];
    if (value) params.set(key, value);
  }
  return params;
}

export function amountPassportKey(identity: AmountPassportIdentity): string {
  return new URLSearchParams({
    subjectKind: identity.subjectKind, subjectId: identity.subjectId, ...Object.fromEntries(passportQuery(identity))
  }).toString();
}

export function parseAmountPassportKey(key: string): AmountPassportIdentity | null {
  const params = new URLSearchParams(key);
  const subjectKind = params.get("subjectKind");
  const subjectId = params.get("subjectId");
  const asOf = params.get("asOf");
  const revision = params.get("revision");
  const tenantId = params.get("tenantId");
  const companyId = params.get("companyId");
  const basis = params.get("basis");
  if ((subjectKind !== "ledger-amount" && subjectKind !== "position-valuation") || !subjectId ||
    subjectId.length > 512 || !asOf || !revision || !tenantId || !companyId || !basis ||
    [...params].some(([key, value]) => params.getAll(key).length !== 1 || !value.trim() ||
      (!["subjectKind", "subjectId", "asOf", "revision"].includes(key) && !scopeKeys.includes(key as keyof AmountPassportScope)))) return null;
  const scope: AmountPassportScope = { tenantId, companyId, basis };
  for (const dimension of scopeKeys) {
    const value = params.get(dimension);
    if (value) scope[dimension] = value;
  }
  if (subjectKind === "ledger-amount" ? !scope.ledgerBookId || !scope.periodId || !scope.fundProfileId : !scope.accountId) return null;
  return { subjectKind, subjectId, scope, asOf, revision };
}

export function readAmountPassportLink(hash: string): AmountPassportIdentity | null {
  return hash.startsWith(AMOUNT_PASSPORT_FRAGMENT_PREFIX)
    ? parseAmountPassportKey(hash.slice(AMOUNT_PASSPORT_FRAGMENT_PREFIX.length)) : null;
}

/** The fragment opens the shared rail on an existing workstation screen. It contains only the address. */
export function amountPassportHref(identity: AmountPassportIdentity): string {
  const hash = AMOUNT_PASSPORT_FRAGMENT_PREFIX + amountPassportKey(identity);
  if (identity.subjectKind === "position-valuation") return `/workstation${WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync}${hash}`;
  if (identity.subjectId.startsWith("report:")) {
    const runId = identity.subjectId.slice("report:".length, identity.subjectId.lastIndexOf(":"));
    return `/workstation${WORKSTATION_ROUTE_CATALOG.reportingRunDetail}?${new URLSearchParams({ runId })}${hash}`;
  }
  const query = new URLSearchParams({
    ledgerBookId: identity.scope.ledgerBookId ?? "", periodId: identity.scope.periodId ?? ""
  });
  return `/workstation${WORKSTATION_ROUTE_CATALOG.accountingLedger}?${query}${hash}`;
}

export function getAmountPassport(identity: AmountPassportIdentity, options: ApiRequestOptions = {}) {
  const endpoint = routeWithParam(
    routeWithParam(UI_API_ROUTES.WorkstationAmountPassport, "subjectKind", identity.subjectKind),
    "subjectId", identity.subjectId
  );
  return apiGetJson<AmountPassport>(`${endpoint}?${passportQuery(identity)}`, { ...options, allowDevelopmentFallback: false });
}
