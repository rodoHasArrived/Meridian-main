import { useState } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import {
  getStrategyDesignerDraft,
  getStrategyDesignerDrafts,
  getStrategyDesignerTemplates,
  listEvidenceVaultDocuments,
  runStrategyDesignerBacktest,
  saveStrategyDesignerDraft
} from "@/lib/api";
import type { StrategyDesignRunBacktestResponse } from "@/types";
import type { StrategyBuilderDocument } from "./strategy-designer-screen.view-model";
import { toStrategyDesignDocument, useHostedDesigner } from "./strategy-designer-hosted";

vi.mock("@/lib/api", () => ({
  getStrategyDesignerDraft: vi.fn(),
  getStrategyDesignerDrafts: vi.fn(),
  getStrategyDesignerTemplates: vi.fn(),
  listEvidenceVaultDocuments: vi.fn(),
  runStrategyDesignerBacktest: vi.fn(),
  saveStrategyDesignerDraft: vi.fn()
}));

const accountA = "10000000-0000-0000-0000-000000000001";
const accountB = "20000000-0000-0000-0000-000000000002";
const validation = { isValid: true, summary: "Valid", messages: [] };

function design(): StrategyBuilderDocument {
  return {
    documentId: "saved-equity-design", name: "Equity filter", description: "Retained draft",
    version: "1", revision: "revision-1", dirty: false, datasetReference: "daily-bars",
    universe: ["TESTX"], createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z",
    metadata: { original: "retained evidence metadata" },
    cells: [
      { cellId: "price-filter", label: "Filter", kind: "visual", purpose: "filter", source: "PRICE > 100", fieldRefs: ["PRICE"] },
      { cellId: "fixed-shares", label: "Buy", kind: "trade", purpose: "trade", source: "Buy", fieldRefs: [], parameters: { sizingValue: "10" } }
    ],
    transitions: []
  };
}

function outcome(runId = "retained-run"): StrategyDesignRunBacktestResponse {
  return {
    success: true, runId, strategyId: "saved-equity-design", strategyName: "Equity filter",
    validation, compiled: { source: "", datasetFingerprint: "snapshot-1", fieldRefs: [], disabledFieldRefs: [] },
    trace: [], previewRows: [], metrics: { "Net P&L": "19" }, runtimeError: null,
    promotionCandidatePath: null, reviewPacketPath: null, strategyVersionSha256: "strategy-version",
    dataSnapshotSha256: "retained-data-snapshot", dataProvenanceToken: "simulated"
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

function mount(document = design(), account = accountA) {
  const applyDocument = vi.fn();
  const hook = renderHook(({ selectedDocument, selectedAccount }) => useHostedDesigner(selectedDocument, applyDocument, selectedAccount), {
    initialProps: { selectedDocument: document, selectedAccount: account }
  });
  act(() => {
    hook.result.current.setFrom("2024-01-02");
    hook.result.current.setTo("2024-01-05");
    hook.result.current.setCriteria("Retain the result");
    hook.result.current.setVaultReference("evidence://evidence-vault/ev-0123456789abcdef01234567");
  });
  expect(hook.result.current.blocker).toBeNull();
  return { ...hook, applyDocument };
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getStrategyDesignerDrafts).mockResolvedValue([]);
  vi.mocked(getStrategyDesignerTemplates).mockResolvedValue([]);
  vi.mocked(listEvidenceVaultDocuments).mockResolvedValue([]);
});

