import { describe, expect, it } from "vitest";

import type {
  AccountingBookContext,
  AssetOperationsDetail,
  AssetOperationsProjection,
  EconomicEventReference,
  PostingRuleJournalCandidateRequest,
  ProjectionLineage,
} from "../types";

describe("instrument-to-journal contract compatibility", () => {
  it("keeps legacy Asset Operations payloads valid when aligned collections are absent", () => {
    const legacyPayload = {
      subject: {
        securityId: "security-1",
        assetClass: "MortgageBackedSecurity",
        displayName: "Agency pass-through",
        primaryIdentifier: null,
        operationalProfile: [],
      },
      termsHistory: [],
      lifecycleEvents: [],
      cashFlowProjectionRuns: [],
      projectedCashFlows: [],
      actualActivity: [],
      reconciliationRuns: [],
      reconciliationResults: [],
      ledgerProjections: [],
      readiness: {
        securityId: "security-1",
        status: "ReviewRequired",
        capabilities: [],
        readyCapabilities: [],
        missingCapabilities: [],
        blockers: [],
        evaluatedAt: "2026-07-10T00:00:00Z",
        sourceDomain: "SecurityMaster",
        sourceEntityId: "security-1",
      },
      workflowAudit: [],
    } satisfies AssetOperationsDetail;

    const deserialized = JSON.parse(JSON.stringify(legacyPayload)) as AssetOperationsDetail;
    const legacyProjection: AssetOperationsProjection = legacyPayload;

    expect(deserialized.instrumentRoles ?? []).toEqual([]);
    expect(deserialized.bookPositions ?? []).toEqual([]);
    expect(deserialized.positionEconomicStates ?? []).toEqual([]);
    expect(deserialized.projectionLineages ?? []).toEqual([]);
    expect(legacyProjection.bookPositions ?? []).toEqual([]);
  });

  it("carries typed book, event, position, and projection references on candidate intent", () => {
    const bookContext: AccountingBookContext = {
      ledgerBookId: "book-1",
      fundProfileId: "fund-1",
      fundStructureNodeId: "entity-1",
      fundStructureNodeKind: "LegalEntity",
      displayName: "Primary GAAP book",
      baseCurrency: "USD",
      accountingBasis: "Gaap",
      accountingPolicyId: "policy-1",
      accountingPolicyVersion: "v3",
      periodId: "period-1",
      dimensions: {
        instrumentId: "security-1",
        positionId: "position-1",
      },
    };
    const economicEvent: EconomicEventReference = {
      eventId: "event-1",
      eventType: "MbsFactorUpdated",
      eventVersion: 1,
      effectiveDate: "2026-07-10",
      occurredAtUtc: "2026-07-10T00:00:00Z",
      sourceDomain: "SecurityMaster",
      sourceContentHash: "sha256:factor-evidence",
      evidenceLinks: ["evidence://factor/event-1"],
      securityId: "security-1",
      bookPositionId: "position-1",
    };
    const projectionLineage: ProjectionLineage = {
      projectionRunId: "projection-run-1",
      projectionEventId: "projection-event-1",
      modelKey: "mbs-factor-paydown",
      modelVersion: "v1",
      engineVersion: "v1",
      scenario: "Base",
      projectionAsOfDate: "2026-07-10",
      generatedAtUtc: "2026-07-10T00:00:01Z",
      sourceDomain: "AssetOperations",
      triggerEvent: economicEvent,
      evidenceLinks: ["evidence://projection/projection-run-1"],
      bookPositionId: "position-1",
    };
    const request = {
      fundProfileId: "fund-1",
      sourceEventType: "MbsFactorUpdated",
      eventAmount: 1750,
      currency: "USD",
      effectiveDate: "2026-07-10",
      actor: "operator-1",
      aggregateId: "journal-aggregate-1",
      periodId: "period-1",
      accountingTimestamp: "2026-07-10T00:00:02Z",
      description: "MBS factor principal paydown",
      ledgerBookId: "book-1",
      dimensions: bookContext.dimensions,
      bookContext,
      bookPositionId: "position-1",
      economicEvent,
      projectionLineage,
      rulePackReference: {
        rulePackId: "mbs-accounting",
        rulePackVersion: "v2",
        selectedRuleId: "mbs-factor-paydown",
        selectedRuleVersion: "v4",
      },
    } satisfies PostingRuleJournalCandidateRequest;

    expect(request.dimensions?.positionId).toBe("position-1");
    expect(request.economicEvent.eventId).toBe("event-1");
    expect(request.projectionLineage.triggerEvent).toBe(economicEvent);
  });
});
