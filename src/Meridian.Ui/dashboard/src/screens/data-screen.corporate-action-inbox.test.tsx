import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { CorporateActionInboxRegion, CORPORATE_ACTION_CASE_WORKSPACE_ID } from "@/screens/data-screen.corporate-action-inbox";
import { useCorporateActionInboxPanel } from "@/screens/data-screen.corporate-action-inbox.view-model";
import type {
  CorporateActionInboxAcceptResult,
  CorporateActionInboxAcceptRequest,
  CorporateActionInboxResponse,
  CorporateActionProcessingCaseDto,
  CorporateActionProposalEntry
} from "@/types";

const FAN_OUT_BLOCKER =
  "Corporate-action source decisions are read-only until an authoritative service can enumerate every affected tenant/company scope and apply the decision atomically.";

function proposal(overrides: Partial<CorporateActionProposalEntry> = {}): CorporateActionProposalEntry {
  return {
    securityId: "sec-aapl",
    ticker: "AAPL",
    actionType: "Dividend",
    exDate: "2026-07-20",
    recordDate: "2026-07-21",
    payableDate: "2026-08-01",
    amount: 0.25,
    currency: "USD",
    splitFromFactor: null,
    splitToFactor: null,
    winningSource: "exchange-feed",
    agreeingSources: ["exchange-feed", "custodian-feed"],
    dissentingSources: [],
    autoApplied: false,
    proposalId: "proposal-aapl-dividend-v3",
    version: 3,
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
  };
}

function inbox(): CorporateActionInboxResponse {
  return {
    lastIngestAt: "2026-07-05T12:00:00Z",
    stagedCount: 2,
    appliedLastRun: 0,
    duplicatesSkippedLastRun: 1,
    staged: [
      proposal(),
      proposal({
        securityId: "sec-gme",
        ticker: "GME",
        exDate: "2026-08-14",
        amount: 0.24,
        agreeingSources: ["finnhub"],
        dissentingSources: ["custodian-feed"],
        winningSource: "finnhub",
        proposalId: "proposal-gme-dividend-v2",
        version: 2,
        actionAvailability: null
      })
    ],
    errors: ["custodian-feed: timed out"],
    cases: [processingCase()]
  };
}

function processingCase(overrides: Partial<CorporateActionProcessingCaseDto> = {}): CorporateActionProcessingCaseDto {
  return {
    caseId: "case-aapl-dividend",
    proposalId: "proposal-aapl-dividend-v3",
    corporateActionId: "ca-aapl-dividend",
    securityId: "sec-aapl",
    scope: {
      tenantId: "tenant-meridian",
      companyId: "company-alpha"
    },
    state: "AccountingReview",
    version: 1,
    methodologyProfileId: "clearwater-corporate-actions/v1",
    assignedTo: "Avery Reviewer",
    blockedReason: null,
    createdBy: "avery.reviewer",
    createdAtUtc: "2026-07-05T12:05:00Z",
    updatedBy: "avery.reviewer",
    updatedAtUtc: "2026-07-05T12:05:00Z",
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
        corpActId: "observed-aapl-dividend",
        securityId: "sec-aapl",
        eventType: "Dividend",
        exDate: "2026-07-20",
        payDate: "2026-08-01",
        dividendPerShare: 0.25,
        currency: "USD",
        splitRatio: null,
        newSecurityId: null,
        distributionRatio: null,
        acquirerSecurityId: null,
        exchangeRatio: null,
        subscriptionPricePerShare: null,
        rightsPerShare: null,
        recordDate: "2026-07-21",
        lifecycleState: "Announced",
        supersedesCorpActId: null,
        redemptionPricePercentOfPar: null,
        payload: { declaredAmount: 0.25 },
        payloadSchemaVersion: 1
      },
      providerIdentity: {
        providerId: "exchange-feed",
        sourceEventId: "event-aapl-dividend",
        sourceEventVersion: "v3",
        observedAtUtc: "2026-07-05T09:55:00Z",
        evidenceHash: "sha256:evidence-dividend-25",
        evidenceReference: "evidence://dividend-25",
        releaseStatus: "AcceptanceEligible"
      },
      displayMetadata: {
        ticker: "AAPL",
        winningSource: "exchange-feed",
        agreeingSources: ["exchange-feed", "custodian-feed"],
        dissentingSources: []
      }
    },
    ...overrides
  };
}

