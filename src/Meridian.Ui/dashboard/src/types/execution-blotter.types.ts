/**
 * Broker-side execution read models.
 *
 * Mirrors `ExecutionGatewayHealth` and `ExecutionAccountSnapshot` in
 * `Meridian.Ui.Shared.Endpoints.ExecutionEndpoints`, and
 * `ExecutionBlotterSnapshotResponse` in `Meridian.Contracts.Api.ExecutionApiModels`.
 *
 * Only the contracts made of strings, numbers, and booleans are modelled here.
 * The neighbouring `/accounts` and `/capabilities` reads carry bare .NET enums,
 * which this endpoint group serializes as ordinals with no catalog to resolve
 * them against — so they stay unwired rather than being displayed as guesses.
 */

/** `GET /api/execution/health` — which gateway is selected and whether it answers. */
export interface ExecutionGatewayHealth {
  brokerName: string;
  mode: string;
  isAvailable: boolean;
  asOf: string;
  selectedGatewayId?: string | null;
}

/** `GET /api/execution/account` — headline account figures. */
export interface ExecutionAccountSnapshot {
  cash: number;
  portfolioValue: number;
  unrealisedPnl: number;
  realisedPnl: number;
  positionCount: number;
  asOf: string;
}

export interface ExecutionPositionLot {
  lotId: string;
  quantity: number;
  costBasis: number;
  acquiredAt: string;
}

export interface ExecutionBlotterPosition {
  positionKey: string;
  symbol: string;
  underlyingSymbol: string;
  productDescription: string;
  tradeId?: string | null;
  quantity: number;
  averageCostBasis: number;
  marketPrice: number;
  marketValue: number;
  unrealisedPnl: number;
  realisedPnl: number;
  assetClass: string;
  side: string;
  expiration?: string | null;
  strike?: number | null;
  right?: string | null;
  supportsClose?: boolean;
  supportsUpsize?: boolean;
  metadata?: Record<string, string> | null;
  lots?: ExecutionPositionLot[] | null;
}

/**
 * `GET /api/execution/positions/blotter`.
 *
 * `isBrokerBacked`, `isLive`, and `source` are the provenance of every row: the
 * same shape carries a broker's own book and a paper simulation, and only these
 * flags say which one an operator is looking at.
 */
export interface ExecutionBlotterSnapshot {
  positions: ExecutionBlotterPosition[];
  isBrokerBacked: boolean;
  isLive: boolean;
  source: string;
  statusMessage: string;
  asOf: string;
}

/** Body of `POST /api/execution/positions/actions/upsize`. */
export interface ExecutionPositionActionRequest {
  positionKey: string;
  quantity?: number;
  fundAccountId?: string;
}

/**
 * Result of a blotter-driven execution action.
 *
 * Re-exported from the shared barrel rather than redeclared: the close action on
 * this same C# record already uses that definition, and a second one here would
 * let the two drift apart.
 */
export type { TradingActionResult } from "@/types";
