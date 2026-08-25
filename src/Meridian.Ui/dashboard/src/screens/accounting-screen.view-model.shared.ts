import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { ApiErrorDisplay } from "@/lib/api-errors";
import type {
  AccountingBasisKind,
  LedgerTrialBalanceLine,
  PrivateCapitalFundEventLedgerRecord
} from "@/types";

export const DEFAULT_ACCOUNTING_BASIS: AccountingBasisKind = "Primary";

/**
 * The basis to select for a trial balance that has just loaded.
 *
 * The builders render exactly one basis and filter the rest out, so the selection has to name a
 * basis the response actually carries. Keeping the previous choice hides an incoming Primary-only
 * set; resetting to Primary hides an incoming GAAP- or tax-only one. Prefer Primary when it is
 * present — it is what an operator expects to land on — and otherwise fall back to whichever basis
 * the rows are keyed on. Rows with no basis normalize to Primary downstream, so they count as
 * Primary here too.
 *
 * Shared by the posted ledger and the strategy run ledger: both hit the same failure from opposite
 * directions, and a second copy would drift.
 */
export function resolveAvailableAccountingBasis(rows: LedgerTrialBalanceLine[]): AccountingBasisKind {
  const present = new Set(rows.map((row) => row.accountingBasis ?? DEFAULT_ACCOUNTING_BASIS));
  if (present.size === 0 || present.has(DEFAULT_ACCOUNTING_BASIS)) {
    return DEFAULT_ACCOUNTING_BASIS;
  }

  return [...present][0];
}

/**
 * How many accounting bases a trial balance actually carries.
 *
 * A surface renders one basis at a time, so anything derived across all of them — the endpoint's
 * period totals, its period-on-period variance — means something different from the rows on
 * screen the moment this is greater than one, and has to be labelled rather than presented as the
 * selected basis's own. Rows with no basis normalize to Primary, matching the builders.
 */
export function countAvailableAccountingBases(rows: readonly Pick<LedgerTrialBalanceLine, "accountingBasis">[]): number {
  return new Set(rows.map((row) => row.accountingBasis ?? DEFAULT_ACCOUNTING_BASIS)).size;
}

export function normalizeQueryValue(value: string | null): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

export function buildPrivateCapitalFundEventCommandCenterRoute(
  fundProfileId: string | null | undefined,
  ledgerBookId: string | null | undefined,
  fundEventId: string
): string {
  const params = new URLSearchParams();
  const normalizedFundProfileId = normalizePrivateCapitalRouteId(fundProfileId);
  const normalizedLedgerBookId = normalizePrivateCapitalRouteId(ledgerBookId);
  if (normalizedFundProfileId) {
    params.set("fundProfileId", normalizedFundProfileId);
  }

  if (normalizedLedgerBookId && isGuid(normalizedLedgerBookId)) {
    params.set("ledgerBookId", normalizedLedgerBookId);
  }

  params.set("fundEventId", fundEventId);
  return `${WORKSTATION_API_ENDPOINTS.privateCapitalFundEventCommandCenter}?${params.toString()}`;
}

export function manualJournalPrivateCapitalReadinessTone(
  // Accepts null because the final branch already returns "outline" for an absent readiness; the
  // narrower parameter type made that branch reachable only for the empty string. Widening a
  // parameter is safe for every existing caller.
  readiness: PrivateCapitalFundEventLedgerRecord["readiness"] | null
): "danger" | "success" | "warning" | "outline" {
  if (readiness === "Blocked") return "danger";
  if (readiness === "Ready" || readiness === "Published") return "success";
  return readiness ? "warning" : "outline";
}

export function normalizeApiErrorDisplay(error: string | ApiErrorDisplay | null): ApiErrorDisplay | null {
  if (!error) {
    return null;
  }

  if (typeof error === "string") {
    return { summary: error, details: [] };
  }

  return error;
}

function normalizePrivateCapitalRouteId(value: string | null | undefined): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}
