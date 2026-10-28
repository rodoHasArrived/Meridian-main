import { createAssetApprovalFixture } from "@/test/asset-approval-fixtures";
import { act, renderHook, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AssetAccountingDraftCapabilities } from "@/types/asset-accounting-workflow";
import type { JournalEntryLifecycleActionRequest, JournalEntryLifecycleActionResult, ManualJournalEntryDraft } from "@/types";
import { useAssetAccountingWorkflow } from "./accounting-screen.asset-workflow";
const draft = { journalEntryId: "journal-1", fundProfileId: "fund-1", ledgerBookId: "book-1", periodId: "period-1", version: 3, status: "Submitted", assetAccountingDraft: {}, accountingBasis: "Primary" } as ManualJournalEntryDraft;
const capability = (patch: Partial<AssetAccountingDraftCapabilities> = {}): AssetAccountingDraftCapabilities => ({ protocolVersion: 1, authenticatedActor: "independent-checker", journalEntryId: draft.journalEntryId, fundProfileId: draft.fundProfileId, ledgerBookId: draft.ledgerBookId!, periodId: draft.periodId!, draftVersion: 3, candidateFingerprint: "a".repeat(64), canApprove: true, canPost: false, approveBlockedReason: null, postBlockedReason: "Approval is required.", approval: null, hasPendingPost: false, pendingPostCommand: null, postedJournal: null, ...patch });
function response(request: JournalEntryLifecycleActionRequest): JournalEntryLifecycleActionResult {
  return { journalEntry: { ...draft, status: request.action === "Post" ? "Posted" : "Approved", version: 4 }, transition: { transitionId: "transition", fromStatus: "Submitted", toStatus: "Approved", action: request.action, actor: "independent-checker", recordedAtUtc: "2026-10-28T00:00:00Z", correlationId: request.correlationId, evidenceLinks: [] }, generatedJournalEntries: [], ...(request.action === "Post" ? { postedJournal: { journalEntryId: draft.journalEntryId, ledgerBookId: draft.ledgerBookId!, periodId: draft.periodId!, accountingBasis: "Primary" as const, aggregateId: "aggregate", commandId: request.correlationId } } : {}) };
}
function services(value = capability()) {
  return { sessionIdentity: "checker-A", load: vi.fn().mockResolvedValue(value), execute: vi.fn().mockImplementation((request: JournalEntryLifecycleActionRequest) => Promise.resolve(response(request))), onResult: vi.fn(), refreshWorkbench: vi.fn().mockResolvedValue(undefined) };
}
async function confirm(result: { current: ReturnType<typeof useAssetAccountingWorkflow> }) {
  await waitFor(() => expect(result.current.capability).not.toBeNull());
  act(() => { result.current.setReviewNote("Independent review of retained source"); result.current.setConfirmed(true); });
}
beforeEach(() => { sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); });
afterEach(() => vi.unstubAllGlobals());
describe("Governed asset command state", () => {
  it("keeps old/missing capability and role denials read-only", async () => {
    const denied = services(capability({ canApprove: false, approveBlockedReason: "Maker cannot approve." }));
    const view = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...denied }));
    await confirm(view.result); expect(view.result.current.blockedReason("Approve")).toBe("Maker cannot approve.");
    await act(async () => view.result.current.run("Approve")); expect(denied.execute).not.toHaveBeenCalled();
    view.unmount();
    const old = services(capability({ protocolVersion: 0 }));
    const legacy = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...old }));
    await waitFor(() => expect(legacy.result.current.errorText).toContain("unsupported"));
    expect(legacy.result.current.capability).toBeNull();
  });
  it("retains the durable pending-post lock even when capability loading is unavailable", async () => {
    const api = services();
    const view = renderHook(() => useAssetAccountingWorkflow({ draft: { ...draft, assetAccountingPostIntent: {} }, active: true, ...api, load: undefined }));
    expect(view.result.current.hasPendingPost).toBe(true);
    expect(view.result.current.blockedReason("Post")).not.toBeNull();
  });

  it("binds an explicit reviewed decision to exact draft/version and a fresh request identity", async () => {
    const api = services(); const view = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api }));
    await confirm(view.result); await act(async () => view.result.current.run("Approve"));
    const sent = api.execute.mock.calls[0][0];
    expect(sent).toMatchObject({ journalEntryId: "journal-1", ledgerBookId: "book-1", fundProfileId: "fund-1", version: 3, action: "Approve", notes: "Independent review of retained source", evidenceLinks: [] });
    expect(sent.correlationId).toMatch(/^[a-f0-9-]{36}$/);
    expect(api.onResult).toHaveBeenCalledTimes(1);
  });
  it("retains and retries the exact failed command across a reload and remount", async () => {
    const api = services(); api.execute.mockRejectedValueOnce(new Error("Response interrupted"));
    const first = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api }));
    await confirm(first.result); await act(async () => first.result.current.run("Approve"));
    const original = api.execute.mock.calls[0][0]; first.unmount();
    const second = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api }));
    await confirm(second.result); await act(async () => second.result.current.run("Approve"));
    expect(api.execute.mock.calls[1][0]).toEqual(original);
  });
  it("clears prior-session retry state and refuses callbacks captured by another identity", async () => {
    const api = services(); api.execute.mockRejectedValueOnce(new Error("Outcome unknown"));
    const view = renderHook(({ identity }) => useAssetAccountingWorkflow({ draft, active: true, ...api, sessionIdentity: identity }), { initialProps: { identity: "checker-A" } });
    await confirm(view.result); const oldRun = view.result.current.run;
    await act(async () => view.result.current.run("Approve"));
    expect(view.result.current.pendingCommandId).not.toBeNull();
    view.rerender({ identity: "checker-B" });
    await waitFor(() => expect(view.result.current.capability).not.toBeNull());
    expect(view.result.current.pendingCommandId).toBeNull();
    expect(view.result.current.reviewNote).toBe("");
    await act(async () => oldRun("Approve"));
    expect(api.execute).toHaveBeenCalledTimes(1);
    expect(sessionStorage.length).toBe(0);
  });

  it("does not replay a prior actor's request when exposed session labels stay the same", async () => {
    const api = services(); api.execute.mockRejectedValueOnce(new Error("Outcome unknown"));
    const view = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api }));
    await confirm(view.result); const priorActorRun = view.result.current.run;
    await act(async () => view.result.current.run("Approve"));
    api.load.mockResolvedValueOnce(capability({ authenticatedActor: "different-authenticated-principal" }));
    await act(async () => view.result.current.reload());
    await waitFor(() => expect(view.result.current.capability?.authenticatedActor).toBe("different-authenticated-principal"));
    expect(view.result.current.pendingCommandId).toBeNull();
    expect(view.result.current.confirmed).toBe(false);
    await act(async () => priorActorRun("Approve"));
    expect(api.execute).toHaveBeenCalledTimes(1);
  });

  it("refuses decisions when session identity is unavailable despite a supported capability", async () => {
    const api = services(); const view = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api, sessionIdentity: null }));
    await confirm(view.result);
    expect(view.result.current.blockedReason("Approve")).toContain("session identity is unavailable");
    await act(async () => view.result.current.run("Approve")); expect(api.execute).not.toHaveBeenCalled();
  });

  it("rejects a corrupted cached request for another scope before dispatch", async () => {
    const scopeKey = JSON.stringify(["checker-A", draft.fundProfileId, draft.ledgerBookId, draft.periodId, draft.journalEntryId]);
    sessionStorage.setItem(`meridian.asset-accounting.command.v1:${scopeKey}`, JSON.stringify({ scopeKey, candidateFingerprint: "a".repeat(64), request: { journalEntryId: "foreign", ledgerBookId: "book-1", fundProfileId: "fund-1", action: "Approve", actor: "independent-checker", version: 3, correlationId: crypto.randomUUID() } }));
    const api = services(); const view = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api }));
    await waitFor(() => expect(view.result.current.errorText).toContain("selected journal scope"));
    expect(view.result.current.capability).toBeNull();
    await act(async () => view.result.current.run("Approve"));
    expect(api.execute).not.toHaveBeenCalled();
  });

  it("resumes the server's original posting command even when its prior version differs", async () => {
    const retained: JournalEntryLifecycleActionRequest = { journalEntryId: "journal-1", ledgerBookId: "book-1", fundProfileId: "fund-1", version: 2, action: "Post", actor: "independent-checker", notes: "Original posting decision", correlationId: "11111111-1111-1111-1111-111111111111", evidenceLinks: ["retained:approval"] };
    const api = services(capability({ canApprove: false, canPost: true, hasPendingPost: true, pendingPostCommand: retained, approval: createAssetApprovalFixture({ journalEntryId: draft.journalEntryId, ledgerBookId: draft.ledgerBookId!, periodId: draft.periodId!, fundProfileId: draft.fundProfileId, draftVersion: draft.version }).approval }));
    const view = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api }));
    await confirm(view.result); expect(view.result.current.hasPendingPost).toBe(true);
    expect(view.result.current.blockedReason("Approve")).toContain("retained command");
    await act(async () => view.result.current.run("Post"));
    expect(api.execute).toHaveBeenCalledWith(retained);
    expect(view.result.current.postedJournal?.journalEntryId).toBe(draft.journalEntryId);
  });
  it("preserves the unresolved Post when its receipt names another journal in the same period", async () => {
    const api = services(capability({ canApprove: false, canPost: true, approval: createAssetApprovalFixture({ journalEntryId: draft.journalEntryId, ledgerBookId: draft.ledgerBookId!, periodId: draft.periodId!, fundProfileId: draft.fundProfileId, draftVersion: draft.version }).approval }));
    api.execute.mockImplementationOnce((request: JournalEntryLifecycleActionRequest) => Promise.resolve({ ...response(request), postedJournal: { ...response(request).postedJournal!, journalEntryId: "other-journal" } }));
    const view = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api }));
    await confirm(view.result); await act(async () => view.result.current.run("Post"));
    expect(api.onResult).not.toHaveBeenCalled();
    expect(view.result.current.postedJournal).toBeNull();
    expect(view.result.current.pendingCommandId).toBe(api.execute.mock.calls[0][0].correlationId);
    expect(view.result.current.errorText).toContain("canonical journal receipt");
  });
  it("discards late reads and command results after a scope change", async () => {
    let resolve!: (value: JournalEntryLifecycleActionResult) => void;
    const api = services(); api.execute.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const view = renderHook(({ selected }) => useAssetAccountingWorkflow({ draft: selected, active: true, ...api }), { initialProps: { selected: draft } });
    await confirm(view.result); let pending!: Promise<void>;
    act(() => { pending = view.result.current.run("Approve"); });
    const original = api.execute.mock.calls[0][0];
    view.rerender({ selected: { ...draft, journalEntryId: "another-journal" } });
    await act(async () => { resolve(response(original)); await pending; });
    expect(api.onResult).not.toHaveBeenCalled(); expect(view.result.current.postedJournal).toBeNull();
  });
  it("refuses a mismatched result and a changed candidate fingerprint on retry", async () => {
    const api = services(); api.execute.mockImplementationOnce((request: JournalEntryLifecycleActionRequest) => Promise.resolve({ ...response(request), transition: { ...response(request).transition, correlationId: "another-command" } }));
    const view = renderHook(() => useAssetAccountingWorkflow({ draft, active: true, ...api }));
    await confirm(view.result); await act(async () => view.result.current.run("Approve"));
    expect(api.onResult).not.toHaveBeenCalled(); expect(view.result.current.pendingCommandId).not.toBeNull();
    api.load.mockResolvedValueOnce(capability({ candidateFingerprint: "b".repeat(64) }));
    await act(async () => view.result.current.reload());
    await waitFor(() => expect(view.result.current.errorText).toContain("different candidate"));
    expect(view.result.current.capability).toBeNull();
  });
});
