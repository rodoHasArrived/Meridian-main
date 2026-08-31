import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  useCapitalAccountWorkbenchViewModel,
} from "@/screens/accounting-screen.view-model";
import type {
  CapitalAccountWorkbenchServices,
} from "@/screens/accounting-screen.view-model";
import type {
  CapitalAccountWorkbench,
  PrivateCapitalFundEventLedgerRecord,
} from "@/types";

const fundEventLedgerRecord: PrivateCapitalFundEventLedgerRecord = {
  fundEventRecordId: "fund-event-ledger-record:fund-event:fund-alpha:capital-call:20260630",
  fundEventId: "fund-event:fund-alpha:capital-call:20260630",
  fundEventType: "CapitalCall",
  capitalAccountId: "capital-account:fund-alpha:lp-1",
  investorId: "investor:lp-1",
  approvalState: "Draft",
  journalEntryId: "manual-je-1",
  effectiveDate: "2026-06-30",
  currency: "USD",
  grossAmount: 125,
  netCapitalActivity: 125,
  capitalAccountOpeningNetActivity: 0,
  capitalAccountEndingNetActivity: 125,
  memo: "Manual close adjustment",
  paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
  settlementReference: "settlement:fund-alpha:capital-call:20260630",
  activityRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
  evidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet",
  approvalId: null,
  approvalRoute: null,
  isPosted: false,
  isPostingReady: false,
  isReportReady: false,
  isPublished: false,
  readiness: "ApprovalPending",
  readinessLabel: "Approval pending",
  readinessReason: "Submit the fund-event journal for approval before posting or stakeholder report output.",
  nextAction: "Submit approval",
  nextActionRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
  evidenceLinkCount: 2,
  capitalAccountSubledgerEntryCount: 1,
  ledgerImpactCount: 1,
  reportOutputCount: 1,
  validationIssueCount: 2,
  primaryReportOutputId: "report-output:fund-event:fund-alpha:capital-call:20260630:capitalcallnotice",
  primaryReportOutputType: "CapitalCallNotice",
  primaryReportRoute: "/api/ledger/private-capital/report-output?fundProfileId=fund-alpha&reportOutputId=report-output%3Afund-event%3Afund-alpha%3Acapital-call%3A20260630%3Acapitalcallnotice&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
  reportWorkflowState: "Draft",
  publicationManifestId: null,
  retainedManifestPath: null,
  reportLineProvenanceCount: 1,
  evidenceLinks: [
    "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
    "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
  ],
  evidenceCategories: [],
  fundEvent: {
    fundEventId: "fund-event:fund-alpha:capital-call:20260630",
    fundEventType: "CapitalCall",
    entryType: "CapitalCall",
    journalStatus: "Draft",
    journalEntryId: "manual-je-1",
    effectiveDate: "2026-06-30",
    capitalAccountId: "capital-account:fund-alpha:lp-1",
    investorId: "investor:lp-1",
    currency: "USD",
    grossAmount: 125,
    netCapitalActivity: 125,
    memo: "Manual close adjustment",
    paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
    settlementReference: "settlement:fund-alpha:capital-call:20260630",
    evidenceLinks: [
      "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
      "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
    ],
    validationIssues: [],
    updatedAtUtc: "2026-06-30T00:00:00Z"
  },
  capitalAccountSubledgerEntries: [],
  ledgerImpacts: [],
  reportOutputs: [],
  validationIssues: [],
  paymentIntentEvidence: {
    paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
    settlementReference: "settlement:fund-alpha:capital-call:20260630",
    status: "SettlementMatched",
    isReady: true,
    direction: "Inflow",
    amount: 125,
    currency: "USD",
    effectiveDate: "2026-06-30",
    summary: "Cash evidence matched.",
    cashEvidenceLinkCount: 1,
    cashEvidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"],
    requiredEvidence: [],
    evidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet"
  }
};

