import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as onboarding from "@/lib/api/accounting-onboarding.api";
import { AccountingOnboardingPanel } from "./accounting-screen.onboarding-panel";
import type { OnboardingComparison, OnboardingDifference, OnboardingReadinessPacket, OnboardingWorkspace } from "@/types/accounting-onboarding";

vi.mock("@/lib/api/accounting-onboarding.api", () => ({
  listOnboardingWorkspaces: vi.fn(), getOnboardingWorkspace: vi.fn(), getOnboardingSources: vi.fn(), createOnboardingWorkspace: vi.fn(),
  updateOnboardingCriteria: vi.fn(), captureOnboardingComparison: vi.fn(), assignOnboardingDifference: vi.fn(),
  reviewOnboardingWorkspace: vi.fn(), freezeOnboardingPacket: vi.fn(), replayOnboardingComparison: vi.fn(), downloadOnboardingPacket: vi.fn()
}));
const api = vi.mocked(onboarding);
const source = { providerId: "custodian", importId: "import-feb", asOfDate: "2026-02-28", mappingProfileId: "mapping-1", mappingVersion: "v2" };
const difference: OnboardingDifference = {
  differenceKey: "Balance:account-1:USD", kind: "Balance", accountId: "account-1", currency: "USD", instrumentId: null,
  meridianAmount: 100, externalAmount: 105, difference: -5, tolerance: 0, status: "New", ownerId: "owner",
  firstSeenComparisonId: "comparison-jan", lastSeenComparisonId: "comparison-jan", evidenceIds: ["evidence-jan"], missingReason: null
};
function workspace(overrides: Partial<OnboardingWorkspace> = {}): OnboardingWorkspace {
  return {
    workspaceId: "workspace-1", name: "Quarter one onboarding", ownerId: "owner", version: 1, dataRevision: 1,
    createdAtUtc: "2026-01-01T00:00:00Z", updatedAtUtc: "2026-01-01T00:00:00Z",
    scope: { tenantId: "tenant-1", companyId: "company-1", entityId: "entity-1", fundProfileId: "profile-1", ledgerBookId: "book-1", accountIds: ["account-1"], startDate: "2026-01-01", endDate: "2026-03-31" },
    criteria: { requiredDates: ["2026-01-31", "2026-02-28"], requiredKinds: ["Balance", "Position", "Nav"], balanceTolerance: 0, positionTolerance: 0, navTolerance: 0, minimumCoveragePercent: 100, requiredReviewerIds: ["reviewer"], minimumReviewers: 1, requireCloseReadiness: true, reviewInstructions: "Reconcile both month ends." },
    criteriaHistory: [], comparisons: [], currentDifferences: [], assignments: [], reviews: [], packets: [],
    readiness: { status: "Blocked", isReady: false, unresolvedDifferenceCount: 0, missingDates: ["2026-01-31", "2026-02-28"], missingSources: [], blockers: ["Required comparisons are missing."], approvedReviewers: 0, requiredReviewers: 1, unresolvedDifferences: [] },
    authorityPosture: "ReadOnlySupport", ...overrides
  };
}
function comparison(): OnboardingComparison {
  return {
    comparisonId: "comparison-jan", sequence: 1, asOfDate: "2026-01-31", dataRevision: 2,
    actorId: "owner", capturedAtUtc: "2026-02-01T00:00:00Z", notes: "First import", providerId: "custodian", importId: "import-jan", mappingProfileId: "mapping-1", mappingVersion: "v1",
    criteria: workspace().criteria, inputs: { snapshots: [{ snapshotId: "snapshot-jan", sourceKind: "ExternalBalances", sourceId: "import-jan", version: "source-v1", contentHash: "source-hash-january", capturedAtUtc: "2026-02-01T00:00:00Z", asOfDate: "2026-01-31", mappingVersion: "v1", payloadJson: '{"balance":105}', evidenceIds: ["evidence-jan"] }], observations: [], missingSources: [], closeReadiness: null },
    differences: [difference], coveragePercent: 100, contentHash: "comparison-hash-january", previousContentHash: null, algorithmVersion: "onboarding-v1"
  };
}
function packet(value: OnboardingWorkspace): OnboardingReadinessPacket {
  return { packetId: "packet-1", frozenAtUtc: "2026-03-01T12:00:00Z", frozenBy: "owner", content: { ...value, workspaceVersion: value.version }, contentHash: "frozen-sha256", hashAlgorithm: "SHA-256" };
}
async function selectWorkspace() {
  await waitFor(() => expect(screen.getByLabelText("Retained workspace")).toBeEnabled());
  await userEvent.selectOptions(screen.getByLabelText("Retained workspace"), "workspace-1");
  await screen.findByRole("region", { name: "Onboarding Quarter one onboarding" });
}
beforeEach(() => {
  vi.resetAllMocks();
  api.listOnboardingWorkspaces.mockResolvedValue([workspace()]);
  api.getOnboardingSources.mockResolvedValue([source]);
});