function acceptResult(): CorporateActionInboxAcceptResult {
  return {
    corporateAction: {
      corpActId: "ca-aapl-dividend",
      securityId: "sec-aapl",
      eventType: "Dividend",
      exDate: "2026-07-20",
      payDate: "2026-08-01",
      dividendPerShare: 0.25,
      currency: "USD",
      splitRatio: null,
      newSecurityId: null,
      distributionRatio: null,
      acquirerSecurityId: null,
      exchangeRatio: null,
      subscriptionPricePerShare: null,
      rightsPerShare: null,
      recordDate: "2026-07-21",
      lifecycleState: "Announced",
      supersedesCorpActId: null,
      redemptionPricePercentOfPar: null,
      payload: { declaredAmount: 0.25 },
      payloadSchemaVersion: 1
    },
    audit: {
      auditId: "audit-aapl-dividend",
      securityId: "sec-aapl",
      corporateActionId: "ca-aapl-dividend",
      eventType: "Dividend",
      sourceSystem: "exchange-feed",
      actor: "avery.reviewer",
      recordedAtUtc: "2026-07-05T12:05:00Z",
      sourceRecordId: "proposal-aapl-dividend-v3",
      reason: "Accepted canonical fact",
      correlationId: "correlation-aapl"
    },
    restatement: {
      restatementRequired: true,
      candidates: [{
        reportId: "report-june-v2",
        priorVersionReportId: "report-june-v1",
        periodLabel: "2026-06",
        summary: "Dividend disclosure changed",
        changedLines: [{
          lineKey: "dividend-income",
          previousValue: "0",
          currentValue: "250"
        }]
      }]
    }
  };
}

async function defaultFetchInbox() {
  return inbox();
}

async function defaultAcceptProposal() {
  return acceptResult();
}

function defaultIdempotencyKey() {
  return "test-a";
}

function Harness({
  fetchInbox = defaultFetchInbox,
  acceptProposal = defaultAcceptProposal
}: {
  fetchInbox?: () => Promise<CorporateActionInboxResponse>;
  acceptProposal?: (request: CorporateActionInboxAcceptRequest) => Promise<CorporateActionInboxAcceptResult>;
}) {
  const panel = useCorporateActionInboxPanel(fetchInbox, acceptProposal, defaultIdempotencyKey);
  return <CorporateActionInboxRegion panel={panel} />;
}

