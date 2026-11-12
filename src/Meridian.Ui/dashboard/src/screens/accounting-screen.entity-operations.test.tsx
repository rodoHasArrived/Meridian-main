import { fireEvent, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { getEntityOperation, postEntityOperation } from "@/lib/api/entity-operations.api";
import type { LedgerBook, ManualJournalEntryLine } from "@/types";
import type { EntityOperationScope, IntercompanyAgreement, IntercompanyProposal } from "@/types/entity-operations";
import type { ManualJournalEntryWorkbenchViewModel } from "./accounting-screen.view-model";
import { EntityOperationsPanel } from "./accounting-screen.entity-operations";
import { EntityBookControls } from "./accounting-screen.entity-book-controls";
import { EntityAgreements } from "./accounting-screen.entity-agreements";
import { AgreementEditor } from "./accounting-screen.entity-agreement-editor";
import { EntityCounterpartProposals } from "./accounting-screen.entity-proposals";
import { FxRateSetEditor } from "./accounting-screen.entity-fx-rates";
import { ManualJournalIntercompanyDimensions } from "./accounting-screen.journal-intercompany-dimensions";
import { CounterpartRepricing } from "./accounting-screen.entity-counterpart-repricing";
import { SignedAgreementEvidencePicker } from "./accounting-screen.entity-signed-evidence";
import type { EvidenceVaultDocumentEntry, ManualJournalEntryDraft } from "@/types";
import { EntityPerimeterTransition } from "./accounting-screen.entity-perimeter-transition";
import type { ConsolidationTransitionPreview } from "@/types/entity-operations";

vi.mock("@/lib/api/entity-operations.api", async () => ({
  ...await vi.importActual<typeof import("@/lib/api/entity-operations.api")>("@/lib/api/entity-operations.api"),
  getEntityOperation: vi.fn(), postEntityOperation: vi.fn()
}));
const book: LedgerBook = { ledgerBookId: "book-a", fundProfileId: "group", fundStructureNodeId: "entity-a", fundStructureNodeKind: "Entity",
  displayName: "Entity A primary", baseCurrency: "USD", createdAt: "2026-01-01", updatedAt: "2026-01-01", accountingBasis: "Primary", accountingPolicyId: "operating", accountingPolicyVersion: "v1" };
const targetBook: LedgerBook = { ...book, ledgerBookId: "book-b", fundStructureNodeId: "entity-b", displayName: "Entity B primary" };
const scope: EntityOperationScope = { book, books: [book, targetBook], capabilities: { canManageAgreements: true, canManageLedger: true, canManageLifecycle: true, canViewLedger: true },
  graph: { organizations: [{ organizationId: "org-a", name: "Organization A" }], entities: [{ entityId: "entity-a", name: "Entity A", baseCurrency: "USD", lifecycleStatus: "Active" }, { entityId: "entity-b", name: "Entity B", baseCurrency: "USD", lifecycleStatus: "Active" }], ownershipLinks: [] } };
const agreement: IntercompanyAgreement = { agreementId: "agreement-a", organizationId: "org-a", facilityReference: "Shared services facility", kind: "CostRecharge", partyOneEntityId: "entity-a", partyTwoEntityId: "entity-b",
  partyOneRole: "Service provider", partyTwoRole: "Recipient", currency: "USD", settlementCadence: "Monthly", settlementDueDays: 30, effectiveFrom: "2026-01-01T00:00:00Z", effectiveTo: null,
  revisionId: "terms-a", revisionNumber: 1, version: 3, status: "Submitted", partyMappings: [book, targetBook].map((item) => ({ entityId: item.fundStructureNodeId, ledgerBookId: item.ledgerBookId,
    receivableAccountNodeId: "ar", payableAccountNodeId: "ap", settlementAccountNodeId: null, revenueAccountNodeId: null, expenseAccountNodeId: null })),
  signedDocument: { vaultId: "vault-a", documentId: "document-a", artifactId: "artifact-a", contentHashSha256: "a".repeat(64), scope: "tenant-a" },
  createdBy: "owner", createdAt: "2026-01-01", updatedBy: "owner", updatedAt: "2026-01-01", approvedBy: null, approvedAt: null, terminatedAt: null, predecessorRevisionId: null, successorAgreementId: null };
const profile = { ledgerBookId: book.ledgerBookId, mode: "LegacyWarning", version: 8, policyVersion: "entity-controls-v1" };
const preflight = { ledgerBookId: book.ledgerBookId, profileVersion: 8, policyVersion: "entity-controls-v1", digest: "reviewed-preflight", preparedAtUtc: "2026-11-12T00:00:00Z", canActivate: true,
  decisions: [{ control: "Agreement mapping", code: "mapped", allowed: true, message: "All intercompany lines map to approved agreements.", evidenceRevision: "terms-a" }] };

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getEntityOperation).mockImplementation(async (path) => {
    if (path.includes("capabilities")) return scope.capabilities;
    if (path.includes("/graph")) return scope.graph;
    if (path === "/api/ledger/books") return scope.books;
    if (path.includes("/controls")) return profile;
    if (path.includes("/history")) return { events: [{ requestId: "event-a", action: "Submit", actor: "owner", recordedAt: "2026-01-01", reason: "Reviewed terms", snapshot: agreement }] };
    if (path.includes("intercompany-agreements")) return [agreement];
    return [];
  });
  vi.mocked(postEntityOperation).mockResolvedValue(agreement);
});

