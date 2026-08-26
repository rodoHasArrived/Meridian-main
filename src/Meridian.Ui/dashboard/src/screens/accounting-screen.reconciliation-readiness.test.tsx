import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as readinessApi from "@/lib/api/reconciliation-readiness.api";
import { ReconciliationReadinessPanel } from "@/screens/accounting-screen.reconciliation-readiness";
import type {
  ReconciliationCaseSummary,
  ReconciliationQueueAccountStatus,
  ReconciliationTaxonomySnapshot
} from "@/types/reconciliation-readiness.types";

vi.mock("@/lib/api/reconciliation-readiness.api", () => ({
  getReconciliationQueueStatus: vi.fn(),
  getReconciliationOpenCases: vi.fn(),
  getReconciliationTaxonomy: vi.fn()
}));

const api = vi.mocked(readinessApi);

afterEach(() => {
  vi.resetAllMocks();
});

const account: ReconciliationQueueAccountStatus = {
  accountId: "acct-1",
  accountCode: "FUND-A",
  queueState: "Open",
  unresolvedBreakCount: 3,
  signOffReady: false,
  nextBestAction: "Resolve the three cash breaks raised on 26 Aug.",
  blockerReason: "Custodian statement not received",
  evidenceLinks: ["evidence/run-42"]
};

const openCase: ReconciliationCaseSummary = {
  caseId: "case-1",
  importId: "import-1",
  status: "InReview",
  reason: "Cash balance mismatch",
  confidence: 0.82,
  rationale: "Matched on amount, not on value date.",
  createdAtUtc: "2026-08-24T09:00:00Z",
  assignee: "j.rowe",
  priority: "High",
  slaState: "OnTrack",
  slaDueAtUtc: "2026-08-27T09:00:00Z",
  businessAgeHours: 12.25,
  ageBand: "1-2 days",
  rootCauseCode: "TIMING",
  version: 3
};

const taxonomy: ReconciliationTaxonomySnapshot = {
  version: 4,
  rootCauses: [{ code: "TIMING", displayName: "Settlement timing", version: 4, isActive: true }],
  resolutionCodes: [{ code: "ADJUSTED", displayName: "Ledger adjusted", version: 4, isActive: true }]
};

function primeReads() {
  api.getReconciliationQueueStatus.mockResolvedValue([account]);
  api.getReconciliationOpenCases.mockResolvedValue([openCase]);
  api.getReconciliationTaxonomy.mockResolvedValue(taxonomy);
}

describe("ReconciliationReadinessPanel", () => {
  it("shows per-account readiness with the server's next action and blocker", async () => {
    primeReads();
    render(<ReconciliationReadinessPanel />);

    expect(await screen.findByText("FUND-A")).toBeInTheDocument();
    expect(screen.getByText("Resolve the three cash breaks raised on 26 Aug.")).toBeInTheDocument();
    expect(screen.getByText(/Blocked: Custodian statement not received/)).toBeInTheDocument();
    expect(screen.getByText("Not ready")).toBeInTheDocument();
  });

  it("resolves a case's root-cause code through the taxonomy", async () => {
    primeReads();
    render(<ReconciliationReadinessPanel />);

    expect(await screen.findByText("Settlement timing")).toBeInTheDocument();
  });

  it("names each failed read separately so a degraded label is distinguishable from a missing verdict", async () => {
    api.getReconciliationQueueStatus.mockResolvedValue([account]);
    api.getReconciliationOpenCases.mockResolvedValue([openCase]);
    api.getReconciliationTaxonomy.mockRejectedValue(new Error("taxonomy route unavailable"));
    render(<ReconciliationReadinessPanel />);

    expect(await screen.findByText(/Taxonomy: taxonomy route unavailable/)).toBeInTheDocument();
    expect(screen.queryByText(/Queue status:/)).not.toBeInTheDocument();
    // The readiness verdict survived, so this is a gap, not an outage.
    expect(screen.getByText("Queue readiness loaded with gaps")).toBeInTheDocument();
    // The code still renders, as recorded, rather than claiming it is unknown.
    expect(screen.getByText("TIMING")).toBeInTheDocument();
  });

  it("calls the panel unavailable when the readiness verdict itself did not load", async () => {
    api.getReconciliationQueueStatus.mockRejectedValue(new Error("queue route unavailable"));
    api.getReconciliationOpenCases.mockResolvedValue([openCase]);
    api.getReconciliationTaxonomy.mockResolvedValue(taxonomy);
    render(<ReconciliationReadinessPanel />);

    expect(await screen.findByText("Queue readiness unavailable")).toBeInTheDocument();
    expect(screen.getByText(/Queue status: queue route unavailable/)).toBeInTheDocument();
  });

  it("raises the blocked notice only when an account reports a blocker", async () => {
    primeReads();
    const { unmount } = render(<ReconciliationReadinessPanel />);
    expect(await screen.findByText(/must clear before sign-off/)).toBeInTheDocument();
    unmount();

    vi.resetAllMocks();
    api.getReconciliationQueueStatus.mockResolvedValue([{ ...account, blockerReason: "", signOffReady: true }]);
    api.getReconciliationOpenCases.mockResolvedValue([]);
    api.getReconciliationTaxonomy.mockResolvedValue(taxonomy);
    render(<ReconciliationReadinessPanel />);

    expect(await screen.findByText("Ready")).toBeInTheDocument();
    expect(screen.queryByText(/must clear before sign-off/)).not.toBeInTheDocument();
  });

  it("refetches all three reads on refresh", async () => {
    primeReads();
    render(<ReconciliationReadinessPanel />);
    await screen.findByText("FUND-A");

    await userEvent.click(screen.getByRole("button", { name: /Refresh/ }));

    await waitFor(() => expect(api.getReconciliationQueueStatus).toHaveBeenCalledTimes(2));
    expect(api.getReconciliationOpenCases).toHaveBeenCalledTimes(2);
    expect(api.getReconciliationTaxonomy).toHaveBeenCalledTimes(2);
  });
});
