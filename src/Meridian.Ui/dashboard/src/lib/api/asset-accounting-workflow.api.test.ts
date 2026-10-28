import { createAssetApprovalFixture } from "@/test/asset-approval-fixtures";
import { webcrypto } from "node:crypto";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson } from "@/lib/api";
import { getAssetAccountingDraftCapabilities } from "./asset-accounting-workflow.api";
import type { AssetAccountingDraftCapabilities, AssetWorkflowScope } from "@/types/asset-accounting-workflow";
vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));
const scope: AssetWorkflowScope = { journalEntryId: "55555555-5555-5555-5555-555555555555", fundProfileId: "fund-1", ledgerBookId: "11111111-1111-1111-1111-111111111111", periodId: "22222222-2222-2222-2222-222222222222", draftVersion: 3 };
function capability(patch: Partial<AssetAccountingDraftCapabilities> = {}): AssetAccountingDraftCapabilities {
  return { ...scope, protocolVersion: 1, authenticatedActor: "independent-checker", candidateFingerprint: "a".repeat(64), canApprove: true, canPost: false,
    approveBlockedReason: null, postBlockedReason: "Approval required", approval: null, hasPendingPost: false, pendingPostCommand: null, postedJournal: null, ...patch };
}
async function withApproval() { return capability({ approval: createAssetApprovalFixture(scope).approval }); }

beforeEach(() => { vi.resetAllMocks(); vi.stubGlobal("crypto", webcrypto); });
afterEach(() => vi.unstubAllGlobals());
describe("Authenticated asset workflow API", () => {
  it("loads exact scoped versioned capabilities without development fallback", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(capability());
    expect((await getAssetAccountingDraftCapabilities(scope, { allowDevelopmentFallback: true })).canApprove).toBe(true);
    expect(apiGetJson).toHaveBeenCalledWith(`/api/ledger/books/${scope.ledgerBookId}/factor-paydowns/drafts/${scope.journalEntryId}/capabilities?fundProfileId=fund-1`, { allowDevelopmentFallback: false });
  });
  it.each([{ protocolVersion: 0 }, { authenticatedActor: undefined }, { authenticatedActor: null }, { authenticatedActor: "" }, { draftVersion: 2 }, { journalEntryId: "foreign" }, { fundProfileId: "foreign" }, { ledgerBookId: "foreign" }, { periodId: "foreign" }, { candidateFingerprint: "" }])("refuses unsupported or stale capability %j", async patch => {
    vi.mocked(apiGetJson).mockResolvedValue(capability(patch));
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("does not match");
  });
  it("verifies retained approval bytes against their declared digest and candidate", async () => {
    const value = await withApproval(); vi.mocked(apiGetJson).mockResolvedValue(value);
    expect((await getAssetAccountingDraftCapabilities(scope)).approval?.approver).toBe("independent-checker");
    vi.mocked(apiGetJson).mockResolvedValue({ ...value, approval: { ...value.approval, canonicalContentBase64: btoa("changed receipt") } });
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("do not match their digest");
    vi.mocked(apiGetJson).mockResolvedValue({ ...value, approval: { ...value.approval, candidateFingerprint: "b".repeat(64) } });
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("another candidate");
  });
  it("refuses validly hashed receipt bytes for a different declared decision scope", async () => {
    const value = await withApproval();
    const body = { ...createAssetApprovalFixture(scope).decision, fundProfileId: "foreign-fund" };
    const bytes = Buffer.from(JSON.stringify(body));
    const digest = Buffer.from(await webcrypto.subtle.digest("SHA-256", bytes)).toString("hex");
    vi.mocked(apiGetJson).mockResolvedValue({ ...value, approval: { ...value.approval, canonicalContentBase64: bytes.toString("base64"), contentHashSha256: digest } });
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("declared identity, candidate or scope");
  });

  it("refuses posting recovery owned by a different actor than the actual approval", async () => {
    const value = await withApproval();
    vi.mocked(apiGetJson).mockResolvedValue({ ...value, hasPendingPost: true, canPost: true, pendingPostCommand: {
      journalEntryId: scope.journalEntryId, fundProfileId: scope.fundProfileId, ledgerBookId: scope.ledgerBookId,
      action: "Post", actor: "foreign-checker", version: 2, correlationId: "original-command", rebookLines: []
    } });
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("retained approver");
  });
  it("accepts server-authorized principal casing without changing the retained recovery request", async () => {
    const value = await withApproval();
    const pendingPostCommand = { journalEntryId: scope.journalEntryId, fundProfileId: scope.fundProfileId, ledgerBookId: scope.ledgerBookId,
      action: "Post" as const, actor: "independent-checker", version: 2, correlationId: "original-command", rebookLines: [] };
    vi.mocked(apiGetJson).mockResolvedValue({ ...value, authenticatedActor: "INDEPENDENT-CHECKER", hasPendingPost: true, canPost: true, pendingPostCommand });
    expect((await getAssetAccountingDraftCapabilities(scope)).pendingPostCommand).toEqual(pendingPostCommand);
  });

  it("requires an exact retained posting request for recovery and checks canonical receipt scope", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(capability({ canPost: true, hasPendingPost: true }));
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("original command");
    vi.mocked(apiGetJson).mockResolvedValue(capability({ hasPendingPost: true, pendingPostCommand: { journalEntryId: "foreign", fundProfileId: scope.fundProfileId, ledgerBookId: scope.ledgerBookId, action: "Post", actor: "checker", version: 2, correlationId: "retained-command" } }));
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("does not match this journal");
    vi.mocked(apiGetJson).mockResolvedValue(capability({ postedJournal: { journalEntryId: "posted-1", ledgerBookId: "foreign", periodId: scope.periodId, accountingBasis: "Primary", aggregateId: "aggregate" } }));
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("does not match this journal");
  });
  it("refuses a different posted journal within the same book, period and basis", async () => {
    const postedJournal = { journalEntryId: scope.journalEntryId, ledgerBookId: scope.ledgerBookId, periodId: scope.periodId, accountingBasis: "Primary" as const, aggregateId: "aggregate" };
    vi.mocked(apiGetJson).mockResolvedValue(capability({ postedJournal }));
    expect((await getAssetAccountingDraftCapabilities(scope)).postedJournal?.journalEntryId).toBe(scope.journalEntryId);
    vi.mocked(apiGetJson).mockResolvedValue(capability({ postedJournal: { ...postedJournal, journalEntryId: "77777777-7777-7777-7777-777777777777" } }));
    await expect(getAssetAccountingDraftCapabilities(scope)).rejects.toThrow("does not match this journal");
  });
});