describe("CorporateActionInboxRegion", () => {
  it("preserves source and case review while the server locks source decisions", async () => {
    const user = userEvent.setup();
    const acceptProposal = vi.fn().mockResolvedValue(acceptResult());
    const fetchInbox = vi.fn().mockResolvedValue({
      ...inbox(),
      stagedCount: 1,
      staged: [proposal({
        actionAvailability: {
          canAccept: false,
          canReject: false,
          canCompareEvidence: true,
          blockers: [FAN_OUT_BLOCKER]
        }
      })],
      errors: []
    });
    render(<Harness fetchInbox={fetchInbox} acceptProposal={acceptProposal} />);

    const row = await screen.findByRole("row", { name: /Inspect corporate action case for Dividend on AAPL/i });
    await user.click(row);

    expect(screen.getAllByText("case-aapl-dividend").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Denied by server policy").length).toBeGreaterThan(0);
    const sourceFacts = screen.getByRole("table", { name: "Corporate action source facts and provenance" });
    expect(within(sourceFacts).getByText("0.25 USD")).toBeInTheDocument();
    const accept = screen.getByRole("button", { name: "Accept canonical fact" });
    expect(accept).toBeDisabled();
    expect(accept).toHaveAttribute("title", FAN_OUT_BLOCKER);
    expect(screen.queryByRole("button", { name: /reject/i })).not.toBeInTheDocument();

    await user.click(accept);
    expect(screen.queryByRole("dialog", { name: /Accept Dividend as a canonical fact/i })).not.toBeInTheDocument();
    expect(acceptProposal).not.toHaveBeenCalled();
  });

  it("supports dense keyboard drill-in, locked server actions, precise acceptance, and receipt proof", async () => {
    const user = userEvent.setup();
    const acceptProposal = vi.fn().mockResolvedValue(acceptResult());
    render(<Harness acceptProposal={acceptProposal} />);

    expect((await screen.findAllByText("case-aapl-dividend")).length).toBeGreaterThan(0);
    expect(screen.getByText("Provider ingest partially succeeded")).toBeInTheDocument();
    expect(screen.getAllByText("AccountingReview").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Avery Reviewer").length).toBeGreaterThan(0);
    expect(screen.getAllByText("v3").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Bulk accept" })).toBeDisabled();

    const aaplRow = screen.getByRole("row", { name: /Inspect corporate action case for Dividend on AAPL/i });
    aaplRow.focus();
    await user.keyboard("{ArrowDown}");
    const gmeRow = screen.getByRole("row", { name: /Inspect corporate action case for Dividend on GME/i });
    expect(gmeRow).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{Enter}");
    await waitFor(() => expect(document.getElementById(CORPORATE_ACTION_CASE_WORKSPACE_ID)).toHaveFocus());
    expect(screen.getAllByText("Authorization not supplied").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Accept canonical fact" })).toBeDisabled();
    await user.keyboard("{Escape}");
    expect(gmeRow).toHaveFocus();

    await user.click(aaplRow);
    expect(screen.getByText("Entitlement not supplied")).toBeInTheDocument();
    expect(screen.getByText("Basis treatment not supplied")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Submit election" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve treatment" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Post accounting" })).not.toBeInTheDocument();
    expect(screen.getByText(/Election submission, treatment approval, and accounting posting remain outside/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Accept canonical fact" }));

    const dialog = screen.getByRole("dialog", { name: "Accept Dividend as a canonical fact?" });
    expect(within(dialog).getByText(/does not confirm entitlement/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/does not confirm entitlement, submit an election, approve GAAP, STAT, tax, or management treatment, change lots, or post journals/i)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: /Accept Dividend for AAPL as a canonical Security Master fact/i }));

    expect(await screen.findByText("Canonical fact accepted")).toBeInTheDocument();
    expect(screen.getByText(/audit audit-aapl-dividend/i)).toBeInTheDocument();
    expect(screen.getByText(/Restatement review required · 1 candidate/i)).toBeInTheDocument();
    expect(acceptProposal).toHaveBeenCalledWith({
      proposalId: "proposal-aapl-dividend-v3",
      expectedVersion: 3,
      idempotencyKey: "test-a",
      scope: {
        tenantId: "tenant-meridian",
        companyId: "company-alpha"
      }
    });
  });

  it("has no automated accessibility violations in loaded case-workspace state", async () => {
    const { container } = render(<Harness />);
    await screen.findAllByText("case-aapl-dividend");
    await waitFor(() => expect(screen.getByRole("treegrid", { name: "Corporate action case queue" })).toBeInTheDocument());

    const results = await axe(container);
    expect(results.violations).toEqual([]);
  });

  it("surfaces accepted provider dissent and pending period validation in the receipt", async () => {
    const user = userEvent.setup();
    const result = acceptResult();
    const acceptProposal = vi.fn().mockResolvedValue({
      ...result,
      sourceConflict: {
        conflictId: "conflict-provider-dissent",
        caseId: "case-aapl-dividend",
        field: "ProviderConsensus",
        description: "Provider observations disagree on the event economics.",
        candidates: [
          { source: "exchange-feed", value: { amount: 0.25 } },
          { source: "custodian-feed", value: { amount: 0.24 } }
        ],
        state: "Open",
        resolution: null,
        caseVersion: 1,
        recordedBy: "avery.reviewer",
        recordedAtUtc: "2026-07-05T12:05:00Z"
      },
      restatement: {
        ...result.restatement!,
        evaluationStatus: "PendingPeriodValidation"
      }
    });
    render(<Harness acceptProposal={acceptProposal} />);

    const row = await screen.findByRole("row", { name: /Inspect corporate action case for Dividend on AAPL/i });
    await user.click(row);
    await user.click(screen.getByRole("button", { name: "Accept canonical fact" }));
    const dialog = screen.getByRole("dialog", { name: "Accept Dividend as a canonical fact?" });
    await user.click(within(dialog).getByRole("button", { name: /Accept Dividend for AAPL as a canonical Security Master fact/i }));

    expect(await screen.findByText("Canonical fact accepted; source conflict requires resolution")).toBeInTheDocument();
    expect(screen.getByText("Open source conflict · ProviderConsensus")).toBeInTheDocument();
    expect(screen.getByText(/conflict-provider-dissent · exchange-feed, custodian-feed/i)).toBeInTheDocument();
    expect(screen.getByText(/Period validation pending · 1 candidate/i)).toBeInTheDocument();
  });

  it("renders accepted durable cases returned outside the staged collection", async () => {
    const fetchInbox = vi.fn().mockResolvedValue({
      ...inbox(),
      stagedCount: 0,
      staged: [],
      cases: [processingCase({
        caseId: "case-accepted-aapl-dividend",
        proposalId: "proposal-accepted-aapl-dividend",
        state: "TermsConfirmed"
      })]
    });
    render(<Harness fetchInbox={fetchInbox} />);

    expect((await screen.findAllByText("case-accepted-aapl-dividend")).length).toBeGreaterThan(0);
    expect(screen.getAllByText("TermsConfirmed").length).toBeGreaterThan(0);
    expect(screen.getByText(/1 durable processing case; no staged corporate actions awaiting review/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Accept canonical fact" })).toBeDisabled();
  });
});
