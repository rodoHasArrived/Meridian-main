/**
 * Client functions for the direct-lending read routes.
 *
 * Thin wrappers over the shared workstation API client so errors, aborts, and no-host
 * fixture semantics stay aligned with the rest of the dashboard. Only reads live here;
 * loan servicing commands remain desktop-owned.
 */

import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { directLendingPortfolioSummaryEndpoint } from "@/lib/workstation-endpoints";
import type { LoanPortfolioSummary } from "@/types/direct-lending.types";

export function getLoanPortfolioSummary(
  options: ApiRequestOptions = {}
): Promise<LoanPortfolioSummary> {
  return apiGetJson<LoanPortfolioSummary>(directLendingPortfolioSummaryEndpoint(), options);
}
