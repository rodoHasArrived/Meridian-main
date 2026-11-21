import {
  EXECUTION_API_ENDPOINTS,
  executionAuditEndpoint,
  executionSessionEndpoint,
  executionSessionReplayEndpoint,
  executionSymbolPositionLimitEndpoint
} from "@/lib/workstation-endpoints";
import type {
  ExecutionAuditEntry,
  ExecutionCircuitBreakerActivationResponse,
  ExecutionControlSnapshot,
  PaperSessionDetail,
  PaperSessionReplayVerification,
  UpdateExecutionCircuitBreakerRequest,
  UpdateExecutionPositionLimitRequest
} from "@/types";

type GetJson = <T>(path: string) => Promise<T>;
type PostJson = <T>(path: string, body?: unknown) => Promise<T>;

export function createExecutionEvidenceApi(getJson: GetJson, postJson: PostJson) {
  return {
    getPaperSessionDetail(sessionId: string) {
      return getJson<PaperSessionDetail>(executionSessionEndpoint(sessionId));
    },
    getPaperSessionReplayVerification(sessionId: string) {
      return getJson<PaperSessionReplayVerification>(executionSessionReplayEndpoint(sessionId));
    },
    getExecutionAudit(take = 20) {
      return getJson<ExecutionAuditEntry[]>(executionAuditEndpoint(take));
    },
    getExecutionControls() {
      return getJson<ExecutionControlSnapshot>(EXECUTION_API_ENDPOINTS.controls);
    },
    updateExecutionCircuitBreaker(request: UpdateExecutionCircuitBreakerRequest) {
      return postJson<ExecutionCircuitBreakerActivationResponse>(EXECUTION_API_ENDPOINTS.circuitBreaker, request);
    },
    updateExecutionDefaultPositionLimit(request: UpdateExecutionPositionLimitRequest) {
      return postJson<ExecutionControlSnapshot>(EXECUTION_API_ENDPOINTS.defaultPositionLimit, request);
    },
    updateExecutionSymbolPositionLimit(symbol: string, request: UpdateExecutionPositionLimitRequest) {
      return postJson<ExecutionControlSnapshot>(executionSymbolPositionLimitEndpoint(symbol), request);
    }
  };
}
