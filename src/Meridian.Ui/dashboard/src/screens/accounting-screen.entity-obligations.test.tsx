import { fireEvent, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { getEntityOperation, postEntityOperation } from "@/lib/api/entity-operations.api";
import type { EntityOperationScope, EntityTimeline } from "@/types/entity-operations";
import type { IntercompanyObligationMonitor } from "@/types/intercompany-obligations";
import { EntityObligationMonitor } from "./accounting-screen.entity-obligations";
import { EntityLifecycleTimeline } from "./accounting-screen.entity-timeline";
import { IntercompanyCloseAdvisories } from "./accounting-screen.intercompany-close-advisories";

vi.mock("@/lib/api/entity-operations.api", async () => ({ ...await vi.importActual<typeof import("@/lib/api/entity-operations.api")>("@/lib/api/entity-operations.api"), getEntityOperation: vi.fn(), postEntityOperation: vi.fn() }));
const book = { ledgerBookId: "book-a", fundProfileId: "profile-a", fundStructureNodeId: "entity-a", fundStructureNodeKind: "Entity", displayName: "Operating primary book", baseCurrency: "USD", createdAt: "2026-01-01", updatedAt: "2026-01-01", accountingBasis: "Primary" as const, accountingPolicyId: "primary", accountingPolicyVersion: "v1" };
const scope: EntityOperationScope = { book, books: [book], capabilities: { canManageAgreements: true, canManageLedger: true, canManageLifecycle: true, canViewLedger: true }, graph: { organizations: [{ organizationId: "organization-a", name: "Example Holdings" }], entities: [{ entityId: "entity-a", name: "Operating Company", baseCurrency: "USD", lifecycleStatus: "Active" }], ownershipLinks: [] } };
const monitor: IntercompanyObligationMonitor = { asOf: "2026-11-13", expiryHorizonDays: 30, evaluatedAtUtc: "2026-11-13T12:00:00Z", status: "Partial", isComplete: false, sourceFingerprint: "observation-hash", sourceVersions: [{ sourceKind: "Agreement", sourceId: "agreement-a", version: "2", contentHash: "legal-hash" }], findings: [{ code: "LifecycleHistoryUnknown", severity: "Warning", message: "Historical lifecycle is unknown.", entityId: "entity-a" }], balanceSchedule: null, guarantees: [] };
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/obligations?") ? monitor : []); });

describe("Native intercompany obligation observations", () => {
  it("uses the selected authoritative book/profile and explicit date without altering close behavior", async () => {
    const readOnly = { ...scope, capabilities: { ...scope.capabilities, canManageLedger: false } };
    const { container } = renderWithRouter(<EntityObligationMonitor scope={readOnly} />);
    fireEvent.change(screen.getByLabelText("Obligation monitoring as of"), { target: { value: "2026-11-13" } });
    fireEvent.change(screen.getByLabelText("Agreement expiry horizon (calendar days)"), { target: { value: "45" } });
    fireEvent.click(screen.getByRole("button", { name: "Evaluate intercompany obligations" }));
    await screen.findByText("Partial · Incomplete observation");
    const [path] = vi.mocked(getEntityOperation).mock.calls.find(([value]) => value.includes("/obligations?"))!;
    expect(Object.fromEntries(new URL(path, "https://meridian.test").searchParams)).toEqual({ fundProfileId: "profile-a", ledgerBookId: "book-a", asOf: "2026-11-13", expiryHorizonDays: "45" });
    expect(screen.getByText(/Historical lifecycle is unknown/)).toBeVisible();
    expect(screen.getByText("The balance schedule is unavailable; coverage cannot be established from registry membership.")).toBeVisible();
    expect(postEntityOperation).not.toHaveBeenCalled();
    expect((await axe(container)).violations).toEqual([]);
  });
  it("clears stale results immediately when the date changes and preserves unavailable authority", async () => {
    renderWithRouter(<EntityObligationMonitor scope={{ ...scope, capabilities: { ...scope.capabilities, canManageLedger: false } }} />);
    fireEvent.change(screen.getByLabelText("Obligation monitoring as of"), { target: { value: "2026-11-13" } });
    fireEvent.click(screen.getByRole("button", { name: "Evaluate intercompany obligations" }));
    await screen.findByText("Partial · Incomplete observation");
    fireEvent.change(screen.getByLabelText("Obligation monitoring as of"), { target: { value: "2026-11-14" } });
    expect(screen.queryByText("Partial · Incomplete observation")).not.toBeInTheDocument();
    vi.mocked(getEntityOperation).mockRejectedValueOnce(new Error("Authoritative observation service unavailable"));
    fireEvent.click(screen.getByRole("button", { name: "Evaluate intercompany obligations" }));
    await screen.findByText(/Authoritative observation service unavailable/);
    expect(screen.queryByText("No advisory findings in the evaluated population.")).not.toBeInTheDocument();
  });
  it("renders close advisories outside blocker and readiness decisions", () => {
    renderWithRouter(<IntercompanyCloseAdvisories result={monitor} />);
    expect(screen.getByRole("region", { name: "Nonblocking intercompany advisories" })).toHaveTextContent("Advisory evidence does not change close readiness or posting permissions.");
    expect(screen.getByText(/Warning · Historical lifecycle is unknown/)).toBeVisible();
    expect(screen.queryByRole("list", { name: "Close command center blockers" })).not.toBeInTheDocument();
  });
});

it("prepares the exact native lifecycle subject server-side and invalidates it when legal inputs change", async () => {
  const lifecycle = { entityId: "entity-a", revision: 3, recordedBy: "operator-a", recordedAtUtc: "2026-11-01T12:00:00Z", reason: "Reviewed baseline", evidenceReference: "retained:baseline", contentHash: "lifecycle-3", legacySnapshot: false, timeline: [{ transitionId: "transition-a", effectiveFrom: "2026-01-01T00:00:00Z", effectiveTo: null, status: "Active", eventKind: "Formation", successorEntityId: null }] };
  const timeline: EntityTimeline = { canAttestExternalLifecycleReview: true, organizationId: "organization-a", entity: scope.graph!.entities[0], asOf: "2026-11-13", lifecycleRevisions: [lifecycle], ownershipLinks: [], agreements: [], accountingArrangements: [], books: [book], warnings: [] };
  vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/timeline?") ? timeline : lifecycle);
  vi.mocked(postEntityOperation).mockImplementation(async (path) => path.endsWith("/review-preview") ? { submittedHash: "exact-submitted-lifecycle" } : { ...lifecycle, revision: 4 });
  renderWithRouter(<EntityLifecycleTimeline scope={scope} />);
  fireEvent.click(await screen.findByText("Retain an entity lifecycle transition"));
  fireEvent.change(screen.getByLabelText("Next entity status"), { target: { value: "WindingDown" } });
  fireEvent.change(screen.getByLabelText("Lifecycle transition reason"), { target: { value: "Reviewed wind-down" } });
  fireEvent.change(screen.getByLabelText("Lifecycle transition evidence"), { target: { value: "retained:wind-down" } });
  fireEvent.click(screen.getByRole("button", { name: "Prepare lifecycle review subject" }));
  await screen.findByText("Exact reviewed subject: exact-submitted-lifecycle");
  expect(postEntityOperation).toHaveBeenCalledWith("/api/fund-structure/entities/entity-a/lifecycle/review-preview", expect.objectContaining({ entityId: "entity-a", expectedRevision: 3, reason: "Reviewed wind-down" }), expect.any(Object));
  fireEvent.change(screen.getByLabelText("Lifecycle transition reason"), { target: { value: "Changed legal decision" } });
  expect(screen.queryByRole("button", { name: "Attest external review and retain lifecycle successor" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Prepare lifecycle review subject" }));
  await screen.findByText("Exact reviewed subject: exact-submitted-lifecycle");
  fireEvent.change(screen.getByLabelText("External reviewer"), { target: { value: "Outside counsel" } });
  fireEvent.change(screen.getByLabelText("External reviewer reference"), { target: { value: "counsel-a" } });
  fireEvent.change(screen.getByLabelText("External review evidence reference"), { target: { value: "evidence://evidence-vault/review" } });
  fireEvent.change(screen.getByLabelText("External review artifact ID"), { target: { value: "review-document" } });
  fireEvent.change(screen.getByLabelText("External-review attestation rationale"), { target: { value: "Attest exact legal subject" } });
  fireEvent.click(screen.getByRole("button", { name: "Attest external review and retain lifecycle successor" }));
  await waitFor(() => expect(vi.mocked(postEntityOperation).mock.calls.some(([path]) => path.endsWith("/revisions"))).toBe(true));
  const command = vi.mocked(postEntityOperation).mock.calls.find(([path]) => path.endsWith("/revisions"))![1];
  expect(command).toMatchObject({ reason: "Changed legal decision", externalReview: { externalReviewer: "Outside counsel", submittedHash: "exact-submitted-lifecycle" } });
});

it("records explicit operator review and revokes coverage through an immutable native successor", async () => {
  const source = { ledgerBookId: "book-a", journalEntryId: "journal-a", entryId: "line-a", sourceLineDigest: "a".repeat(64), accountName: "Intercompany Receivable", accountType: "Asset", debit: "120", credit: "0", effectiveDate: "2026-11-01" };
  const prior = { attributionId: "attribution-a", revision: 2, ledgerBookId: "book-a", journalEntryId: "journal-a", lineId: "line-a", sourceLineDigest: source.sourceLineDigest, accountingArrangementId: "profile-a", accountingArrangementRevisionId: "mapping-a", accountBindingRevisionId: "binding-a", chartAccountNodeId: "receivable-a", intercompanyTransactionId: "transaction-a", preparedBy: "preparer", approvedBy: "reviewer", approvedAt: "2026-11-01T12:00:00Z", evidenceReference: "retained:original", tenantId: "tenant-a", companyId: "company-a", previousAttributionId: null, intercompanyAgreementId: "legal-a", intercompanyAgreementVersion: 4, intercompanyAgreementPayloadHash: "b".repeat(64) };
  vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/candidates?") ? [source] : path.includes("legacy-attributions?") ? [prior] : []);
  vi.mocked(postEntityOperation).mockResolvedValue({ ...prior, attributionId: "revocation-a", revision: 3, revoked: true });
  renderWithRouter(<EntityObligationMonitor scope={scope} />);
  fireEvent.click(screen.getByText("Review legacy posted-line agreement coverage"));
  await waitFor(() => expect(screen.getByLabelText("Legacy posted source line").querySelectorAll("option")).toHaveLength(2));
  fireEvent.change(screen.getByLabelText("Legacy posted source line"), { target: { value: "journal-a:line-a" } });
  await waitFor(() => expect(getEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany-accounting-arrangements?bookId=book-a&asOf=2026-11-01", expect.any(Object)));
  expect(getEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/account-bindings?ledgerBookId=book-a&asOf=2026-11-01", expect.any(Object));
  expect(vi.mocked(getEntityOperation).mock.calls.some(([path]) => path === "/api/ledger/intercompany-accounting-arrangements")).toBe(false);
  fireEvent.click(screen.getByLabelText("Operator-reviewed coverage: the authenticated preparer may also review"));
  fireEvent.change(screen.getByLabelText("Coverage review evidence reference"), { target: { value: "retained:revocation" } });
  fireEvent.change(screen.getByLabelText("Coverage review rationale"), { target: { value: "Revoke incorrect mapping after review" } });
  fireEvent.click(screen.getByRole("button", { name: "Revoke selected coverage" }));
  await waitFor(() => expect(postEntityOperation).toHaveBeenCalledOnce());
  expect(vi.mocked(postEntityOperation).mock.calls[0][0]).toBe("/api/ledger/intercompany/legacy-attributions");
  expect(vi.mocked(postEntityOperation).mock.calls[0][1]).toMatchObject({ expectedPreviousAttributionId: "attribution-a", attribution: { revision: 3, previousAttributionId: "attribution-a", revoked: true, reviewMode: "OperatorReviewed", preparedBy: "", approvedBy: "", sourceLineDigest: source.sourceLineDigest, intercompanyAgreementPayloadHash: prior.intercompanyAgreementPayloadHash, reviewRationale: "Revoke incorrect mapping after review" } });
});

it("keeps separately unverified arrangement populations visible across a monitoring refresh", async () => {
  const makeRow = (accountingArrangementId: string, amount: string) => ({ ledgerBookId: "book-a", entityId: "entity-a", counterpartyEntityId: "entity-b", accountingArrangementId, accountingArrangementRevisionId: null, chartAccountNodeId: null, role: null, transactionCurrency: "USD", functionalCurrency: "USD", transactionSignedBalance: amount, functionalSignedBalance: amount, mappingVerified: false, journalEntryIds: [], bindingRevisionIds: [] });
  const rows = [makeRow("unverified-arrangement-a", "120.000000000000000001"), makeRow("unverified-arrangement-b", "80.000000000000000002")];
  const snapshot: IntercompanyObligationMonitor = { ...monitor, balanceSchedule: { fundProfileId: "profile-a", ledgerBookId: "book-a", asOf: monitor.asOf, available: true, rows, issues: [], contentHash: "schedule", legacyAttributionIds: [], isMappingComplete: false, isBalanced: false, isReady: false } };
  vi.mocked(getEntityOperation).mockResolvedValueOnce(snapshot).mockResolvedValueOnce({ ...snapshot, balanceSchedule: { ...snapshot.balanceSchedule!, rows: [...rows].reverse() } });
  const errors = vi.spyOn(console, "error").mockImplementation(() => undefined);
  try {
    renderWithRouter(<EntityObligationMonitor scope={{ ...scope, capabilities: { ...scope.capabilities, canManageLedger: false } }} />);
    fireEvent.click(screen.getByRole("button", { name: "Evaluate intercompany obligations" }));
    await screen.findByText("Partial · Incomplete observation");
    expect(screen.getAllByText("120.000000000000000001 USD")).toHaveLength(2);
    expect(screen.getAllByText("80.000000000000000002 USD")).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "Evaluate intercompany obligations" }));
    await waitFor(() => expect(getEntityOperation).toHaveBeenCalledTimes(2));
    expect(screen.getAllByText("120.000000000000000001 USD")).toHaveLength(2);
    expect(screen.getAllByText("80.000000000000000002 USD")).toHaveLength(2);
    expect(errors).not.toHaveBeenCalled();
  } finally { errors.mockRestore(); }
});
