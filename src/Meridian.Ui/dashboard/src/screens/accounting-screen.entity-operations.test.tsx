import { fireEvent, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { getEntityOperation, postEntityOperation } from "@/lib/api/entity-operations.api";
import type { LedgerBook } from "@/types";
import type { EntityOperationScope, IntercompanyAccountingArrangement, IntercompanyProposal } from "@/types/entity-operations";
import type { AgreementDetail, AgreementRevision } from "@/types/intercompany-agreements";
import { EntityOperationsPanel } from "./accounting-screen.entity-operations";
import { EntityBookControls } from "./accounting-screen.entity-book-controls";
import { EntityAccountingArrangements } from "./accounting-screen.entity-agreements";
import { AccountingArrangementEditor } from "./accounting-screen.entity-agreement-editor";
import { EntityCounterpartProposals } from "./accounting-screen.entity-proposals";
import { EntityFxAuthority } from "./accounting-screen.entity-fx";
import { FxRateSetEditor } from "./accounting-screen.entity-fx-rates";
import { CounterpartRepricing } from "./accounting-screen.entity-counterpart-repricing";
import type { ManualJournalEntryDraft } from "@/types";
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
const legalRevision: AgreementRevision = { agreementId: "legal-a", agreementVersion: 4, action: "Amend", terms: { code: "LEGAL-01", name: "Shared services agreement", partyOneId: "entity-a", partyOneRole: "Service provider", partyTwoId: "entity-b", partyTwoRole: "Recipient", agreementType: "CostShare", currency: "USD", effectiveFrom: "2026-01-01", effectiveTo: null, action: "Amend", terms: { scheduleDescription: "Monthly", settlementDays: 30 }, evidence: [{ reference: "retained://signed-native-terms", artifactId: "artifact-native", contentHash: "signed-hash" }] }, proposalId: "native-proposal", payloadHash: "approved-native-payload", authors: ["legal-author"], submitter: "legal-author", approver: "independent-legal-reviewer", approvedAtUtc: "2026-01-01", rationale: "Legal terms approved independently" };
const legalDetail: AgreementDetail = { agreementId: legalRevision.agreementId, aggregateVersion: 12, code: legalRevision.terms.code, name: legalRevision.terms.name, partyOneId: "entity-a", partyOneRole: "Service provider", partyTwoId: "entity-b", partyTwoRole: "Recipient", agreementType: "CostShare", currency: "USD", asOf: "2026-11-12", effectiveStatus: "Active", latestApprovedVersion: 4, applicableRevision: legalRevision, proposals: [], revisions: [legalRevision], history: [], capabilities: { canEdit: false, canSubmit: false, canApprove: false, canReject: false, canWithdraw: false, canAmend: false, canTerminate: false } };
const arrangementRecord = { arrangementId: "arrangement-a", organizationId: "org-a", arrangementReference: "Shared services profile", templateKind: "CostRecharge" as const, canonicalAgreementId: "legal-a", canonicalAgreementVersion: 4, canonicalPayloadHash: legalRevision.payloadHash, effectiveFrom: "2026-01-01T00:00:00Z", effectiveTo: null, revisionId: "mapping-a", revisionNumber: 1, version: 3, status: "Submitted" as const, partyMappings: [book, targetBook].map((item) => ({ entityId: item.fundStructureNodeId, ledgerBookId: item.ledgerBookId, receivableAccountNodeId: "ar", payableAccountNodeId: "ap", settlementAccountNodeId: null, revenueAccountNodeId: null, expenseAccountNodeId: null })), createdBy: "owner", createdAt: "2026-01-01", updatedBy: "owner", updatedAt: "2026-01-01", approvedBy: null, approvedAt: null, retiredAt: null, predecessorRevisionId: null };
const arrangement: IntercompanyAccountingArrangement = { ...arrangementRecord, arrangement: arrangementRecord, legalRevision, partyOneEntityId: "entity-a", partyTwoEntityId: "entity-b", partyOneRole: "Service provider", partyTwoRole: "Recipient", currency: "USD", settlementCadence: "Monthly", settlementDueDays: 30, canonicalEvidence: legalRevision.terms.evidence! };
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
    if (path.includes("/history")) return { events: [{ requestId: "event-a", action: "Submit", actor: "owner", recordedAt: "2026-01-01", reason: "Reviewed mappings", snapshot: arrangement.arrangement }] };
    if (path.includes("intercompany-accounting-arrangements")) return [arrangement];
    if (path.includes("intercompany-agreements/")) return legalDetail;
    if (path.includes("intercompany-agreements?")) return { items: [legalDetail], nextCursor: null };
    return [];
  });
  vi.mocked(postEntityOperation).mockResolvedValue(arrangement);
});