describe("Entity operations", () => {
  it("uses server permission capabilities and keeps agreement mutation unavailable to ledger-only readers", async () => {
    vi.mocked(getEntityOperation).mockResolvedValue({ canManageAgreements: false, canManageLedger: false, canManageLifecycle: false, canViewLedger: true });
    renderWithRouter(<EntityOperationsPanel book={book} />);
    await screen.findByText(/Agreement management requires fund-structure permission/);
    expect(screen.queryByRole("button", { name: "New agreement" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "Book controls" }));
    expect(await screen.findByText(/Ledger management permission is required to inspect/)).toBeVisible();
    expect(postEntityOperation).not.toHaveBeenCalled();
  });

  it("activates only the exact reviewed preflight and records operator evidence", async () => {
    vi.mocked(postEntityOperation).mockImplementation(async (path) => path.endsWith("/preflight") ? preflight : { ...profile, mode: "Enforced" });
    const { container } = renderWithRouter(<EntityBookControls scope={scope} />);
    await screen.findByText("Legacy warning mode");
    fireEvent.click(screen.getByRole("button", { name: "Run control preflight" }));
    await screen.findByText("All intercompany lines map to approved agreements.");
    fireEvent.change(screen.getByLabelText("Control activation reason"), { target: { value: "Historical mapping reconciled" } });
    fireEvent.change(screen.getByLabelText("Migration evidence reference"), { target: { value: "vault:migration-report" } });
    fireEvent.click(screen.getByRole("button", { name: "Activate enforced controls" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/books/book-a/controls/activate", {
      expectedVersion: 8, preflightDigest: "reviewed-preflight", reason: "Historical mapping reconciled", evidenceReference: "vault:migration-report"
    }, expect.objectContaining({ signal: expect.any(AbortSignal) })));
    expect((await axe(container)).violations).toEqual([]);
  });

  it("keeps blocked migration decisions visible and prevents activation", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue({ ...preflight, canActivate: false, decisions: [{ ...preflight.decisions[0], allowed: false, code: "unmapped", message: "A historical counterparty needs mapping." }] });
    renderWithRouter(<EntityBookControls scope={scope} />);
    await screen.findByText("Legacy warning mode");
    fireEvent.click(screen.getByRole("button", { name: "Run control preflight" }));
    await screen.findByText("A historical counterparty needs mapping.");
    expect(screen.getByRole("button", { name: "Activate enforced controls" })).toBeDisabled();
    expect(postEntityOperation).toHaveBeenCalledTimes(1);
  });

  it("approves an owner's submitted agreement against its exact version and revision", async () => {
    renderWithRouter(<EntityAgreements scope={scope} />);
    fireEvent.click(await screen.findByRole("button", { name: agreement.facilityReference }));
    fireEvent.change(screen.getByLabelText("Agreement action reason"), { target: { value: "Signed terms verified" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve agreement" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/fund-structure/intercompany-agreements/agreement-a/Approve", expect.objectContaining({ expectedVersion: 3, revisionId: "terms-a", reason: "Signed terms verified", requestId: expect.any(String) }), expect.any(Object)));
    expect(vi.mocked(postEntityOperation).mock.calls.every(([path]) => !path.includes("/post"))).toBe(true);
  });

  it("retains a typed successor revision with signed source identity and governed due days", async () => {
    const saved = vi.fn();
    renderWithRouter(<AgreementEditor scope={scope} agreement={agreement} onSaved={saved} onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Settlement due after calendar days"), { target: { value: "45" } });
    fireEvent.change(screen.getByLabelText("Terms revision reason"), { target: { value: "Reviewed settlement amendment" } });
    fireEvent.click(screen.getByRole("button", { name: "Retain successor revision" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith(expect.stringContaining("/revisions"), expect.objectContaining({
      expectedVersion: 3, settlementDueDays: 45, signedDocument: agreement.signedDocument, partyMappings: agreement.partyMappings, reason: "Reviewed settlement amendment"
    }), expect.any(Object)));
  });

  it("edits an informational guarantee without forcing accounting mappings or losing its parties", async () => {
    const informational = { ...agreement, kind: "Guarantee" as const, partyMappings: [] };
    renderWithRouter(<AgreementEditor scope={scope} agreement={informational} onSaved={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByLabelText("Party 1 entity")).toHaveValue("entity-a");
    expect(screen.getByLabelText("Party 2 entity")).toHaveValue("entity-b");
    expect(screen.queryByLabelText("Party 1 book")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Terms revision reason"), { target: { value: "Guarantee document reference updated" } });
    fireEvent.click(screen.getByRole("button", { name: "Retain successor revision" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith(expect.stringContaining("/revisions"), expect.objectContaining({ partyMappings: [] }), expect.any(Object)));
  });

  it("targets an older approved revision for termination while a successor is pending", async () => {
    const pending = { ...agreement, revisionId: "terms-pending", revisionNumber: 2, version: 7, status: "Draft" as const };
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.endsWith("/history") ? { events: [
      { requestId: "approved-old", action: "Approve", actor: "owner", recordedAt: "2026-01-02", snapshot: { ...agreement, status: "Approved" } },
      { requestId: "pending-new", action: "Revise", actor: "owner", recordedAt: "2026-11-01", snapshot: pending }
    ] } : [pending]);
    renderWithRouter(<EntityAgreements scope={scope} />);
    fireEvent.click(await screen.findByRole("button", { name: pending.facilityReference }));
    await screen.findByLabelText("Approved terms revision for terminal action");
    fireEvent.change(screen.getByLabelText("Agreement action reason"), { target: { value: "Facility settlement completed" } });
    fireEvent.click(screen.getByRole("button", { name: "Terminate agreement" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith(expect.stringContaining("/Terminate"), expect.objectContaining({ revisionId: "terms-a", expectedVersion: 7 }), expect.any(Object)));
  });

  it("preserves the full entered decimal quote as a string without browser conversion", async () => {
    renderWithRouter(<FxRateSetEditor scope={scope} previous={null} onSaved={vi.fn()} onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Rate-set source evidence"), { target: { value: "vault:approved-rates" } });
    fireEvent.change(screen.getByLabelText("Quote 1 source currency"), { target: { value: "EUR" } });
    fireEvent.change(screen.getByLabelText("Quote 1 exact rate"), { target: { value: "1.1234567890123456789012345678" } });
    fireEvent.change(screen.getByLabelText("Quote 1 evidence"), { target: { value: "retained-provider-observation" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve and retain rate set" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/consolidation/fx/rate-sets/approve", expect.objectContaining({ rates: [expect.objectContaining({ rate: "1.1234567890123456789012345678", fromCurrency: "EUR", toCurrency: "USD", kind: "Closing" })] }), expect.any(Object)));
  });

  it("shows delivery failure and links a specifically identified existing counterpart", async () => {
    const proposal: IntercompanyProposal = { source: { proposalId: "proposal-a", sourceJournalId: "source-journal", sourceBookId: "book-a", sourceEntityId: "entity-a", counterpartyEntityId: "entity-b", agreementId: "agreement-a", agreementRevisionId: "terms-a", transactionId: "recharge-123", effectiveDate: "2026-11-01", sourceFunctionalCurrency: "USD" }, status: "NeedsFix", attempts: 2, recordedAt: "2026-11-01", targetBookId: null, targetJournalId: null, lastError: "Destination period is locked.", dueDate: null, sourceDigest: "source-digest" };
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/proposals") ? [proposal] : []);
    vi.mocked(postEntityOperation).mockResolvedValue({ ...proposal, status: "Linked", targetBookId: "book-b", targetJournalId: "existing-journal" });
    renderWithRouter(<EntityCounterpartProposals scope={scope} />);
    await screen.findByText("Destination period is locked.");
    fireEvent.click(screen.getByRole("button", { name: "recharge-123" }));
    fireEvent.change(screen.getByLabelText("Existing counterpart book"), { target: { value: "book-b" } });
    fireEvent.change(screen.getByLabelText("Existing counterpart journal ID"), { target: { value: "existing-journal" } });
    fireEvent.change(screen.getByLabelText("Counterpart linking reason"), { target: { value: "Book B already recorded the source invoice" } });
    fireEvent.click(screen.getByRole("button", { name: "Validate and link existing counterpart" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/proposals/proposal-a/link", expect.objectContaining({ targetBookId: "book-b", targetJournalId: "existing-journal", requestId: expect.any(String) }), expect.any(Object)));
  });

  it.each([true, false])("uses the appended manual resolution without rewriting original delivery or offering more actions (resolved getters %s)", async (includeResolvedGetters) => {
    let proposal: IntercompanyProposal = { source: { proposalId: "proposal-a", sourceJournalId: "source-journal", sourceBookId: "book-a", sourceEntityId: "entity-a", counterpartyEntityId: "entity-b", agreementId: "agreement-a", agreementRevisionId: "terms-a", transactionId: "rejected-recharge", effectiveDate: "2026-11-01", sourceFunctionalCurrency: "USD" }, status: "Drafted", attempts: 1, recordedAt: "2026-11-01", targetBookId: "book-b", targetJournalId: "original-rejected-generated-draft", lastError: null, dueDate: null, sourceDigest: "source-digest" };
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/proposals?") ? [proposal] : []);
    vi.mocked(postEntityOperation).mockImplementation(async () => {
      proposal = { ...proposal, latestLink: { requestId: "link-request", proposalId: "proposal-a", targetBookId: "book-b", targetJournalId: "manually-posted-counterpart", actor: "target-reviewer", reason: "Rejected generated draft replaced by existing reciprocal posted journal", recordedAt: "2026-11-12", rejectedDraftId: "original-rejected-generated-draft", rejectedDraftVersion: 6 },
        ...(includeResolvedGetters ? { resolvedTargetBookId: "book-b", resolvedTargetJournalId: "manually-posted-counterpart" } : {}) };
      return proposal;
    });
    renderWithRouter(<EntityCounterpartProposals scope={scope} />);
    fireEvent.click(await screen.findByRole("button", { name: "rejected-recharge" }));
    expect(screen.getByRole("button", { name: "Preview counterpart FX repricing" })).toBeVisible();
    fireEvent.change(screen.getByLabelText("Existing counterpart book"), { target: { value: "book-b" } });
    fireEvent.change(screen.getByLabelText("Existing counterpart journal ID"), { target: { value: "manually-posted-counterpart" } });
    fireEvent.change(screen.getByLabelText("Counterpart linking reason"), { target: { value: "Reviewed generated-draft rejection and exact posted counterpart" } });
    fireEvent.click(screen.getByRole("button", { name: "Validate and link existing counterpart" }));
    const resolved = await screen.findByRole("link", { name: "Open resolved counterpart journal" });
    expect(resolved).toHaveAttribute("href", "/accounting/journal-entries?ledgerBookId=book-b&journalEntryId=manually-posted-counterpart");
    expect(screen.getByText("Resolved", { exact: true })).toBeVisible();
    expect(screen.getByText("Original delivery receipt: Drafted")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Preview counterpart FX repricing" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry counterpart delivery" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Validate and link existing counterpart" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("Provenance for rejected-recharge"));
    expect(screen.getByText("Original delivered journal")).toBeVisible();
    expect(screen.getByText("original-rejected-generated-draft", { exact: true })).toBeVisible();
    expect(screen.getByText("target-reviewer · 2026-11-12")).toBeVisible();
  });

  it("sets journal line authority from approved agreement and account-binding lookups", async () => {
    const approved = { ...agreement, status: "Approved" };
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("account-bindings") ? [{ revisionId: "binding-revision", chartAccountNodeId: "ar", role: "Receivable", accountPath: "Assets:Group Receivable", accountName: "Group Receivable", revisionNumber: 2, isArchived: false }]
      : path.includes("/agreements?") ? [approved] : null);
    const line: ManualJournalEntryLine = { lineId: "line-a", side: "Debit", amount: 100, currency: "USD", accountPath: "Assets:Group Receivable" };
    const updateLine = vi.fn();
    const view = { draft: { journalEntryId: "journal-a", ledgerBookId: "book-a", accountingDate: "2026-11-12", currency: "USD", status: "Draft" }, updateLine } as unknown as ManualJournalEntryWorkbenchViewModel;
    const { container } = renderWithRouter(<ManualJournalIntercompanyDimensions view={view} line={line} />);
    const details = container.querySelector("details")!;
    details.open = true; fireEvent(details, new Event("toggle"));
    await screen.findByRole("option", { name: /Shared services facility.*revision 1/ });
    fireEvent.change(screen.getByLabelText("Line approved intercompany agreement"), { target: { value: "agreement-a" } });
    expect(updateLine).toHaveBeenCalledWith("line-a", expect.objectContaining({ dimensions: expect.objectContaining({ agreementId: "agreement-a", agreementRevisionId: "terms-a", counterpartyId: "entity-b", intercompanyTransactionId: "journal:journal-a" }) }));
    fireEvent.change(screen.getByLabelText("Line intercompany account binding"), { target: { value: "binding-revision" } });
    expect(updateLine).toHaveBeenCalledWith("line-a", expect.objectContaining({ dimensions: expect.objectContaining({ chartAccountNodeId: "ar", accountBindingRevisionId: "binding-revision" }) }));
  });

  it("previews counterpart repricing without mutation, then explicitly applies the exact before-version and fingerprint", async () => {
    const before = { journalEntryId: "target-journal", ledgerBookId: "book-b", version: 4, status: "Draft", accountingDate: "2026-11-12",
      lines: [{ lineId: "target-line", side: "Debit", amount: 100, currency: "USD", accountPath: "Expenses:Shared service" }] } as ManualJournalEntryDraft;
    const after = { ...before, lines: [{ ...before.lines[0], amount: 120, transactionCurrency: { currency: "EUR", amount: "100", fxRateToFunctional: "1.2" } }] };
    const proposal = { source: { proposalId: "proposal-a" }, sourceDigest: "retained-original-source", status: "Drafted" } as IntercompanyProposal;
    vi.mocked(getEntityOperation).mockResolvedValue({ currentDraft: before, draft: after, fingerprint: "reviewed-repricing" });
    vi.mocked(postEntityOperation).mockResolvedValue({ ...after, version: 5 });
    const applied = vi.fn();
    renderWithRouter(<CounterpartRepricing proposal={proposal} canManage onApplied={applied} />);
    fireEvent.click(screen.getByRole("button", { name: "Preview counterpart FX repricing" }));
    await screen.findByText("$100.00");
    expect(screen.getByText("$120.00")).toBeVisible();
    expect(postEntityOperation).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("Counterpart FX repricing reason"), { target: { value: "Approved closing source corrected" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply repricing and renew review" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/proposals/proposal-a/refresh", expect.objectContaining({ expectedDraftVersion: 4, previewFingerprint: "reviewed-repricing", reason: "Approved closing source corrected", requestId: expect.any(String) }), expect.any(Object)));
    await waitFor(() => expect(applied).toHaveBeenCalledTimes(1));
  });

  it("offers only accepted signed artifacts linked to the exact agreement", async () => {
    const document = { vaultId: "vault-a", subjectKind: "accounting-record", subjectId: "current", document: { documentId: "doc-a", artifactId: "artifact-a", fileName: "Signed facility.pdf", scope: "company-a", receivedAt: "2026-11-01", sourceHashSha256: "a".repeat(64), reviewerState: { status: "Accepted" }, objectLinks: [{ linkKind: "IntercompanyAgreement", objectId: "agreement-a" }] } } as EvidenceVaultDocumentEntry;
    vi.mocked(getEntityOperation).mockResolvedValue([document,
      { ...document, document: { ...document.document, documentId: "foreign-doc", fileName: "Foreign agreement.pdf", objectLinks: [{ linkKind: "IntercompanyAgreement", objectId: "another-agreement" }] } },
      { ...document, document: { ...document.document, documentId: "unreviewed-doc", fileName: "Unreviewed agreement.pdf", reviewerState: { status: "Unreviewed" } } }
    ]);
    const select = vi.fn();
    renderWithRouter(<SignedAgreementEvidencePicker agreementId="agreement-a" onSelect={select} />);
    fireEvent.click(screen.getByRole("button", { name: "Find retained signed agreements" }));
    await screen.findByRole("option", { name: /Signed facility.pdf/ });
    expect(screen.queryByRole("option", { name: /Foreign agreement.pdf/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /Unreviewed agreement.pdf/ })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Retained signed agreement document"), { target: { value: "vault-a:doc-a:artifact-a" } });
    expect(select).toHaveBeenCalledWith({ vaultId: "vault-a", documentId: "doc-a", artifactId: "artifact-a", contentHashSha256: "a".repeat(64), scope: "company-a" });
  });

  it("loads another operator's retained cutover preview and approves only its exact fingerprint", async () => {
    const preview = cutoverPreview();
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/previews/") ? preview : []);
    vi.mocked(postEntityOperation).mockResolvedValue({ transitionId: preview.request.transitionId, previewId: preview.previewId, request: preview.request, approvedBy: "independent-reviewer", approvedAt: "2026-11-12", rootVersion: 1, contentHash: "approved-transition" });
    renderWithRouter(<EntityPerimeterTransition scope={scope} />);
    fireEvent.change(screen.getByLabelText("Retained perimeter preview ID"), { target: { value: preview.previewId } });
    fireEvent.click(screen.getByRole("button", { name: "Load retained perimeter preview" }));
    await screen.findByText(/Prepared by preparer-a/);
    expect(screen.getByRole("heading", { name: "Joined entities" })).toBeVisible();
    expect(screen.getByText("Entity B")).toBeVisible();
    expect(postEntityOperation).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Independently approve perimeter cutover" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/consolidation/transitions/activate", { previewId: preview.previewId, previewFingerprint: "exact-cutover-preview" }, expect.any(Object)));
  });

  it("does not approve a retained perimeter preview with source or routing blockers", async () => {
    const preview = { ...cutoverPreview(), canApprove: false, blockers: ["The prior book has a later posted elimination."] };
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/previews/") ? preview : []);
    renderWithRouter(<EntityPerimeterTransition scope={scope} />);
    fireEvent.change(screen.getByLabelText("Retained perimeter preview ID"), { target: { value: preview.previewId } });
    fireEvent.click(screen.getByRole("button", { name: "Load retained perimeter preview" }));
    await screen.findByText("The prior book has a later posted elimination.");
    expect(screen.getByRole("button", { name: "Independently approve perimeter cutover" })).toBeDisabled();
    expect(postEntityOperation).not.toHaveBeenCalled();
  });
});

function cutoverPreview(): ConsolidationTransitionPreview {
  return { previewId: "70000000-0000-0000-0000-000000000001", request: { transitionId: "70000000-0000-0000-0000-000000000002", organizationId: "70000000-0000-0000-0000-000000000003", ownershipRootId: "70000000-0000-0000-0000-000000000004", previousEliminationBookId: "70000000-0000-0000-0000-000000000005", newEliminationBookId: "70000000-0000-0000-0000-000000000006", newPeriodId: "70000000-0000-0000-0000-000000000007", cutoverDate: "2026-11-12", reason: "Owned entity joins group", evidenceReference: "vault:ownership-cutover" }, rootVersion: 0, previousTransitionId: null, preparedBy: "preparer-a", preparedAt: "2026-11-12", previousBookEvidenceDigest: "old-reviewed-evidence", previousPostedJournalIds: [], previousLatestPostedAsOf: "2026-11-11", previousEntityIds: ["entity-a"], joinedEntityIds: ["entity-b"], removedEntityIds: [], newEvidence: { ruleVersion: "w10-v4", entityIds: ["entity-a", "entity-b"], sourceFingerprint: "new-source-population", perimeterFingerprint: "new-owned-perimeter" }, blockers: [], fingerprint: "exact-cutover-preview", canApprove: true };
}
