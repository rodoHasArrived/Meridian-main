import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as onboarding from "@/lib/api/accounting-onboarding.api";
import { OnboardingShadowPanel, parseAccountMappings } from "./accounting-screen.onboarding-shadow-panel";
import type { OnboardingDifference, OnboardingParallelRunCertificate, OnboardingWorkspace } from "@/types/accounting-onboarding";

vi.mock("@/lib/api/accounting-onboarding.api", () => ({
  designateShadowBook: vi.fn(), withdrawShadowBook: vi.fn(), importOnboardingOpeningBalances: vi.fn(),
  explainOnboardingDifference: vi.fn(), issueParallelRunCertificate: vi.fn(), promoteShadowBook: vi.fn(),
  getOnboardingWorkspace: vi.fn()
}));
const api = vi.mocked(onboarding);

const difference: OnboardingDifference = {
  differenceKey: "cash-key", kind: "Balance", accountId: "account-1", currency: "USD", instrumentId: null,
  meridianAmount: 100, externalAmount: 97, difference: 3, tolerance: 0, status: "New", ownerId: "owner",
  firstSeenComparisonId: "comparison-feb", lastSeenComparisonId: "comparison-feb", evidenceIds: [], missingReason: null
};

function workspace(overrides: Partial<OnboardingWorkspace> = {}): OnboardingWorkspace {
  return {
    workspaceId: "workspace-1", name: "Shadow onboarding", ownerId: "owner", version: 4, dataRevision: 3,
    createdAtUtc: "2026-01-01T00:00:00Z", updatedAtUtc: "2026-01-01T00:00:00Z",
    scope: { tenantId: "tenant-1", companyId: "company-1", entityId: "entity-1", fundProfileId: "profile-1", ledgerBookId: "book-1", accountIds: ["account-1"], startDate: "2026-01-31", endDate: "2026-03-31" },
    criteria: { requiredDates: ["2026-01-31", "2026-02-28"], requiredKinds: ["Balance"], balanceTolerance: 0, positionTolerance: 0, navTolerance: 0, minimumCoveragePercent: 100, requiredReviewerIds: ["reviewer"], minimumReviewers: 1, requireCloseReadiness: false, reviewInstructions: "Reconcile." },
    criteriaHistory: [], comparisons: [], currentDifferences: [], assignments: [], reviews: [], packets: [],
    readiness: { status: "Exceptions", isReady: false, unresolvedDifferenceCount: 0, missingDates: [], missingSources: [], blockers: [], approvedReviewers: 0, requiredReviewers: 1, unresolvedDifferences: [] },
    authorityPosture: "ExternalBooksReadOnly", ...overrides
  };
}

const shadow = { state: "Shadow" as const, ledgerBookId: "book-1", designatedBy: "owner", designatedAtUtc: "2026-02-01T00:00:00Z", rationale: "Parallel run." };

function certificate(): OnboardingParallelRunCertificate {
  return {
    certificateId: "certificate-1", issuedAtUtc: "2026-04-01T00:00:00Z", issuedBy: "owner", contentHash: "c".repeat(64), hashAlgorithm: "SHA-256",
    content: { workspaceId: "workspace-1", name: "Shadow onboarding", scope: workspace().scope, dataRevision: 3,
      opening: { comparisonId: "opening-1", sourceKind: "AdministratorPack", asOfDate: "2026-01-31", sourceContentHash: "a".repeat(64), matchedCount: 1, explainedCount: 0, breakCount: 0, missingCount: 0, isReconciled: true, lines: [], evidenceIds: [] },
      periods: [], cleanPeriods: ["2026-01-31", "2026-02-28"], explanations: [], approvingReviews: [], packetId: "packet-1", packetContentHash: "p".repeat(64),
      statement: "Meridian ran in parallel with the incumbent books for every required period." }
  };
}

beforeEach(() => vi.resetAllMocks());

