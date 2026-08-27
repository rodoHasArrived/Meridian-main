import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  buildCorporateActionInboxModel,
  filterCorporateActionInboxRows,
  useCorporateActionInboxPanel
} from "./data-screen.corporate-action-inbox.view-model";
import type {
  CorporateActionCaseProjection,
  CorporateActionInboxAcceptResult,
  CorporateActionInboxResponse,
  CorporateActionProcessingCaseDto,
  CorporateActionProposalEntry
} from "@/types";

function proposal(overrides: Partial<CorporateActionProposalEntry> = {}): CorporateActionProposalEntry {
  return {
    securityId: "3f0c9a53-3f8f-4b04-9c1e-0f8f4b049c1e",
    ticker: "GME",
    actionType: "Dividend",
    exDate: "2026-08-14",
    recordDate: null,
    payableDate: "2026-08-28",
    amount: 0.24,
    currency: "USD",
    splitFromFactor: null,
    splitToFactor: null,
    winningSource: "finnhub",
    agreeingSources: ["finnhub"],
    dissentingSources: [],
    autoApplied: false,
    ...overrides
  };
}

function response(overrides: Partial<CorporateActionInboxResponse> = {}): CorporateActionInboxResponse {
  return {
    lastIngestAt: "2026-07-05T12:00:00Z",
    stagedCount: 1,
    appliedLastRun: 2,
    duplicatesSkippedLastRun: 1,
    staged: [proposal()],
    errors: [],
    cases: [],
    ...overrides
  };
}

function compactCase(overrides: Partial<CorporateActionProcessingCaseDto> = {}): CorporateActionProcessingCaseDto {
  return {
    caseId: "case-compact-100",
    proposalId: "proposal-100",
    corporateActionId: "corporate-action-100",
    securityId: "3f0c9a53-3f8f-4b04-9c1e-0f8f4b049c1e",
    scope: {
      tenantId: "tenant-meridian",
      companyId: "company-alpha"
    },
    state: "TermsConfirmed",
    version: 2,
    methodologyProfileId: "clearwater-corporate-actions/v1",
    assignedTo: "Casey Operator",
    blockedReason: null,
    createdBy: "casey.operator",
    createdAtUtc: "2026-07-05T12:01:00Z",
    updatedBy: "casey.operator",
    updatedAtUtc: "2026-07-05T12:01:00Z",
    actionAvailability: {
      canAddEvidence: true,
      canRecordConflict: true,
      canResolveConflict: true,
      canManageOptions: false,
      canTransition: true,
      canApproveAccounting: false,
      allowedTransitionTargets: ["ElectionPending"],
      blockers: []
    },
    ...overrides
  };
}

function durableCase(overrides: Partial<CorporateActionCaseProjection> = {}): CorporateActionCaseProjection {
  return {
    caseId: "case-100",
    proposalId: "proposal-100",
    version: 7,
    status: "AccountingReview",
    assignedTo: "Casey Operator",
    conflictState: "Resolved",
    permissionState: "Allowed",
    scope: {
      tenantId: "tenant-meridian",
      companyId: "company-alpha",
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      accountingBasis: "GAAP"
    },
    receivedAt: "2026-07-05T11:55:00Z",
    dueAt: "2026-08-13T17:00:00Z",
    sourceFacts: [],
    entitlement: null,
    elections: [],
    basisComparisons: [],
    lotPreview: [],
    journalPreview: [],
    reconciliation: [],
    history: [],
    proofReferences: [],
    actionAvailability: {
      canAcceptCanonicalFact: true,
      canSubmitElection: false,
      canApproveTreatment: false,
      canPost: false
    },
    ...overrides
  };
}

function acceptResult(): CorporateActionInboxAcceptResult {
  return {
    corporateAction: {
      corpActId: "ca-accepted",
      securityId: "3f0c9a53-3f8f-4b04-9c1e-0f8f4b049c1e",
      eventType: "Dividend",
      exDate: "2026-08-14",
      payDate: "2026-08-28",
      dividendPerShare: 0.24,
      currency: "USD",
      splitRatio: null,
      newSecurityId: null,
      distributionRatio: null,
      acquirerSecurityId: null,
      exchangeRatio: null,
      subscriptionPricePerShare: null,
      rightsPerShare: null,
      recordDate: "2026-08-15",
      lifecycleState: "Announced",
      supersedesCorpActId: null,
      redemptionPricePercentOfPar: null,
      payload: { taxability: "source-assertion-only" },
      payloadSchemaVersion: 1
    },
    audit: {
      auditId: "audit-accepted",
      securityId: "3f0c9a53-3f8f-4b04-9c1e-0f8f4b049c1e",
      corporateActionId: "ca-accepted",
      eventType: "Dividend",
      sourceSystem: "finnhub",
      actor: "casey.operator",
      recordedAtUtc: "2026-07-05T12:01:00Z",
      sourceRecordId: "proposal-100",
      reason: "Accepted from case queue",
      correlationId: "correlation-100"
    },
    restatement: {
      restatementRequired: true,
      candidates: [{
        reportId: "report-1",
        priorVersionReportId: "report-0",
        periodLabel: "2026-06",
        summary: "Dividend amount changed",
        changedLines: [{
          lineKey: "investment-income",
          previousValue: "0",
          currentValue: "240"
        }]
      }]
    }
  };
}

