import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  buildCorporateActionInboxModel,
  filterCorporateActionInboxRows,
  useCorporateActionInboxPanel
} from "./data-screen.corporate-action-inbox.view-model";
import type {
  CorporateActionInboxAcceptResult,
  CorporateActionInboxResponse,
  CorporateActionProcessingCaseDto,
  CorporateActionProposalEntry
} from "@/types";

const FAN_OUT_BLOCKER =
  "Corporate-action source decisions are read-only until an authoritative service can enumerate every affected tenant/company scope and apply the decision atomically.";

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
    sourceSnapshot: {
      proposedAction: {
        corpActId: "observed-action-100",
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
      providerIdentity: {
        providerId: "finnhub",
        sourceEventId: "event-100",
        sourceEventVersion: "v3",
        observedAtUtc: "2026-07-05T11:55:00Z",
        evidenceHash: "sha256:evidence-100",
        evidenceReference: "evidence://event-100/v3",
        releaseStatus: "AcceptanceEligible"
      },
      displayMetadata: {
        ticker: "GME",
        winningSource: "finnhub",
        agreeingSources: ["finnhub"],
        dissentingSources: []
      }
    },
    ...overrides
  };
}

function actionableProposal(overrides: Partial<CorporateActionProposalEntry> = {}): CorporateActionProposalEntry {
  return proposal({
    proposalId: "proposal-100",
    version: 7,
    proposalState: "ReviewRequired",
    acceptanceScope: {
      tenantId: "tenant-meridian",
      companyId: "company-alpha"
    },
    actionAvailability: {
      canAccept: true,
      canReject: true,
      canCompareEvidence: true,
      blockers: []
    },
    ...overrides
  });
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

  it("projects staged command identity alongside the matching durable case", () => {
    const model = buildCorporateActionInboxModel(response({
      staged: [actionableProposal()],
      cases: [compactCase()]
    }), new Date("2026-07-05T15:00:00Z"));

    expect(model.rows[0]).toMatchObject({
      rowId: "proposal-100",
      caseIdLabel: "case-compact-100",
      proposalIdLabel: "proposal-100",
      versionLabel: "v7",
      statusLabel: "TermsConfirmed",
      assignmentLabel: "Casey Operator",
      conflictLabel: "Not supplied",
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

  it("preserves server blockers even when the availability flag is inconsistent", () => {
    const model = buildCorporateActionInboxModel(response({
      staged: [actionableProposal({
        actionAvailability: {
          canAccept: true,
          canReject: true,
          canCompareEvidence: true,
          blockers: ["Provider evidence is still under review."]
        }
      })]
    }));

    expect(model.rows[0]).toMatchObject({
      permissionLabel: "Denied by server policy",
      canAcceptCanonicalFact: false,
      acceptCanonicalFactDisabledReason: "Provider evidence is still under review."
    });
  });

  it("keeps proposal review readable but never prepares a decision while fan-out authority is unavailable", async () => {
    const fetchInbox = vi.fn().mockResolvedValue(response({
      staged: [actionableProposal({
        actionAvailability: {
          canAccept: false,
          canReject: false,
          canCompareEvidence: true,
          blockers: [FAN_OUT_BLOCKER]
        }
      })],
      cases: [compactCase()]
    }));
    const acceptProposal = vi.fn().mockResolvedValue(acceptResult());
    const { result } = renderHook(() => useCorporateActionInboxPanel(
      fetchInbox,
      acceptProposal,
      () => "test-b"
    ));

    await waitFor(() => expect(result.current.selectedRow?.rowId).toBe("proposal-100"));
    expect(result.current.selectedRow).toMatchObject({
      ticker: "GME",
      actionType: "Dividend",
      valueLabel: "0.24 USD",
      caseIdLabel: "case-compact-100",
      permissionLabel: "Denied by server policy",
      canAcceptCanonicalFact: false,
      acceptCanonicalFactDisabledReason: FAN_OUT_BLOCKER
    });

    act(() => result.current.requestAcceptance(result.current.selectedRow!));
    await act(async () => {
      await result.current.confirmAcceptance();
    });

    expect(result.current.pendingAcceptance).toBeNull();
    expect(acceptProposal).not.toHaveBeenCalled();
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
      rowId: "proposal-100",
      caseIdLabel: "case-compact-100",
      proposalIdLabel: "proposal-100",
      versionLabel: "v4",
      statusLabel: "TermsConfirmed",
      ticker: "GME",
      canAcceptCanonicalFact: true
    });
    expect(model.rows[0].compactCase?.corporateActionId).toBe("corporate-action-100");
  });

  it("never falls back to a processing-case version or availability for proposal acceptance", () => {
    const model = buildCorporateActionInboxModel(response({
      staged: [proposal({
        proposalId: "proposal-100",
        acceptanceScope: { tenantId: "tenant-meridian", companyId: "company-alpha" },
        actionAvailability: { canAccept: true, canReject: true, canCompareEvidence: true, blockers: [] }
      })],
      cases: [compactCase({ version: 99 })]
    }));

    expect(model.rows[0]).toMatchObject({
      versionLabel: "Not supplied",
      expectedVersion: null,
      canAcceptCanonicalFact: false,
      acceptCanonicalFactDisabledReason: "Server did not supply the proposal version required for concurrency control."
    });
  });

  it("keeps an accepted durable case visible after its proposal leaves staged", () => {
    const model = buildCorporateActionInboxModel(response({
      stagedCount: 0,
      staged: [],
      cases: [compactCase({ state: "AccountingReview", version: 5 })]
    }));

    expect(model.rows).toHaveLength(1);
    expect(model.rows[0]).toMatchObject({
      rowId: "proposal-100",
      caseIdLabel: "case-compact-100",
      versionLabel: "case v5",
      statusLabel: "AccountingReview",
      expectedVersion: null,
      canAcceptCanonicalFact: false,
      ticker: "GME",
      actionType: "Dividend",
      sourceEventLabel: "event-100 · v3",
      sourceEvidenceReference: "evidence://event-100/v3"
    });
    expect(model.rows[0].durableCase?.status).toBe("AccountingReview");
    expect(model.summary).toContain("1 durable processing case");
  });

  it("filters case rows by search, status, assignment, and conflict without changing source order", () => {
    const model = buildCorporateActionInboxModel(response({
      stagedCount: 2,
      staged: [
        actionableProposal({ ticker: "MSFT", exDate: "2026-09-15" }),
        actionableProposal({
          proposalId: "proposal-aapl",
          ticker: "AAPL",
          exDate: "2026-07-20",
          dissentingSources: ["custodian-feed"]
        })
      ],
      cases: [
        compactCase({ state: "AccountingReview" }),
        compactCase({
          caseId: "case-aapl",
          proposalId: "proposal-aapl",
          state: "Disputed",
          assignedTo: null
        })
      ]
    }), new Date("2026-07-05T15:00:00Z"));

    const filtered = filterCorporateActionInboxRows(model.rows, {
      search: "aapl",
      status: "Disputed",
      assignment: "Unassigned",
      conflict: "Source dissent"
    });

    expect(filtered.map((row) => row.ticker)).toEqual(["AAPL"]);
    expect(model.rows.map((row) => row.ticker)).toEqual(["AAPL", "MSFT"]);
  });

  it("retains the append receipt and reports partial success when the queue refresh fails", async () => {
    const fetchInbox = vi.fn()
      .mockResolvedValueOnce(response({ staged: [actionableProposal()] }))
      .mockRejectedValueOnce(new Error("refresh failed"));
    const acceptProposal = vi.fn().mockResolvedValue(acceptResult());
    const { result } = renderHook(() => useCorporateActionInboxPanel(
      fetchInbox,
      acceptProposal,
      () => "test-b"
    ));

    await waitFor(() => expect(result.current.selectedRow?.rowId).toBe("proposal-100"));
    act(() => result.current.requestAcceptance(result.current.selectedRow!));
    await act(async () => {
      await result.current.confirmAcceptance();
    });

    expect(acceptProposal).toHaveBeenCalledWith({
      proposalId: "proposal-100",
      expectedVersion: 7,
      idempotencyKey: "test-b",
      scope: {
        tenantId: "tenant-meridian",
        companyId: "company-alpha"
      }
    });
    expect(result.current.acceptanceReceipt?.result.audit.auditId).toBe("audit-accepted");
    expect(result.current.acceptanceReceipt?.result.restatement?.candidates[0].periodLabel).toBe("2026-06");
    expect(result.current.acceptanceReceipt?.queueRefreshWarning).toContain("canonical fact was accepted");
    expect(result.current.model?.rows).toHaveLength(1);
  });

  it("retains the accepted case after a successful refresh removes the staged proposal", async () => {
    const fetchInbox = vi.fn()
      .mockResolvedValueOnce(response({ staged: [actionableProposal()] }))
      .mockResolvedValueOnce(response({
        stagedCount: 0,
        staged: [],
        cases: [compactCase({ state: "TermsConfirmed", version: 1 })]
      }));
    const acceptProposal = vi.fn().mockResolvedValue(acceptResult());
    const { result } = renderHook(() => useCorporateActionInboxPanel(
      fetchInbox,
      acceptProposal,
      () => "test-b"
    ));

    await waitFor(() => expect(result.current.selectedRow?.rowId).toBe("proposal-100"));
    act(() => result.current.requestAcceptance(result.current.selectedRow!));
    await act(async () => {
      await result.current.confirmAcceptance();
    });

    expect(result.current.acceptanceReceipt?.queueRefreshWarning).toBeNull();
    expect(result.current.model?.rows).toHaveLength(1);
    expect(result.current.selectedRow).toMatchObject({
      rowId: "proposal-100",
      caseIdLabel: "case-compact-100",
      statusLabel: "TermsConfirmed",
      canAcceptCanonicalFact: false,
      ticker: "GME",
      actionType: "Dividend"
    });
  });
});