async function settleWorkbenchRequest(request: Promise<unknown>): Promise<void> {
  await act(async () => {
    await request.catch(() => undefined);
  });
}

function createCapitalAccountWorkbench(
  fundEventRecords: CapitalAccountWorkbench["investorAccounts"][number]["fundEventRecords"]
): CapitalAccountWorkbench {
  return {
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    projectedAtUtc: "2026-06-30T17:00:00Z",
    capitalAccountId: "capital-account:fund-alpha:lp-1",
    investorId: "investor:lp-1",
    currency: "USD",
    workbenchRoute: "/api/ledger/private-capital/capital-account-workbench?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
    statusLabel: "Ready",
    statusReason: "Capital account projection loaded.",
    investorAccountCount: 1,
    fundEventCount: fundEventRecords?.length ?? 0,
    statementCount: 0,
    restatementLineageCount: 0,
    auditDrillThroughCount: 0,
    netCapitalActivity: 125,
    investorAccounts: [{
      accountKey: "capital-account:fund-alpha:lp-1|investor:lp-1|USD",
      capitalAccountId: "capital-account:fund-alpha:lp-1",
      investorId: "investor:lp-1",
      currency: "USD",
      activityRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
      readiness: "Ready",
      readinessLabel: "Ready",
      readinessReason: "Retained evidence and statement support are available.",
      nextAction: "Open statement",
      nextActionRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output-1",
      openingNetActivity: 0,
      endingNetActivity: 125,
      netCapitalActivity: 125,
      contributions: 125,
      distributions: 0,
      subscriptions: 0,
      redemptions: 0,
      managementFees: 0,
      fundEventCount: fundEventRecords?.length ?? 0,
      postedFundEventCount: 0,
      approvalQueueCount: 0,
      publishedReportOutputCount: 0,
      evidenceLinkCount: 0,
      validationIssueCount: 0,
      evidenceCategorySummary: "No evidence categories projected.",
      evidenceLinks: [],
      evidenceCategories: [],
      fundEventRecords,
      subledgerEntries: [],
      ledgerImpacts: [],
      reportOutputs: [],
      validationIssues: [],
      paymentIntentEvidence: null
    }],
    allocationRules: [],
    statementLineage: [],
    auditDrillThroughs: [],
    validationIssues: [],
    liveCapabilities: [],
    plannedCapabilities: []
  };
}