describe("buildCorporateActionInboxModel", () => {
  it("formats dividends with ex-date countdown and consensus", () => {
    const model = buildCorporateActionInboxModel(response(), new Date("2026-07-05T15:00:00Z"));

    const row = model.rows[0];
    expect(row.ticker).toBe("GME");
    expect(row.valueLabel).toBe("0.24 USD");
    expect(row.daysUntilEx).toBe(40);
    expect(row.countdownLabel).toBe("in 40 days");
    expect(row.consensusLabel).toBe("1/1 source agree");
    expect(row.tone).toBe("neutral");
    expect(row.statusLabel).toBe("Not supplied");
    expect(row.permissionLabel).toBe("Authorization not supplied");
    expect(row.canAcceptCanonicalFact).toBe(false);
    expect(row.acceptCanonicalFactDisabledReason).toBe("Server did not supply action authorization.");
    expect(model.summary).toContain("1 staged proposal");
  });

  it("orders rows by ex-date urgency and flags disputes with a warning tone", () => {
    const model = buildCorporateActionInboxModel(
      response({
        stagedCount: 2,
        staged: [
          proposal({ ticker: "MSFT", exDate: "2026-09-15" }),
          proposal({
            ticker: "AAPL",
            exDate: "2026-07-20",
            splitFromFactor: 1,
            splitToFactor: 4,
            amount: null,
            currency: null,
            agreeingSources: ["tiingo"],
            dissentingSources: ["alphavantage"]
          })
        ]
      }),
      new Date("2026-07-05T15:00:00Z")
    );

    expect(model.rows.map((row) => row.ticker)).toEqual(["AAPL", "MSFT"]);
    const disputed = model.rows[0];
    expect(disputed.tone).toBe("warning");
    expect(disputed.valueLabel).toBe("4:1 split");
    expect(disputed.consensusLabel).toBe("1/2 sources agree");
    expect(disputed.dissentingSources).toEqual(["alphavantage"]);
  });

  it("reports an empty inbox without rows", () => {
    const model = buildCorporateActionInboxModel(
      response({ stagedCount: 0, staged: [], lastIngestAt: null })
    );

    expect(model.rows).toHaveLength(0);
    expect(model.lastIngestLabel).toBe("never");
    expect(model.summary).toContain("No staged corporate actions");
  });

  it("projects explicit durable case identity, version, assignment, conflict, and authorization", () => {
    const model = buildCorporateActionInboxModel(response({
      staged: [proposal({ case: durableCase() })]
    }), new Date("2026-07-05T15:00:00Z"));

    expect(model.rows[0]).toMatchObject({
      key: "proposal-100",
      caseIdLabel: "case-100",
      proposalIdLabel: "proposal-100",
      versionLabel: "v7",
      statusLabel: "AccountingReview",
      assignmentLabel: "Casey Operator",
      conflictLabel: "Resolved",
      permissionLabel: "Allowed by server policy",
      canAcceptCanonicalFact: true
    });
  });

  it("fails closed when the server supplies a non-positive proposal version", () => {
    const model = buildCorporateActionInboxModel(response({
      staged: [proposal({
        proposalId: "proposal-100",
        version: 0,
        acceptanceScope: { tenantId: "tenant-meridian", companyId: "company-alpha" },
        actionAvailability: { canAccept: true, canReject: true, canCompareEvidence: true, blockers: [] }
      })]
    }));

    expect(model.rows[0]).toMatchObject({
      canAcceptCanonicalFact: false,
      acceptCanonicalFactDisabledReason: "Server supplied an invalid proposal version for concurrency control."
    });
  });

  it("merges a top-level compact backend case into its staged proposal", () => {
    const model = buildCorporateActionInboxModel(response({
      staged: [proposal({
        proposalId: "proposal-100",
        version: 4,
        proposalState: "ReviewRequired",
        acceptanceScope: { tenantId: "tenant-meridian", companyId: "company-alpha" },
        actionAvailability: { canAccept: true, canReject: true, canCompareEvidence: true, blockers: [] }
      })],
      cases: [compactCase()]
    }), new Date("2026-07-05T15:00:00Z"));

    expect(model.rows).toHaveLength(1);
    expect(model.rows[0]).toMatchObject({
      key: "proposal-100",
      caseIdLabel: "case-compact-100",
      proposalIdLabel: "proposal-100",
      versionLabel: "v4",
      statusLabel: "TermsConfirmed",
      ticker: "GME",
      canAcceptCanonicalFact: true
    });
    expect(model.rows[0].compactCase?.corporateActionId).toBe("corporate-action-100");
  });

  it("keeps an accepted durable case visible after its proposal leaves staged", () => {
    const model = buildCorporateActionInboxModel(response({
      stagedCount: 0,
      staged: [],
      cases: [compactCase({ state: "AccountingReview", version: 5 })]
    }));

    expect(model.rows).toHaveLength(1);
    expect(model.rows[0]).toMatchObject({
      key: "proposal-100",
      caseIdLabel: "case-compact-100",
      versionLabel: "case v5",
      statusLabel: "AccountingReview",
      expectedVersion: null,
      canAcceptCanonicalFact: false,
      ticker: "Not supplied",
      actionType: "Not supplied"
    });
    expect(model.rows[0].durableCase?.status).toBe("AccountingReview");
    expect(model.summary).toContain("1 durable processing case");
  });

  it("filters case rows by search, status, assignment, and conflict without changing source order", () => {
    const model = buildCorporateActionInboxModel(response({
      stagedCount: 2,
      staged: [
        proposal({ ticker: "MSFT", exDate: "2026-09-15", case: durableCase({ status: "AccountingReview" }) }),
        proposal({
          ticker: "AAPL",
          exDate: "2026-07-20",
          case: durableCase({
            caseId: "case-aapl",
            proposalId: "proposal-aapl",
            status: "Disputed",
            assignedTo: null,
            conflictState: "Open"
          })
        })
      ]
    }), new Date("2026-07-05T15:00:00Z"));

    const filtered = filterCorporateActionInboxRows(model.rows, {
      search: "aapl",
      status: "Disputed",
      assignment: "Unassigned",
      conflict: "Open"
    });

    expect(filtered.map((row) => row.ticker)).toEqual(["AAPL"]);
    expect(model.rows.map((row) => row.ticker)).toEqual(["AAPL", "MSFT"]);
  });

  it("retains the append receipt and reports partial success when the queue refresh fails", async () => {
    const fetchInbox = vi.fn()
      .mockResolvedValueOnce(response({ staged: [proposal({ case: durableCase() })] }))
      .mockRejectedValueOnce(new Error("refresh failed"));
    const acceptProposal = vi.fn().mockResolvedValue(acceptResult());
    const { result } = renderHook(() => useCorporateActionInboxPanel(
      fetchInbox,
      acceptProposal,
      () => "idempotency-proposal-100-v7"
    ));

    await waitFor(() => expect(result.current.selectedRow?.key).toBe("proposal-100"));
    act(() => result.current.requestAcceptance(result.current.selectedRow!));
    await act(async () => {
      await result.current.confirmAcceptance();
    });

    expect(acceptProposal).toHaveBeenCalledWith({
      proposalId: "proposal-100",
      expectedVersion: 7,
      idempotencyKey: "idempotency-proposal-100-v7",
      scope: {
        tenantId: "tenant-meridian",
        companyId: "company-alpha",
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        accountingBasis: "GAAP"
      }
    });
    expect(result.current.acceptanceReceipt?.result.audit.auditId).toBe("audit-accepted");
    expect(result.current.acceptanceReceipt?.result.restatement?.candidates[0].periodLabel).toBe("2026-06");
    expect(result.current.acceptanceReceipt?.queueRefreshWarning).toContain("canonical fact was accepted");
    expect(result.current.model?.rows).toHaveLength(1);
  });

  it("retains the accepted case after a successful refresh removes the staged proposal", async () => {
    const fetchInbox = vi.fn()
      .mockResolvedValueOnce(response({ staged: [proposal({ case: durableCase() })] }))
      .mockResolvedValueOnce(response({
        stagedCount: 0,
        staged: [],
        cases: [compactCase({ state: "TermsConfirmed", version: 1 })]
      }));
    const acceptProposal = vi.fn().mockResolvedValue(acceptResult());
    const { result } = renderHook(() => useCorporateActionInboxPanel(
      fetchInbox,
      acceptProposal,
      () => "idempotency-proposal-100-v7"
    ));

    await waitFor(() => expect(result.current.selectedRow?.key).toBe("proposal-100"));
    act(() => result.current.requestAcceptance(result.current.selectedRow!));
    await act(async () => {
      await result.current.confirmAcceptance();
    });

    expect(result.current.acceptanceReceipt?.queueRefreshWarning).toBeNull();
    expect(result.current.model?.rows).toHaveLength(1);
    expect(result.current.selectedRow).toMatchObject({
      key: "proposal-100",
      caseIdLabel: "case-compact-100",
      statusLabel: "TermsConfirmed",
      canAcceptCanonicalFact: false,
      ticker: "GME",
      actionType: "Dividend"
    });
  });
});