describe("shadow-mode onboarding", () => {
  it("parses incumbent account mappings and refuses malformed entries", () => {
    expect(parseAccountMappings("IBAN-1=account-1\nADMIN-2 = account-2")).toEqual([
      { externalAccount: "IBAN-1", accountId: "account-1" }, { externalAccount: "ADMIN-2", accountId: "account-2" }]);
    expect(parseAccountMappings("")).toEqual([]);
    expect(() => parseAccountMappings("missing-separator")).toThrow(/EXTERNAL=account-id/);
  });

  it("designates the ledger book as a shadow book with the current version", async () => {
    api.designateShadowBook.mockResolvedValue(workspace({ shadowDesignation: shadow }));
    const onChanged = vi.fn();
    const { container } = render(<OnboardingShadowPanel workspace={workspace()} onChanged={onChanged} />);
    expect(screen.getByText("Not in shadow mode")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
    const form = within(screen.getByRole("form", { name: "Designate shadow book" }));
    await userEvent.type(form.getByLabelText("Designation rationale"), "Trial against the administrator.");
    await userEvent.click(form.getByRole("button", { name: "Designate shadow book" }));
    await waitFor(() => expect(api.designateShadowBook).toHaveBeenCalledWith("workspace-1", 4, "Trial against the administrator.", expect.anything()));
    expect(onChanged).toHaveBeenCalledWith(expect.objectContaining({ shadowDesignation: shadow }));
  });

  it("uploads the administrator pack as base64 with explicit mappings and shows the opening report", async () => {
    const report = { comparisonId: "opening-1", sourceKind: "AdministratorPack", asOfDate: "2026-01-31", sourceContentHash: "f".repeat(64),
      matchedCount: 1, explainedCount: 0, breakCount: 1, missingCount: 0, isReconciled: false, evidenceIds: [],
      lines: [{ differenceKey: "k", kind: "Balance", accountId: "account-1", currency: "USD", instrumentId: null, meridianAmount: 100, incumbentAmount: 95, difference: 5, tolerance: 0, status: "Break" as const, classification: null, missingReason: null }] };
    const current = workspace({ shadowDesignation: shadow });
    api.importOnboardingOpeningBalances.mockResolvedValue({ ...current, readiness: { ...current.readiness, opening: report } });
    const { rerender } = render(<OnboardingShadowPanel workspace={current} onChanged={vi.fn()} />);
    const form = within(screen.getByRole("form", { name: "Import incumbent opening balances" }));
    await userEvent.upload(form.getByLabelText("Opening file"), new File(["record_type\nCASH"], "opening.csv", { type: "text/csv" }));
    await userEvent.type(form.getByLabelText("Mapping profile ID"), "admin-map");
    await userEvent.type(form.getByLabelText("Mapping version"), "v1");
    await userEvent.type(form.getByLabelText("Account mappings"), "ADMIN-001=account-1");
    await userEvent.click(form.getByRole("button", { name: "Import opening balances" }));
    await waitFor(() => expect(api.importOnboardingOpeningBalances).toHaveBeenCalled());
    const [id, request] = api.importOnboardingOpeningBalances.mock.calls[0];
    expect(id).toBe("workspace-1");
    expect(request).toMatchObject({ expectedVersion: 4, sourceKind: "AdministratorPack", fileName: "opening.csv",
      mappingProfileId: "admin-map", mappingVersion: "v1", accountMappings: [{ externalAccount: "ADMIN-001", accountId: "account-1" }] });
    expect(atob(request.contentBase64!)).toBe("record_type\nCASH");
    expect(request).not.toHaveProperty("actor");

    rerender(<OnboardingShadowPanel workspace={{ ...current, readiness: { ...current.readiness, opening: report } }} onChanged={vi.fn()} />);
    expect(screen.getByText("Opening balances not reconciled")).toBeInTheDocument();
    expect(within(screen.getByRole("table", { name: "Opening balance lines" })).getByText("Break")).toBeInTheDocument();
  });

  it("explains a timing difference with evidence and reversal date, then shows per-period agreement", async () => {
    const current = workspace({ shadowDesignation: shadow, readiness: { ...workspace().readiness, unresolvedDifferenceCount: 1,
      unresolvedDifferences: [{ comparisonId: "comparison-feb", asOfDate: "2026-02-28", difference }],
      periods: [{ asOfDate: "2026-02-28", comparisonId: "comparison-feb", navCurrency: "USD", meridianNav: 100, externalNav: 100, navVariance: 0,
        observationCount: 2, matchedCount: 1, explainedCount: 0, unexplainedCount: 1, missingSourceCount: 0, explainedByClassification: [], isClean: false }] } });
    api.explainOnboardingDifference.mockResolvedValue(current);
    render(<OnboardingShadowPanel workspace={current} onChanged={vi.fn()} />);
    expect(within(screen.getByRole("table", { name: "Parallel-run period agreement" })).getByText("Open")).toBeInTheDocument();
    await userEvent.click(screen.getByText("Explain parallel-run differences"));
    const form = within(screen.getByRole("form", { name: "Explain Balance difference on 2026-02-28" }));
    await userEvent.type(form.getByLabelText("Expected reversal date"), "2026-03-31");
    await userEvent.type(form.getByLabelText("Explanation"), "Settlement-date booking at the administrator.");
    await userEvent.type(form.getByLabelText("Evidence IDs"), "evidence:wire");
    await userEvent.click(form.getByRole("button", { name: "Retain explanation" }));
    await waitFor(() => expect(api.explainOnboardingDifference).toHaveBeenCalledWith("workspace-1", "cash-key", {
      expectedVersion: 4, comparisonId: "comparison-feb", classification: "Timing", notes: "Settlement-date booking at the administrator.",
      evidenceIds: ["evidence:wire"], expectedReversalDate: "2026-03-31" }, expect.anything()));
  });

  it("promotes only through the latest certificate and shows promoted authority", async () => {
    const current = workspace({ shadowDesignation: shadow, certificates: [certificate()] });
    const promoted = workspace({ shadowDesignation: { ...shadow, state: "Promoted", resolvedBy: "reviewer", resolvedAtUtc: "2026-04-02T00:00:00Z" },
      certificates: [certificate()], promotion: { decisionId: "decision-1", certificateId: "certificate-1", certificateContentHash: "c".repeat(64), decidedBy: "reviewer", decidedAtUtc: "2026-04-02T00:00:00Z", rationale: "Clean.", evidenceIds: [], dataRevision: 3 } });
    api.promoteShadowBook.mockResolvedValue(promoted);
    const onChanged = vi.fn();
    const { rerender } = render(<OnboardingShadowPanel workspace={current} onChanged={onChanged} />);
    expect(screen.getByText("Shadow book — not the book of record")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Issue parallel-run certificate" })).toBeDisabled();
    const form = within(screen.getByRole("form", { name: "Promote shadow book" }));
    await userEvent.type(form.getByLabelText("Promotion rationale"), "Zero unexplained breaks.");
    await userEvent.click(form.getByRole("button", { name: "Promote to book of record" }));
    await waitFor(() => expect(api.promoteShadowBook).toHaveBeenCalledWith("workspace-1",
      { expectedVersion: 4, certificateId: "certificate-1", rationale: "Zero unexplained breaks.", evidenceIds: [] }, expect.anything()));
    rerender(<OnboardingShadowPanel workspace={promoted} onChanged={onChanged} />);
    expect(screen.getByText("Promoted to book of record")).toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Promote shadow book" })).not.toBeInTheDocument();
  });

  it("explains an opening break through its own opening comparison and never offers a form for a missing line", async () => {
    const opening = { comparisonId: "opening-pack", sourceKind: "StatementOpening+AdministratorPack", asOfDate: "2026-01-31", sourceContentHash: "f".repeat(64),
      matchedCount: 1, explainedCount: 0, breakCount: 1, missingCount: 1, isReconciled: false, evidenceIds: [],
      uncoveredKinds: ["Position"],
      sources: [
        { sourceKind: "StatementOpening", comparisonId: "opening-bank", sourceContentHash: "b".repeat(64), kinds: ["Balance"], capturedAtUtc: "2026-02-01T00:00:00Z" },
        { sourceKind: "AdministratorPack", comparisonId: "opening-pack", sourceContentHash: "a".repeat(64), kinds: ["Nav"], capturedAtUtc: "2026-02-01T00:00:00Z" }
      ],
      lines: [
        { differenceKey: "cash", kind: "Balance", accountId: "account-1", currency: "USD", instrumentId: null, meridianAmount: 100, incumbentAmount: 97, difference: 3, tolerance: 0, status: "Break" as const, classification: null, missingReason: null, comparisonId: "opening-bank", sourceKind: "StatementOpening" },
        { differenceKey: "nav", kind: "Nav", accountId: "account-1", currency: "USD", instrumentId: null, meridianAmount: 100, incumbentAmount: null, difference: null, tolerance: 0, status: "Missing" as const, classification: null, missingReason: "No amount.", comparisonId: "opening-pack", sourceKind: "AdministratorPack" }
      ] };
    const current = workspace({ shadowDesignation: shadow, readiness: { ...workspace().readiness, opening } });
    api.explainOnboardingDifference.mockResolvedValue(current);
    const { container } = render(<OnboardingShadowPanel workspace={current} onChanged={vi.fn()} />);

    expect(screen.getByText("Opening kinds not yet covered")).toBeInTheDocument();
    expect(within(screen.getByRole("list", { name: "Opening sources" })).getAllByRole("listitem")).toHaveLength(2);
    expect(within(screen.getByRole("table", { name: "Opening balance lines" })).getAllByText("Bank statement").length).toBeGreaterThan(0);
    expect((await axe(container)).violations).toEqual([]);

    await userEvent.click(screen.getByText("Explain parallel-run differences"));
    expect(screen.queryByRole("form", { name: /Explain opening Nav difference/ })).not.toBeInTheDocument();
    const form = within(screen.getByRole("form", { name: "Explain opening Balance difference (Bank statement)" }));
    await userEvent.selectOptions(form.getByLabelText("Classification"), "Treatment");
    await userEvent.type(form.getByLabelText("Explanation"), "Bank nets an unsettled fee.");
    await userEvent.type(form.getByLabelText("Evidence IDs"), "evidence:fee");
    await userEvent.click(form.getByRole("button", { name: "Retain explanation" }));
    await waitFor(() => expect(api.explainOnboardingDifference).toHaveBeenCalledWith("workspace-1", "cash", {
      expectedVersion: 4, comparisonId: "opening-bank", classification: "Treatment", notes: "Bank nets an unsettled fee.",
      evidenceIds: ["evidence:fee"], expectedReversalDate: null }, expect.anything()));
  });
});