describe("Entity operations", () => {
  it("uses server permission capabilities and keeps accounting profile mutation unavailable to ledger-only readers", async () => {
    vi.mocked(getEntityOperation).mockResolvedValue({ canManageAgreements: false, canManageLedger: false, canManageLifecycle: false, canViewLedger: true });
    renderWithRouter(<EntityOperationsPanel book={book} />);
    await screen.findByText(/Accounting profile management requires fund-structure permission/);
    expect(screen.queryByRole("button", { name: "New accounting profile" })).not.toBeInTheDocument();
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

  it("keeps controlled-close provenance and navigation bound to the authorized server receipt after editing the next request", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue({ authorizationId: "authorization-a", journalEntryId: "authorized-journal-a", ledgerBookId: "authorized-book", fundProfileId: "authorized-profile", draftVersion: 7, postingFingerprint: "fingerprint-a", authorizedBy: "reviewer-a" });
    renderWithRouter(<EntityBookControls scope={scope} />);
    fireEvent.click(screen.getByText("Authorize a controlled wind-down journal"));
    fireEvent.change(screen.getByLabelText("Controlled-close draft journal ID"), { target: { value: "requested-journal-a" } });
    fireEvent.change(screen.getByLabelText("Controlled-close draft version"), { target: { value: "7" } });
    fireEvent.change(screen.getByLabelText("Source obligation evidence"), { target: { value: "retained:obligation-a" } });
    fireEvent.change(screen.getByLabelText("Controlled-close reason"), { target: { value: "Settle retained obligation" } });
    fireEvent.click(screen.getByRole("button", { name: "Retain controlled-close authorization" }));
    const link = await screen.findByRole("link", { name: "Review authorized journal" });
    const expectedPath = "/accounting/journal-entries?fundProfileId=authorized-profile&ledgerBookId=authorized-book&journalEntryId=authorized-journal-a";
    expect(link).toHaveAttribute("href", expectedPath);
    fireEvent.change(screen.getByLabelText("Controlled-close draft journal ID"), { target: { value: "next-journal-b" } });
    fireEvent.change(screen.getByLabelText("Controlled-close draft version"), { target: { value: "8" } });
    expect(link).toHaveAttribute("href", expectedPath);
    expect(screen.getByText("Authorized journal authorized-journal-a · version 7")).toBeVisible();
    expect(screen.getByText("Draft fingerprint fingerprint-a")).toBeVisible();
    expect(postEntityOperation).toHaveBeenCalledTimes(1);
  });

  it("withholds authorized-journal navigation when receipt identity is incomplete", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue({ authorizationId: "incomplete-a", postingFingerprint: "fingerprint-a", authorizedBy: "reviewer-a" });
    renderWithRouter(<EntityBookControls scope={scope} />);
    fireEvent.click(screen.getByText("Authorize a controlled wind-down journal"));
    fireEvent.change(screen.getByLabelText("Controlled-close draft journal ID"), { target: { value: "requested-journal-a" } });
    fireEvent.change(screen.getByLabelText("Source obligation evidence"), { target: { value: "retained:obligation-a" } });
    fireEvent.change(screen.getByLabelText("Controlled-close reason"), { target: { value: "Settle retained obligation" } });
    fireEvent.click(screen.getByRole("button", { name: "Retain controlled-close authorization" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Authorized journal identity is unavailable");
    expect(screen.queryByRole("link", { name: "Review authorized journal" })).not.toBeInTheDocument();
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

  it("approves an owner's accounting mappings without manufacturing legal approval", async () => {
    renderWithRouter(<EntityAccountingArrangements scope={scope} />);
    fireEvent.click(await screen.findByRole("button", { name: arrangement.arrangementReference }));
    fireEvent.change(screen.getByLabelText("Accounting profile action reason"), { target: { value: "Book and account mappings verified" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve accounting mappings" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany-accounting-arrangements/arrangement-a/approve", expect.objectContaining({ expectedVersion: 3, revisionId: "mapping-a", reason: "Book and account mappings verified", requestId: expect.any(String) }), expect.any(Object)));
    expect(vi.mocked(postEntityOperation).mock.calls.every(([path]) => !path.includes("/fund-structure/") && !path.includes("/post"))).toBe(true);
    expect(screen.getByRole("link", { name: "Inspect approved legal terms and signed evidence" })).toHaveAttribute("href", expect.stringContaining("agreementId=legal-a"));
  });

  it("retains mapping revisions against exact approved legal facts without editable legal copies", async () => {
    renderWithRouter(<AccountingArrangementEditor scope={scope} arrangement={arrangement} onSaved={vi.fn()} onCancel={vi.fn()} />);
    await screen.findByRole("heading", { name: "Approved legal version 4 · LEGAL-01" });
    expect(screen.getByLabelText("Canonical legal agreement")).toBeDisabled();
    expect(screen.queryByLabelText("Agreement currency")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Settlement due after calendar days")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("First party role")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Accounting profile reference"), { target: { value: "Reconciled mapping profile" } });
    fireEvent.change(screen.getByLabelText("Accounting profile revision reason"), { target: { value: "Approved account mapping refresh" } });
    fireEvent.click(screen.getByRole("button", { name: "Save accounting profile draft" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith(expect.stringContaining("/revisions"), expect.objectContaining({ expectedVersion: 3, arrangementReference: "Reconciled mapping profile", canonicalAgreementVersion: 4, canonicalPayloadHash: legalRevision.payloadHash, partyMappings: arrangement.partyMappings, reason: "Approved account mapping refresh" }), expect.any(Object)));
    const body = vi.mocked(postEntityOperation).mock.calls[0][1] as Record<string, unknown>;
    expect(body).not.toHaveProperty("currency"); expect(body).not.toHaveProperty("signedDocument"); expect(body).not.toHaveProperty("partyOneRole");
  });

  it("reloads resolved legal facts after a mapping mutation returns only the raw profile", async () => {
    let approved = false;
    const approvedRecord = { ...arrangement.arrangement, status: "Approved" as const, version: 4, approvedBy: "owner" };
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.endsWith("/history") ? { events: [] }
      : [{ ...arrangement, ...(approved ? approvedRecord : arrangement.arrangement), arrangement: approved ? approvedRecord : arrangement.arrangement }]);
    vi.mocked(postEntityOperation).mockImplementation(async () => { approved = true; return approvedRecord; });
    renderWithRouter(<EntityAccountingArrangements scope={scope} />);
    fireEvent.click(await screen.findByRole("button", { name: arrangement.arrangementReference }));
    fireEvent.change(screen.getByLabelText("Accounting profile action reason"), { target: { value: "Exact mappings reviewed" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve accounting mappings" }));
    await screen.findByText("Approved", { exact: true });
    expect(screen.getByText("Canonical LEGAL-01 · legal version 4 · USD · Monthly")).toBeVisible();
    expect(screen.getByRole("button", { name: "Retire accounting mappings" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Approve accounting mappings" })).not.toBeInTheDocument();
  });

  it("retains an informational guarantee profile with derived parties and no accounting mappings", async () => {
    const informational: IntercompanyAccountingArrangement = { ...arrangement, templateKind: "Guarantee", partyMappings: [], arrangement: { ...arrangement.arrangement, templateKind: "Guarantee", partyMappings: [] } };
    renderWithRouter(<AccountingArrangementEditor scope={scope} arrangement={informational} onSaved={vi.fn()} onCancel={vi.fn()} />);
    await screen.findByRole("heading", { name: "Approved legal version 4 · LEGAL-01" });
    expect(screen.queryByLabelText("Party 1 book")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Party 1 entity")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Accounting profile revision reason"), { target: { value: "Informational mapping review" } });
    fireEvent.click(screen.getByRole("button", { name: "Save accounting profile draft" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith(expect.stringContaining("/revisions"), expect.objectContaining({ templateKind: "Guarantee", partyMappings: [], canonicalAgreementVersion: 4 }), expect.any(Object)));
  });

  it("targets an older approved mapping revision for retirement while a successor is pending", async () => {
    const pendingRecord = { ...arrangement.arrangement, revisionId: "mapping-pending", revisionNumber: 2, version: 7, status: "Draft" as const };
    const pending = { ...arrangement, ...pendingRecord, arrangement: pendingRecord };
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.endsWith("/history") ? { events: [
      { requestId: "approved-old", action: "Approve", actor: "owner", recordedAt: "2026-01-02", snapshot: { ...arrangement.arrangement, status: "Approved" } },
      { requestId: "pending-new", action: "Revise", actor: "owner", recordedAt: "2026-11-01", snapshot: pendingRecord }
    ] } : [pending]);
    renderWithRouter(<EntityAccountingArrangements scope={scope} />);
    fireEvent.click(await screen.findByRole("button", { name: pending.arrangementReference }));
    await screen.findByLabelText("Approved mapping revision to retire");
    fireEvent.change(screen.getByLabelText("Accounting profile action reason"), { target: { value: "Accounting mapping superseded" } });
    fireEvent.click(screen.getByRole("button", { name: "Retire accounting mappings" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith(expect.stringContaining("/retire"), expect.objectContaining({ revisionId: "mapping-a", expectedVersion: 7 }), expect.any(Object)));
  });

  it("requires an explicit approved canonical version and pins its hash when creating a profile", async () => {
    renderWithRouter(<AccountingArrangementEditor scope={scope} arrangement={null} onSaved={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Save accounting profile draft" })).toBeDisabled();
    await screen.findByRole("option", { name: /LEGAL-01 · Shared services agreement/ });
    fireEvent.change(screen.getByLabelText("Accounting profile reference"), { target: { value: "New approved-version profile" } });
    fireEvent.change(screen.getByLabelText("Canonical legal agreement"), { target: { value: "legal-a" } });
    await screen.findByRole("option", { name: /Version 4.*independent-legal-reviewer/ });
    fireEvent.change(screen.getByLabelText("Approved legal version"), { target: { value: "4" } });
    fireEvent.change(screen.getByLabelText("Counterpart accounting template"), { target: { value: "Guarantee" } });
    expect(screen.getByLabelText("Include accounting book mappings for this guarantee")).not.toBeChecked();
    expect(screen.queryByLabelText("Party 1 book")).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: "Save accounting profile draft" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Save accounting profile draft" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany-accounting-arrangements", expect.objectContaining({ canonicalAgreementId: "legal-a", canonicalAgreementVersion: 4, canonicalPayloadHash: "approved-native-payload", partyMappings: [] }), expect.any(Object)));
    expect(vi.mocked(postEntityOperation).mock.calls.every(([path]) => !path.includes("/fund-structure/"))).toBe(true);
  });

  it("starts a new accounting profile in the operator's selected organization", async () => {
    const otherOrganization = { organizationId: "org-b", name: "Organization B" };
    renderWithRouter(<EntityAccountingArrangements scope={{ ...scope, graph: { ...scope.graph!, organizations: [...scope.graph!.organizations, otherOrganization] } }} />);
    fireEvent.change(screen.getByLabelText("Accounting profile perimeter"), { target: { value: "org-b" } });
    fireEvent.click(screen.getByRole("button", { name: "New accounting profile" }));
    expect(screen.getByLabelText("Accounting profile organization")).toHaveValue("org-b");
    expect(postEntityOperation).not.toHaveBeenCalled();
  });

  it("keeps pending decisions and unsaved mapping edits in their selected scope until completion or explicit cancel", async () => {
    let rejectDecision: (error: Error) => void = () => {};
    vi.mocked(postEntityOperation).mockImplementation(() => new Promise((_resolve, reject) => { rejectDecision = reject; }));
    renderWithRouter(<EntityAccountingArrangements scope={scope} />);
    const row = await screen.findByRole("button", { name: arrangement.arrangementReference });
    fireEvent.click(row);
    fireEvent.change(screen.getByLabelText("Accounting profile action reason"), { target: { value: "Mappings reviewed" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve accounting mappings" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledTimes(1));
    expect(row).toBeDisabled();
    expect(screen.getByRole("button", { name: "Refresh accounting profiles" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "New accounting profile" })).toBeDisabled();
    expect(screen.getByLabelText("Accounting profile perimeter")).toBeDisabled();
    rejectDecision(new Error("Mapping decision unavailable"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Revise accounting mappings" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Revise accounting mappings" }));
    await screen.findByRole("heading", { name: "Approved legal version 4 · LEGAL-01" });
    fireEvent.change(screen.getByLabelText("Accounting profile reference"), { target: { value: "Unsaved reviewed mapping" } });
    expect(screen.getByRole("button", { name: "Refresh accounting profiles" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "New accounting profile" })).toBeDisabled();
    expect(screen.getByLabelText("Accounting profile perimeter")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Approve accounting mappings" })).toBeDisabled();
    expect(screen.getByLabelText("Accounting profile reference")).toHaveValue("Unsaved reviewed mapping");
    fireEvent.click(screen.getByRole("button", { name: "Cancel profile editing" }));
    expect(screen.getByRole("button", { name: "Refresh accounting profiles" })).toBeEnabled();
    expect(postEntityOperation).toHaveBeenCalledTimes(1);
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

  it.each(["rates", "policy"])("retains the %s editor and its authority scope until save completes or the operator cancels", async (editor) => {
    const rateSet = { rateSetId: "rates-a", revision: 1, rates: [], sourceEvidence: "retained:rates", approvedBy: "reviewer", approvedAt: "2026-11-13", contentHash: "rates-hash" };
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.endsWith("/rate-sets") ? [rateSet] : null);
    let complete: (value: unknown) => void = () => {};
    vi.mocked(postEntityOperation).mockImplementation(() => new Promise((resolve) => { complete = resolve; }));
    renderWithRouter(<EntityFxAuthority scope={scope} />);
    await waitFor(() => expect(screen.getByRole("button", { name: "Configure book translation" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: editor === "rates" ? "New approved rate set" : "Configure book translation" }));
    expect(screen.getByLabelText("FX configuration as of")).toBeDisabled();
    for (const name of ["Refresh FX authority", "New approved rate set", "Configure book translation", "Revise rate set 1"]) {
      expect(screen.getByRole("button", { name })).toBeDisabled();
      expect(screen.getByRole("button", { name })).toHaveAttribute("title", expect.stringContaining("Save or cancel"));
    }
    const cancelName = editor === "rates" ? "Cancel rate editing" : "Cancel policy editing";
    fireEvent.click(screen.getByRole("button", { name: cancelName }));
    expect(screen.getByLabelText("FX configuration as of")).toBeEnabled();
    fireEvent.click(screen.getByRole("button", { name: editor === "rates" ? "New approved rate set" : "Configure book translation" }));
    if (editor === "rates") {
      fireEvent.change(screen.getByLabelText("Rate-set source evidence"), { target: { value: "retained:new-rates" } });
      fireEvent.change(screen.getByLabelText("Quote 1 source currency"), { target: { value: "EUR" } });
      fireEvent.change(screen.getByLabelText("Quote 1 exact rate"), { target: { value: "1.2345" } });
      fireEvent.change(screen.getByLabelText("Quote 1 evidence"), { target: { value: "retained:quote" } });
    } else {
      fireEvent.change(screen.getByLabelText("Approved FX rate-set revision"), { target: { value: "rates-a:1" } });
    }
    fireEvent.click(screen.getByRole("button", { name: editor === "rates" ? "Approve and retain rate set" : "Approve and retain book FX policy" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("button", { name: cancelName })).toBeDisabled();
    expect(screen.getByRole("button", { name: "New approved rate set" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Configure book translation" })).toBeDisabled();
    complete(rateSet);
    await waitFor(() => expect(screen.queryByRole("button", { name: cancelName })).not.toBeInTheDocument());
    expect(screen.getByLabelText("FX configuration as of")).toBeEnabled();
    expect(screen.getByRole("button", { name: "New approved rate set" })).toBeEnabled();
  });

  it("shows delivery failure and links a specifically identified existing counterpart", async () => {
    const proposal: IntercompanyProposal = { source: { proposalId: "proposal-a", sourceJournalId: "source-journal", sourceBookId: "book-a", sourceEntityId: "entity-a", counterpartyEntityId: "entity-b", accountingArrangementId: "arrangement-a", accountingArrangementRevisionId: "mapping-a", intercompanyAgreementId: "legal-a", intercompanyAgreementVersion: 4, intercompanyAgreementPayloadHash: "approved-native-payload", transactionId: "recharge-123", effectiveDate: "2026-11-01", sourceFunctionalCurrency: "USD" }, status: "NeedsFix", attempts: 2, recordedAt: "2026-11-01", targetBookId: null, targetJournalId: null, lastError: "Destination period is locked.", dueDate: null, sourceDigest: "source-digest" };
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
    let proposal: IntercompanyProposal = { source: { proposalId: "proposal-a", sourceJournalId: "source-journal", sourceBookId: "book-a", sourceEntityId: "entity-a", counterpartyEntityId: "entity-b", accountingArrangementId: "arrangement-a", accountingArrangementRevisionId: "mapping-a", intercompanyAgreementId: "legal-a", intercompanyAgreementVersion: 4, intercompanyAgreementPayloadHash: "approved-native-payload", transactionId: "rejected-recharge", effectiveDate: "2026-11-01", sourceFunctionalCurrency: "USD" }, status: "Drafted", attempts: 1, recordedAt: "2026-11-01", targetBookId: "book-b", targetJournalId: "original-rejected-generated-draft", lastError: null, dueDate: null, sourceDigest: "source-digest" };
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
