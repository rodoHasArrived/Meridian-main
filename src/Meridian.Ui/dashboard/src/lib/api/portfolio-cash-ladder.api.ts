/**
 * Client function for the portfolio cash ladder endpoint.
 * Thin wrapper over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { portfolioCashLadderEndpoint } from "@/lib/workstation-endpoints";
import type { PortfolioCashLadder } from "@/types/portfolio-cash-ladder.types";

export function getPortfolioCashLadder(
  params: { scenario?: string; horizonDays?: number; minimumCash?: number; bucketDays?: number; fundAccountId?: string } = {},
  options: ApiRequestOptions = {}
): Promise<PortfolioCashLadder> {
  return apiGetJson<PortfolioCashLadder>(portfolioCashLadderEndpoint(params), options);
}