describe("accounting capital account view model", () => {
  it("keeps the workbench unavailable when the first load fails", async () => {
    const request = Promise.reject(new Error("capital accounts offline"));
    const services: CapitalAccountWorkbenchServices = {
      getWorkbench: vi.fn().mockReturnValue(request)
    };

    const { result } = renderHook(() => useCapitalAccountWorkbenchViewModel(true, "", services));

    await settleWorkbenchRequest(request);
    expect(result.current.errorText).toBeTruthy();
    expect(result.current.available).toBe(false);
    expect(result.current.investorAccounts).toEqual([]);
  });

  it("loads the Capital Account Workbench with investor evidence, allocation rules, lineage, and audit drill-through rows", async () => {
    const workbench: CapitalAccountWorkbench = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-alpha",
      projectedAtUtc: "2026-06-30T17:00:00Z",
      capitalAccountId: "capital-account:fund-alpha:lp-1",
      investorId: "investor:lp-1",
      currency: "USD",
      workbenchRoute: "/api/ledger/private-capital/capital-account-workbench?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
      statusLabel: "Restated lineage",
      statusReason: "Statement lineage includes retained restatement metadata and audit evidence.",
      investorAccountCount: 1,
      fundEventCount: 1,
      statementCount: 1,
      restatementLineageCount: 1,
      auditDrillThroughCount: 2,
      netCapitalActivity: 125,
      investorAccounts: [{
        accountKey: "capital-account:fund-alpha:lp-1|investor:lp-1|USD",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        activityRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
        readiness: "Ready",
        readinessLabel: "Ready",
        readinessReason: "Retained evidence and statement support are available.",
        nextAction: "Open statement",
        nextActionRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output-1",
        openingNetActivity: 0,
        endingNetActivity: 125,
        netCapitalActivity: 125,
        contributions: 125,
        distributions: 0,
        subscriptions: 0,
        redemptions: 0,
        managementFees: 0,
        fundEventCount: 1,
        postedFundEventCount: 1,
        approvalQueueCount: 0,
        publishedReportOutputCount: 1,
        evidenceLinkCount: 2,
        validationIssueCount: 0,
        evidenceCategorySummary: "2/2 allocation evidence categories ready.",
        evidenceLinks: ["/evidence/source", "/evidence/cash"],
        evidenceCategories: [],
        fundEventRecords: [fundEventLedgerRecord],
        subledgerEntries: [],
        ledgerImpacts: [],
        reportOutputs: [],
        validationIssues: [],
        paymentIntentEvidence: {
          paymentIntentId: "payment-1",
          settlementReference: "settlement-1",
          status: "SettlementMatched",
          isReady: true,
          direction: "Inflow",
          amount: 125,
          currency: "USD",
          effectiveDate: "2026-06-30",
          summary: "Cash evidence matched.",
          cashEvidenceLinkCount: 1,
          cashEvidenceLinks: ["/evidence/cash"],
          requiredEvidence: [],
          evidenceRoute: "/evidence/payment-1"
        }
      }],
      allocationRules: [{
        ruleId: "rule-source",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        categoryId: "source-support",
        label: "Source support",
        basis: "Fund event source support must be retained.",
        isSatisfied: true,
        reason: "Source support is retained.",
        route: "/evidence/source",
        evidenceLinkCount: 2,
        evidenceLinks: ["/evidence/source"],
        requiredEvidence: ["Source document"],
        ruleVersion: "source-support:projection:1.1.1.1",
        effectiveFrom: "2026-06-30",
        effectiveTo: "2026-06-30",
        formula: "fund_event.source_evidence_count > 0",
        approvalState: "Approved",
        approvalReference: "approval-lp-1 / /accounting/approvals?approvalId=approval-lp-1",
        replayTrace: "Trace uses 1 fund event(s), 1 subledger entry(ies), 1 ledger impact(s), 1 report output(s), and 1 allocation input(s).",
        inputs: [{
          inputId: "allocation-input:fund-event:fund-event:fund-alpha:capital-call",
          kind: "fund-event",
          sourceId: "fund-event:fund-alpha:capital-call",
          label: "CapitalCall / Capital call",
          amount: 125,
          currency: "USD",
          effectiveDate: "2026-06-30",
          evidenceRoute: "/evidence/source"
        }],
        relatedFundEventIds: ["fund-event:fund-alpha:capital-call"]
      }],
      statementLineage: [{
        lineageId: "lineage-1",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        reportOutputId: "report-output-1",
        reportOutputType: "CapitalAccountStatement",
        displayName: "LP 1 Statement",
        reportRoute: "/reporting/report-packs/lp-1",
        reportPackId: "report-pack-1",
        reportWorkflowState: "Restated",
        isPublished: true,
        isReportReady: true,
        publicationManifestId: "manifest-1",
        retainedManifestPath: "/evidence/manifest.json",
        publicationEvidenceHash: "hash-1",
        publishedAtUtc: "2026-06-30T17:00:00Z",
        publishedBy: "publisher",
        reportLineProvenanceCount: 3,
        hasRestatementLineage: true,
        restatementStatus: "Restatement lineage retained.",
        restatementReasonCode: "capital-account-correction",
        restatementPriorVersionReportId: "prior-report",
        restatementApprover: "audit-partner",
        restatementChangedLineCount: 1,
        restatementEvidenceLinkCount: 1,
        reportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output-1",
        evidenceRoute: "/evidence/restatement",
        capitalAccountSubledgerRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
        evidenceLinks: ["/evidence/report"],
        restatementEvidenceLinks: ["/evidence/restatement"],
        restatementChangedLines: [{
          lineKey: "capital-account-ending",
          previousValue: "100.00",
          currentValue: "125.00",
          evidenceLinkCount: 1,
          evidenceLinks: ["/evidence/restatement"]
        }]
      }],
      auditDrillThroughs: [{
        drillThroughId: "drill-subledger",
        kind: "subledger",
        label: "LP 1 subledger",
        summary: "Open capital-account subledger.",
        route: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
        isAvailable: true,
        evidenceLinkCount: 2,
        evidenceLinks: ["/evidence/source", "/evidence/cash"],
        relatedIds: ["fund-event-1"]
      }],
      validationIssues: [],
      liveCapabilities: ["Investor-level capital account evidence", "Statement publication and restatement lineage"],
      plannedCapabilities: ["Full cap-table administration", "Broad LP portal self-service"]
    };
    const request = Promise.resolve(workbench);
    const services: CapitalAccountWorkbenchServices = {
      getWorkbench: vi.fn().mockReturnValue(request)
    };

    const { result } = renderHook(() => useCapitalAccountWorkbenchViewModel(
      true,
      "?capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD",
      services
    ));

    await settleWorkbenchRequest(request);
    expect(result.current.statusLabel).toBe("Restated lineage");
    expect(services.getWorkbench).toHaveBeenCalledWith(expect.objectContaining({
      capitalAccountId: "capital-account:fund-alpha:lp-1",
      investorId: "investor:lp-1",
      currency: "USD"
    }));
    expect(result.current.summaryCards).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "investor-accounts", value: "1" }),
      expect.objectContaining({ id: "statements", detail: "1 restatement lineage" })
    ]));
    expect(result.current.investorAccounts[0]).toMatchObject({
      title: "LP 1 capital account",
      netActivityLabel: "+$125.00 USD",
      paymentEvidenceLabel: "Settlement matched / Inflow / $125 USD / 1 cash evidence / settlement linked"
    });
    expect(result.current.fundEventCommandRows[0]).toMatchObject({
      title: "CapitalCall",
      readinessLabel: "Approval pending",
      readinessReasonLabel: "Submit the fund-event journal for approval before posting or stakeholder report output.",
      nextActionLabel: "Submit approval",
      commandCenterRouteLabel: "/api/ledger/private-capital/fund-event-command-center?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630",
      evidenceRouteLabel: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet",
      ledgerImpactLabel: "1 ledger impact(s)",
      subledgerLabel: "1 subledger movement(s)",
      reportOutputLabel: "1 report output(s)"
    });
    expect(result.current.allocationRules[0]).toMatchObject({
      label: "Source support",
      statusLabel: "Satisfied",
      routeLabel: "/evidence/source",
      policyLabel: "source-support:projection:1.1.1.1",
      effectiveWindowLabel: "2026-06-30 -> 2026-06-30",
      formulaLabel: "fund_event.source_evidence_count > 0",
      approvalLabel: "Approved / approval-lp-1 / /accounting/approvals?approvalId=approval-lp-1",
      inputSummaryLabel: "1 input(s): fund-event fund-event:fund-alpha:capital-call +$125.00 USD",
      relatedFundEventLabel: "fund-event:fund-alpha:capital-call"
    });
    expect(result.current.statementLineage[0]).toMatchObject({
      title: "LP 1 Statement",
      statusLabel: "Restated",
      restatementLabel: "capital-account-correction / 1 changed line(s) / 1 evidence",
      changedLineRows: [
        expect.objectContaining({
          lineKey: "capital-account-ending",
          valueLabel: "100.00 -> 125.00",
          evidenceLabel: "1 changed-line evidence"
        })
      ]
    });
    expect(result.current.auditDrillThroughs[0]).toMatchObject({
      title: "LP 1 subledger",
      statusLabel: "Available"
    });
    expect(result.current.liveCapabilities).toContain("Investor-level capital account evidence");
    expect(result.current.plannedCapabilities).toContain("Broad LP portal self-service");
  });

  it("renders investor accounts when fund-event records are absent from the projection", async () => {
    const workbench = createCapitalAccountWorkbench(null as unknown as CapitalAccountWorkbench["investorAccounts"][number]["fundEventRecords"]);
    const request = Promise.resolve(workbench);
    const services: CapitalAccountWorkbenchServices = {
      getWorkbench: vi.fn().mockReturnValue(request)
    };

    const { result } = renderHook(() => useCapitalAccountWorkbenchViewModel(
      true,
      "?capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD",
      services
    ));

    await settleWorkbenchRequest(request);
    expect(result.current.statusLabel).toBe("Review required");
    expect(result.current.statusTone).toBe("warning");
    expect(result.current.statusReason).toContain("cash-support evidence review");
    expect(result.current.investorAccounts).toHaveLength(1);
    expect(result.current.investorAccounts[0]).toMatchObject({
      statusLabel: "Cash support review",
      statusTone: "warning",
      paymentEvidenceTone: "warning"
    });
    expect(result.current.fundEventCommandRows).toEqual([]);
  });

  it("does not present an empty capital-account projection as ready", async () => {
    const workbench: CapitalAccountWorkbench = {
      ...createCapitalAccountWorkbench([]),
      statusLabel: "Ready",
      statusReason: "Projection request completed.",
      investorAccountCount: 0,
      statementCount: 0,
      restatementLineageCount: 0,
      auditDrillThroughCount: 0,
      netCapitalActivity: 0,
      investorAccounts: [],
      allocationRules: [],
      statementLineage: [],
      auditDrillThroughs: []
    };
    const request = Promise.resolve(workbench);
    const services: CapitalAccountWorkbenchServices = {
      getWorkbench: vi.fn().mockReturnValue(request)
    };

    const { result } = renderHook(() => useCapitalAccountWorkbenchViewModel(true, "", services));

    await settleWorkbenchRequest(request);
    expect(result.current.available).toBe(true);
    expect(result.current.statusLabel).toBe("No activity loaded");
    expect(result.current.statusTone).toBe("warning");
    expect(result.current.statusReason).toContain("no investor accounts, allocation evidence, statement lineage, or audit routes");
    expect(result.current.summaryCards).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "allocation-rules", value: "0", tone: "warning" }),
      expect.objectContaining({ id: "audit-drill-throughs", value: "0", tone: "warning" })
    ]));
  });

  it("keeps ancillary evidence in review when no investor capital account matches", async () => {
    const populated = createCapitalAccountWorkbench([]);
    const workbench: CapitalAccountWorkbench = {
      ...populated,
      statusLabel: "Ready",
      statusReason: "Supporting evidence loaded.",
      investorAccountCount: 0,
      investorAccounts: [],
      auditDrillThroughCount: 1,
      auditDrillThroughs: [{
        drillThroughId: "drill-supporting-evidence",
        kind: "evidence",
        label: "Supporting evidence",
        summary: "Retained support is available, but no investor account matched.",
        route: "/reporting/evidence",
        isAvailable: true,
        evidenceLinkCount: 1,
        evidenceLinks: ["/reporting/evidence"],
        relatedIds: []
      }]
    };
    const request = Promise.resolve(workbench);
    const services: CapitalAccountWorkbenchServices = {
      getWorkbench: vi.fn().mockReturnValue(request)
    };

    const { result } = renderHook(() => useCapitalAccountWorkbenchViewModel(true, "", services));

    await settleWorkbenchRequest(request);
    expect(result.current.available).toBe(true);
    expect(result.current.statusLabel).toBe("Review required");
    expect(result.current.statusTone).toBe("warning");
    expect(result.current.statusReason).toContain("no investor capital accounts matched this scope");
  });
});
