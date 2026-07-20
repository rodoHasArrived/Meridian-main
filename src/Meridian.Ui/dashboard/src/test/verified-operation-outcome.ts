import type { VerifiedOperationOutcome } from "@/types";

export function buildSuccessfulVerifiedOperationOutcome(
  overrides: Partial<VerifiedOperationOutcome> = {}
): VerifiedOperationOutcome {
  return {
    operationId: "test-operation",
    operationKind: "test.operation",
    state: "Succeeded",
    startedAtUtc: "2026-01-01T00:00:00Z",
    completedAtUtc: "2026-01-01T00:00:00Z",
    attemptNumber: 1,
    correlationId: "test-correlation",
    inputHashSha256: "a".repeat(64),
    postconditions: [],
    evidence: [],
    artifacts: [],
    issues: [],
    recovery: [],
    schemaVersion: "1.0",
    isSuccessful: true,
    ...overrides
  };
}