describe("bounded accounting onboarding", () => {
  it("shows an accessible empty state and sends explicit scope and owner criteria without an actor or tenant", async () => {
    api.listOnboardingWorkspaces.mockResolvedValue([]);
    api.createOnboardingWorkspace.mockResolvedValue(workspace());
    const { container } = render(<AccountingOnboardingPanel />);
    await screen.findByText("No onboarding workspaces retained. Define a bounded scope to begin.");
    await userEvent.click(screen.getByText("Define onboarding scope"));
    const form = within(screen.getByRole("form", { name: "Create onboarding workspace" }));
    for (const [label, value] of [["Workspace name", "Quarter one onboarding"], ["Entity ID", "entity-1"], ["Book profile ID", "profile-1"], ["Ledger book ID", "book-1"], ["Financial account IDs", "account-1, account-2"], ["Start date", "2026-01-01"], ["End date", "2026-03-31"], ["Required dates", "2026-01-31, 2026-02-28"], ["Review criteria", "Reconcile both month ends."]]) {
      await userEvent.type(form.getByLabelText(label), value);
    }
    expect((await axe(container)).violations).toEqual([]);
    await userEvent.click(form.getByRole("button", { name: "Create workspace" }));
    await waitFor(() => expect(api.createOnboardingWorkspace).toHaveBeenCalled());
    const [request] = api.createOnboardingWorkspace.mock.calls[0];
    expect(request.scope.accountIds).toEqual(["account-1", "account-2"]);
    expect(request.criteria.requiredDates).toEqual(["2026-01-31", "2026-02-28"]);
    expect(request.criteria.requiredKinds).toEqual(["Balance", "Position", "Nav"]);
    expect(request).not.toHaveProperty("actor");
    expect(request.scope).not.toHaveProperty("tenantId");
    expect(await screen.findByText("Readiness: Blocked")).toBeInTheDocument();
  });

  it("captures exact selected import identities and preserves historical results while replaying their retained inputs", async () => {
    const initial = workspace({ version: 2, dataRevision: 2, comparisons: [comparison()], currentDifferences: [difference] });
    const february = { ...comparison(), comparisonId: "comparison-feb", sequence: 2, asOfDate: "2026-02-28", mappingVersion: "v2", contentHash: "comparison-hash-february", differences: [{ ...difference, status: "Persistent" }] };
    api.listOnboardingWorkspaces.mockResolvedValue([initial]);
    api.captureOnboardingComparison.mockResolvedValue({ ...initial, version: 3, dataRevision: 3, comparisons: [comparison(), february] });
    api.replayOnboardingComparison.mockResolvedValue(comparison());
    render(<AccountingOnboardingPanel />);
    await selectWorkspace();
    await userEvent.selectOptions(screen.getByLabelText("Retained comparison"), "comparison-jan");
    await userEvent.click(screen.getByText("Capture next comparison"));
    await userEvent.selectOptions(screen.getByLabelText("Available retained source"), "0");
    await userEvent.click(screen.getByRole("button", { name: "Capture comparison" }));
    await waitFor(() => expect(api.captureOnboardingComparison).toHaveBeenCalledWith("workspace-1", { ...source, expectedVersion: 2, notes: "" }, expect.objectContaining({ signal: expect.any(AbortSignal) })));
    expect(screen.getByLabelText("Retained comparison")).toHaveValue("comparison-jan");
    await userEvent.click(screen.getByText("Exact source snapshots and mapping"));
    expect(screen.getByText(/custodian · import-jan · mapping-1 · v1/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Reproduce selected comparison" }));
    expect(await screen.findByText("Comparison #1 reproduced from its retained snapshots and mapping.")).toBeInTheDocument();
    expect(api.replayOnboardingComparison).toHaveBeenCalledWith("workspace-1", "comparison-jan", expect.anything());
  });

  it("retains ownership evidence and authenticated reviewer decisions, then freezes the server packet", async () => {
    const initial = workspace({ version: 2, dataRevision: 2, comparisons: [comparison()], currentDifferences: [difference] });
    const assigned = { ...initial, version: 3, dataRevision: 3, currentDifferences: [{ ...difference, ownerId: "analyst", evidenceIds: ["evidence-jan", "receipt-42"] }] };
    const reviewed = { ...assigned, version: 4, reviews: [{ reviewId: "review-1", dataRevision: 3, reviewerId: "reviewer", decision: "ChangesRequested", notes: "Cash adjustment needs proof.", evidenceIds: ["receipt-42"], recordedAtUtc: "2026-03-01T11:00:00Z" }] };
    const frozen = packet(reviewed);
    api.listOnboardingWorkspaces.mockResolvedValue([initial]); api.assignOnboardingDifference.mockResolvedValue(assigned);
    api.reviewOnboardingWorkspace.mockResolvedValue(reviewed); api.freezeOnboardingPacket.mockResolvedValue(frozen);
    api.getOnboardingWorkspace.mockResolvedValue({ ...reviewed, version: 5, packets: [frozen] });
    const { container } = render(<AccountingOnboardingPanel />);
    await selectWorkspace();
    await userEvent.click(screen.getByText("New · Balance · account-1 · USD · owner owner"));
    const assignment = within(screen.getByRole("form", { name: "Assign Balance difference" }));
    await userEvent.clear(assignment.getByLabelText("Difference owner")); await userEvent.type(assignment.getByLabelText("Difference owner"), "analyst");
    await userEvent.type(assignment.getByLabelText("Ownership notes"), "Investigate cash.");
    await userEvent.type(assignment.getByLabelText("Supporting evidence IDs"), "receipt-42");
    await userEvent.click(assignment.getByRole("button", { name: "Save difference ownership" }));
    await screen.findByText("Difference owner and supporting evidence retained.");
    expect(api.assignOnboardingDifference).toHaveBeenCalledWith("workspace-1", difference.differenceKey, { expectedVersion: 2, ownerId: "analyst", notes: "Investigate cash.", evidenceIds: ["receipt-42"] }, expect.anything());
    await userEvent.click(screen.getByText("Readiness review"));
    const review = within(screen.getByRole("form", { name: "Review onboarding readiness" }));
    await userEvent.type(review.getByLabelText("Reviewer notes"), "Cash adjustment needs proof.");
    await userEvent.type(review.getByLabelText("Review evidence IDs"), "receipt-42");
    await userEvent.click(review.getByRole("button", { name: "Record reviewer decision" }));
    await screen.findByText("Reviewer decision retained for this data revision.");
    expect(api.reviewOnboardingWorkspace).toHaveBeenCalledWith("workspace-1", { expectedVersion: 3, dataRevision: 3, decision: "ChangesRequested", notes: "Cash adjustment needs proof.", evidenceIds: ["receipt-42"] }, expect.anything());
    await userEvent.click(screen.getByRole("button", { name: "Freeze readiness packet" }));
    expect(await screen.findByRole("button", { name: "Download frozen readiness packet" })).toBeInTheDocument();
    expect(screen.getByText("SHA-256: frozen-sha256")).toBeInTheDocument();
    expect(api.freezeOnboardingPacket).toHaveBeenCalledWith("workspace-1", 4, expect.anything());
    expect((await axe(container)).violations).toEqual([]);
  });

  it("discards a late comparison response when the operator changes workspace", async () => {
    const other = workspace({ workspaceId: "workspace-2", name: "Other entity", scope: { ...workspace().scope, entityId: "entity-2" } });
    api.listOnboardingWorkspaces.mockResolvedValue([workspace(), other]);
    let resolve!: (value: OnboardingWorkspace) => void;
    api.captureOnboardingComparison.mockReturnValue(new Promise(value => { resolve = value; }));
    render(<AccountingOnboardingPanel />); await selectWorkspace();
    await userEvent.click(screen.getByText("Capture next comparison"));
    await userEvent.selectOptions(screen.getByLabelText("Available retained source"), "0");
    await userEvent.click(screen.getByRole("button", { name: "Capture comparison" }));
    await waitFor(() => expect(api.captureOnboardingComparison).toHaveBeenCalled());
    await userEvent.selectOptions(screen.getByLabelText("Retained workspace"), "workspace-2");
    expect(api.captureOnboardingComparison.mock.calls[0][2]?.signal?.aborted).toBe(true);
    await act(async () => resolve(workspace({ version: 2, comparisons: [comparison()] })));
    expect(screen.getByRole("region", { name: "Onboarding Other entity" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Onboarding Quarter one onboarding" })).not.toBeInTheDocument();
  });

  it("shows unavailable reads without creating success, readiness, or a fallback workspace", async () => {
    api.listOnboardingWorkspaces.mockRejectedValue(new Error("Authenticated source unavailable"));
    render(<AccountingOnboardingPanel />);
    expect(await screen.findByText("Authenticated source unavailable")).toBeInTheDocument();
    expect(screen.queryByText("Readiness: Ready")).not.toBeInTheDocument();
    expect(api.createOnboardingWorkspace).not.toHaveBeenCalled();
  });

  it("versions owner-defined criteria without relabelling the original comparison", async () => {
    const initial = workspace({ version: 2, dataRevision: 2, comparisons: [comparison()] });
    api.listOnboardingWorkspaces.mockResolvedValue([initial]);
    api.updateOnboardingCriteria.mockImplementation(async (_id, _version, criteria) => ({ ...initial, version: 3, dataRevision: 3, criteria }));
    render(<AccountingOnboardingPanel />); await selectWorkspace();
    await userEvent.click(screen.getByText("Coverage and review criteria"));
    const form = within(screen.getByRole("form", { name: "Update onboarding criteria" }));
    await userEvent.clear(form.getByLabelText("Balance tolerance")); await userEvent.type(form.getByLabelText("Balance tolerance"), "0.01");
    await userEvent.clear(form.getByLabelText("Required dates")); await userEvent.type(form.getByLabelText("Required dates"), "2026-01-31, 2026-02-28, 2026-03-31");
    await userEvent.click(form.getByRole("button", { name: "Save criteria revision" }));
    await screen.findByText("Criteria revision retained.");
    expect(api.updateOnboardingCriteria).toHaveBeenCalledWith("workspace-1", 2, expect.objectContaining({ balanceTolerance: 0.01, requiredDates: ["2026-01-31", "2026-02-28", "2026-03-31"] }), expect.anything());
    expect(screen.getByRole("option", { name: "#1 · 2026-01-31 · 100% coverage · revision 2" })).toBeInTheDocument();
  });

  it("shows unresolved required-period differences even when the current period is resolved", async () => {
    const initial = workspace();
    api.listOnboardingWorkspaces.mockResolvedValue([workspace({ currentDifferences: [{ ...difference, status: "Resolved", difference: 0 }], readiness: {
      ...initial.readiness, unresolvedDifferenceCount: 1, unresolvedDifferences: [{ comparisonId: "comparison-jan", asOfDate: "2026-01-31", difference }]
    } })]);
    render(<AccountingOnboardingPanel />); await selectWorkspace();
    await userEvent.click(screen.getByText("Unresolved differences across required dates"));
    expect(screen.getByRole("heading", { name: "2026-01-31" })).toBeInTheDocument();
    expect(screen.getByText("New · Balance")).toBeInTheDocument();
    expect(screen.getByText("Resolved · Balance · account-1 · USD · owner owner")).toBeInTheDocument();
    expect(screen.getByText("Readiness: Blocked")).toBeInTheDocument();
  });
});
