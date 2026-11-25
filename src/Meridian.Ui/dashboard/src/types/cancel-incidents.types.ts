/** Account-authorized retained observations. Financial quantities stay exact decimal text. */
export interface CancelAllIncidentSummary {
  incidentId: string;
  fundAccountId: string;
  startedAt: string;
  completedAt?: string | null;
  actor?: string | null;
  gatewayId: string;
  executionMode: string;
  evidenceStatus: string;
  evidenceError?: string | null;
  brokerViewUnavailable: boolean;
  orderCount: number;
  unresolvedOrderCount: number;
}

export interface CancelAllIncidentHistory {
  asOf: string;
  returned: number;
  limit: number;
  hasMore: boolean;
  coverageNote: string;
  incidents: CancelAllIncidentSummary[];
}

export interface CancelAllOrderOutcome {
  observationKey: string;
  orderId: string;
  brokerOrderId?: string | null;
  fundAccountId?: string | null;
  brokerAccountId?: string | null;
  symbol?: string | null;
  side?: string | null;
  operation: string;
  disposition: string;
  initialStatus?: string | null;
  status?: string | null;
  initialQuantity?: string | null;
  initialFilledQuantity?: string | null;
  quantity?: string | null;
  filledQuantity?: string | null;
  remainingQuantity?: string | null;
  cancellationAttempted: boolean;
  requiresReconciliation: boolean;
  hasUnconfirmedReplacement: boolean;
  candidateBrokerOrderId?: string | null;
  candidateClientOrderId?: string | null;
  residualNotional?: string | null;
  currency?: string | null;
  valuationCoverage: string;
  valuationPrice?: string | null;
  valuationObservedAt?: string | null;
  valuationSource?: string | null;
  observedAt: string;
  detail?: string | null;
}

export interface CancelAllIncident {
  summary: CancelAllIncidentSummary;
  correlationId?: string | null;
  brokerScopeIdentity?: string | null;
  brokerViewError?: string | null;
  orders: CancelAllOrderOutcome[];
}