describe("useHostedDesigner submitted proof scope", () => {
  it("keeps the outcome unchanged and does not turn the selected account into retained ownership", async () => {
    const retained = outcome();
    vi.mocked(runStrategyDesignerBacktest).mockResolvedValue(retained);
    const hook = mount();
    await act(async () => { hook.result.current.runBacktest(); });
    expect(hook.result.current.run).toBe(retained);
    expect(hook.result.current.run).not.toHaveProperty("fundAccountId");
    expect(runStrategyDesignerBacktest).toHaveBeenCalledWith(expect.objectContaining({
      fundAccountId: accountA, from: "2024-01-02", to: "2024-01-05",
      document: expect.objectContaining({ documentId: "saved-equity-design", revision: "revision-1" })
    }));
  });

  it("withholds completed proof and its status on A → B → A without replacing the draft", async () => {
    const document = design();
    vi.mocked(runStrategyDesignerBacktest).mockResolvedValue(outcome());
    const hook = mount(document);
    await act(async () => { hook.result.current.runBacktest(); });
    expect(hook.result.current.message).toBe("Backtest completed and retained.");
    hook.rerender({ selectedDocument: document, selectedAccount: accountB });
    expect(hook.result.current.run).toBeNull();
    expect(hook.result.current.message).toBe("");
    expect(hook.result.current.error).toBe("");
    hook.rerender({ selectedDocument: document, selectedAccount: accountA });
    expect(hook.result.current.run).toBeNull();
    expect(hook.result.current.message).toBe("");
    expect(hook.applyDocument).not.toHaveBeenCalled();
  });

  it.each(["success", "error"] as const)("discards late A %s and explicitly waits before allowing B to run", async (settlement) => {
    const pendingA = deferred<StrategyDesignRunBacktestResponse>();
    const pendingB = deferred<StrategyDesignRunBacktestResponse>();
    vi.mocked(runStrategyDesignerBacktest).mockReturnValueOnce(pendingA.promise).mockReturnValueOnce(pendingB.promise);
    const document = design();
    const hook = mount(document);
    act(() => hook.result.current.runBacktest());
    hook.rerender({ selectedDocument: document, selectedAccount: accountB });
    expect(hook.result.current.busy).toBe("running");
    expect(hook.result.current.blocker).toContain("previous scope is still running");
    act(() => hook.result.current.runBacktest());
    expect(runStrategyDesignerBacktest).toHaveBeenCalledTimes(1);
    await act(async () => {
      if (settlement === "success") pendingA.resolve(outcome("run-A"));
      else pendingA.reject(new Error("Account A failed"));
    });
    expect(hook.result.current.busy).toBeNull();
    expect(hook.result.current.blocker).toBeNull();
    expect(hook.result.current.run).toBeNull();
    expect(hook.result.current.message).toBe("");
    expect(hook.result.current.error).toBe("");
    act(() => hook.result.current.runBacktest());
    expect(hook.result.current.busy).toBe("running");
    expect(runStrategyDesignerBacktest).toHaveBeenLastCalledWith(expect.objectContaining({ fundAccountId: accountB }));
    const retainedB = outcome("run-B");
    await act(async () => pendingB.resolve(retainedB));
    expect(hook.result.current.run).toBe(retainedB);
    expect(hook.result.current.busy).toBeNull();
  });

  it("rejects a late A result after A → B → A even though the request fields match again", async () => {
    const pending = deferred<StrategyDesignRunBacktestResponse>();
    vi.mocked(runStrategyDesignerBacktest).mockReturnValue(pending.promise);
    const document = design();
    const hook = mount(document);
    act(() => hook.result.current.runBacktest());
    hook.rerender({ selectedDocument: document, selectedAccount: accountB });
    hook.rerender({ selectedDocument: document, selectedAccount: accountA });
    await act(async () => pending.resolve(outcome()));
    expect(hook.result.current.run).toBeNull();
    expect(hook.result.current.message).toBe("");
    expect(hook.result.current.error).toBe("");
  });

  it("binds the complete serialized graph and keeps the submitted request independent of later mutation", async () => {
    const pending = deferred<StrategyDesignRunBacktestResponse>();
    vi.mocked(runStrategyDesignerBacktest).mockReturnValue(pending.promise);
    const document = design();
    const hook = mount(document);
    act(() => hook.result.current.runBacktest());
    const sent = vi.mocked(runStrategyDesignerBacktest).mock.calls[0][0];
    document.cells[0].source = "PRICE > 999";
    document.metadata = { original: "changed without a revision or dirty flag" };
    hook.rerender({ selectedDocument: document, selectedAccount: accountA });
    expect(sent.document.cells[0].source).toBe("PRICE > 100");
    expect(sent.document.metadata).toEqual({ original: "retained evidence metadata" });
    await act(async () => pending.resolve(outcome()));
    expect(hook.result.current.run).toBeNull();
    expect(hook.result.current.message).toBe("");
    expect(hook.applyDocument).not.toHaveBeenCalled();
  });

  it("keeps consecutive dirty graph edits intact and does not revive proof when the saved graph returns", async () => {
    const pending = deferred<StrategyDesignRunBacktestResponse>();
    vi.mocked(runStrategyDesignerBacktest).mockReturnValue(pending.promise);
    const saved = design();
    const hook = mount(saved);
    act(() => hook.result.current.runBacktest());
    const firstEdit = { ...saved, dirty: true, cells: saved.cells.map(cell => ({ ...cell, source: "First unsaved edit" })) };
    const secondEdit = { ...firstEdit, cells: firstEdit.cells.map(cell => ({ ...cell, source: "Second unsaved edit" })) };
    hook.rerender({ selectedDocument: firstEdit, selectedAccount: accountA });
    hook.rerender({ selectedDocument: secondEdit, selectedAccount: accountA });
    expect(hook.result.current.priceFilter?.source).toBe("Second unsaved edit");
    act(() => hook.result.current.runBacktest());
    expect(runStrategyDesignerBacktest).toHaveBeenCalledTimes(1);
    hook.rerender({ selectedDocument: saved, selectedAccount: accountA });
    await act(async () => pending.resolve(outcome()));
    expect(hook.result.current.run).toBeNull();
    expect(hook.applyDocument).not.toHaveBeenCalled();
  });

  it.each(["revision", "document", "date", "evidence"] as const)("invalidates an earlier result when its %s input changes", async (field) => {
    vi.mocked(runStrategyDesignerBacktest).mockResolvedValue(outcome());
    const document = design();
    const hook = mount(document);
    await act(async () => { hook.result.current.runBacktest(); });
    if (field === "revision") hook.rerender({ selectedDocument: { ...document, revision: "revision-2" }, selectedAccount: accountA });
    if (field === "document") hook.rerender({ selectedDocument: { ...document, documentId: "other-design" }, selectedAccount: accountA });
    if (field === "date") act(() => hook.result.current.setTo("2024-02-01"));
    if (field === "evidence") act(() => hook.result.current.setVaultReference("evidence://evidence-vault/ev-fedcba9876543210fedcba98"));
    expect(hook.result.current.run).toBeNull();
    expect(hook.result.current.message).toBe("");
    expect(hook.applyDocument).not.toHaveBeenCalled();
  });

  it("rejects a stale submit handler after the selected account changes", () => {
    const document = design();
    const hook = mount(document);
    const submitA = hook.result.current.runBacktest;
    hook.rerender({ selectedDocument: document, selectedAccount: accountB });
    act(() => submitA());
    expect(runStrategyDesignerBacktest).not.toHaveBeenCalled();
    expect(hook.result.current.busy).toBeNull();
  });

  it("rejects a response for another design instead of retaining its metrics", async () => {
    vi.mocked(runStrategyDesignerBacktest).mockResolvedValue({ ...outcome(), strategyId: "foreign-design" });
    const hook = mount();
    await act(async () => { hook.result.current.runBacktest(); });
    expect(hook.result.current.run).toBeNull();
    expect(hook.result.current.error).toContain("did not match the submitted design or account scope");
  });

  it.each([accountB, "not-an-account", "", 17])("rejects an explicit invalid or mismatched account claim: %s", async (claim) => {
    vi.mocked(runStrategyDesignerBacktest).mockResolvedValue(Object.assign(outcome(), { fundAccountId: claim }));
    const hook = mount();
    await act(async () => { hook.result.current.runBacktest(); });
    expect(hook.result.current.run).toBeNull();
    expect(hook.result.current.error).toContain("did not match the submitted design or account scope");
  });

  it.each([accountA, null])("preserves a compatible explicit account claim unchanged: %s", async (claim) => {
    const retained = Object.assign(outcome(), { fundAccountId: claim });
    vi.mocked(runStrategyDesignerBacktest).mockResolvedValue(retained);
    const hook = mount();
    await act(async () => { hook.result.current.runBacktest(); });
    expect(hook.result.current.run).toBe(retained);
    expect(hook.result.current.run).toHaveProperty("fundAccountId", claim);
  });

  it("does not discard generic save and open results when their own revision or account context changes", async () => {
    const initial = design();
    const saved = { ...toStrategyDesignDocument(initial), revision: "revision-2" };
    const opened = { ...saved, documentId: "another-saved-design", revision: "revision-3" };
    vi.mocked(saveStrategyDesignerDraft).mockResolvedValue({
      document: saved,
      summary: { ...saved, cellCount: saved.cells.length, transitionCount: 0, validationSummary: "Valid" },
      validation, trace: []
    });
    const openReply = deferred<typeof opened>();
    vi.mocked(getStrategyDesignerDraft).mockReturnValue(openReply.promise);
    const hook = renderHook(({ account }) => {
      const [document, setDocument] = useState(initial);
      return { document, hosted: useHostedDesigner(document, setDocument, account) };
    }, { initialProps: { account: accountA } });
    await act(async () => hook.result.current.hosted.save());
    await waitFor(() => expect(hook.result.current.document.revision).toBe("revision-2"));
    expect(hook.result.current.hosted.message).toBe("Draft saved.");
    act(() => hook.result.current.hosted.openDraft(opened.documentId));
    hook.rerender({ account: accountB });
    await act(async () => openReply.resolve(opened));
    expect(hook.result.current.document.documentId).toBe(opened.documentId);
    expect(hook.result.current.document.metadata).toEqual(initial.metadata);
    expect(hook.result.current.hosted.message).toBe("Saved draft opened.");
    expect(hook.result.current.hosted.run).toBeNull();
  });
});
