import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { ApiErrorDisplay } from "@/lib/api-errors";
import type { AccountingBasisKind, PrivateCapitalFundEventLedgerRecord } from "@/types";

export const DEFAULT_ACCOUNTING_BASIS: AccountingBasisKind = "Primary";

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
  readiness: PrivateCapitalFundEventLedgerRecord["readiness"]
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
