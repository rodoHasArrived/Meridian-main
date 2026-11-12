import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createOptimisticLedger,
  OptimisticRevertError,
  OptimisticUnlandedError,
  type OptimisticEdit,
  type OptimisticEditInput,
  type OptimisticHandlers
} from "./optimistic-ledger";

const edit: OptimisticEditInput = { rowId: "synthetic-row", field: "limit", prior: 10, value: 20 };

function setup(overrides: Partial<OptimisticHandlers> = {}) {
  const settlements: OptimisticEdit[] = [];
  const handlers: { current: OptimisticHandlers } = {
    current: {
      commit: vi.fn().mockResolvedValue({ receipt: "synthetic-receipt" }),
      revert: vi.fn().mockResolvedValue(undefined),
      onSettle: (settled) => settlements.push(settled),
      ...overrides
    }
  };
  return { ledger: createOptimisticLedger(handlers), handlers, settlements };
}

describe("createOptimisticLedger", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("holds a committed edit as pending until the transport resolves, then confirms it with the receipt", async () => {
    let release: (value: { receipt: string }) => void = () => {};
    const { ledger, settlements } = setup({
      commit: () => new Promise((resolve) => { release = resolve; })
    });

    const committing = ledger.commit(edit);
    expect(ledger.entries.map((entry) => entry.status)).toEqual(["pending"]);
    expect(ledger.latest("synthetic-row", "limit")?.value).toBe(20);

    release({ receipt: "chg-1" });
    await committing;

    expect(ledger.entries[0]).toMatchObject({ status: "confirmed", receipt: "chg-1", prior: 10, value: 20 });
    expect(settlements.map((entry) => entry.status)).toEqual(["confirmed"]);
  });

  it("rejects with the transport's reason verbatim and settles exactly once", async () => {
    const { ledger, settlements } = setup({
      commit: () => Promise.reject(new Error("transport denied"))
    });

    await ledger.commit(edit);

    expect(ledger.entries[0]).toMatchObject({ status: "rejected", reason: "transport denied" });
    expect(settlements.map((entry) => [entry.status, entry.reason])).toEqual([["rejected", "transport denied"]]);
  });

  it("rejects when the transport throws synchronously or without a message", async () => {
    const { ledger } = setup({
      commit: () => { throw new Error("synchronous refusal"); }
    });
    await ledger.commit(edit);
    expect(ledger.entries[0]).toMatchObject({ status: "rejected", reason: "synchronous refusal" });

    const silent = setup({ commit: () => Promise.reject(undefined) });
    await silent.ledger.commit(edit);
    expect(silent.ledger.entries[0]).toMatchObject({ status: "rejected", reason: "rejected by the write path" });
  });

  it("does not let observer failures change a confirmed write or starve later subscribers", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    const { ledger, handlers } = setup({
      onSettle: () => { throw new Error("consumer projection failed"); }
    });
    const seen: string[][] = [];
    ledger.subscribe(() => { throw new Error("subscriber failed"); });
    ledger.subscribe((entries) => seen.push(entries.map((entry) => entry.status)));

    await ledger.commit(edit);

    expect(handlers.current.commit).toHaveBeenCalledTimes(1);
    expect(ledger.entries[0]).toMatchObject({ status: "confirmed", receipt: "synthetic-receipt" });
    expect(seen).toEqual([["pending"], ["confirmed"]]);
    expect(consoleError).toHaveBeenCalled();
    for (const call of consoleError.mock.calls) {
      expect(String(call[0])).toMatch(/^OptimisticProvider: (ledger|onSettle) observer failed/);
    }
  });

  it("unwinds a batch newest first under one reason and keeps every entry as its own reverted write", async () => {
    const { ledger, handlers, settlements } = setup();

    await ledger.commit(edit);
    await ledger.commit({ ...edit, prior: 20, value: 30 });
    await ledger.revert(ledger.entries.map((entry) => entry.id), "Synthetic correction");

    expect(ledger.entries.map((entry) => entry.status)).toEqual(["reverted", "reverted"]);
    expect(ledger.entries).toHaveLength(2);
    const revert = vi.mocked(handlers.current.revert!);
    expect(revert).toHaveBeenCalledTimes(1);
    expect(revert.mock.calls[0][0].map((entry) => entry.value)).toEqual([30, 20]);
    expect(revert.mock.calls[0][1]).toBe("Synthetic correction");
    expect(settlements.map((entry) => [entry.status, entry.value])).toEqual([
      ["confirmed", 20],
      ["confirmed", 30],
      ["reverted", 30],
      ["reverted", 20]
    ]);
    for (const entry of ledger.entries) {
      expect(entry.reason).toBe("Synthetic correction");
      expect(entry.revertedAt).toEqual(expect.any(Number));
      expect(entry.revertError).toBeUndefined();
    }
  });

  it("keeps a confirmed edit confirmed, with the failure attached, when the compensating write throws", async () => {
    const { ledger, settlements } = setup({
      revert: () => Promise.reject(new Error("compensation denied"))
    });

    await ledger.commit(edit);
    await ledger.revert(ledger.entries[0].id, "Synthetic correction");

    expect(ledger.entries[0]).toMatchObject({ status: "confirmed", revertError: "compensation denied" });
    expect(ledger.entries[0].reason).toBeUndefined();
    expect(settlements.map((entry) => entry.status)).toEqual(["confirmed"]);
  });

  it("clears the previous revert failure when a later revert of the same edit lands", async () => {
    const revert = vi.fn()
      .mockRejectedValueOnce(new Error("compensation denied"))
      .mockResolvedValueOnce(undefined);
    const { ledger } = setup({ revert });

    await ledger.commit(edit);
    await ledger.revert(ledger.entries[0].id, "First try");
    await ledger.revert(ledger.entries[0].id, "Second try");

    expect(ledger.entries[0]).toMatchObject({ status: "reverted", reason: "Second try" });
    expect(ledger.entries[0].revertError).toBeUndefined();
  });

  it("does not revert an older edit beneath a newer confirmed edit that was not selected", async () => {
    const { ledger, handlers } = setup();

    await ledger.commit(edit);
    await ledger.commit({ ...edit, prior: 20, value: 30 });
    await ledger.revert(ledger.entries[0].id, "Only the older one");

    expect(handlers.current.revert).not.toHaveBeenCalled();
    expect(ledger.entries.map((entry) => entry.status)).toEqual(["confirmed", "confirmed"]);
  });

  it("does not revert beneath a newer write that is still in flight", async () => {
    let release: () => void = () => {};
    const commit = vi.fn()
      .mockResolvedValueOnce(undefined)
      .mockImplementationOnce(() => new Promise<void>((resolve) => { release = resolve; }));
    const { ledger, handlers } = setup({ commit });

    await ledger.commit(edit);
    const held = ledger.commit({ ...edit, prior: 20, value: 30 });
    await ledger.revert(ledger.entries.map((entry) => entry.id), "While one is provisional");

    expect(handlers.current.revert).not.toHaveBeenCalled();
    expect(ledger.entries.map((entry) => entry.status)).toEqual(["confirmed", "pending"]);

    release();
    await held;
    expect(ledger.entries.map((entry) => entry.status)).toEqual(["confirmed", "confirmed"]);
  });

  it("offers no revert, and changes nothing, when there is no compensating write", async () => {
    const { ledger, settlements } = setup({ revert: undefined });

    await ledger.commit(edit);
    expect(ledger.canRevert).toBe(false);
    await ledger.revert(ledger.entries[0].id, "No transport for this");

    expect(ledger.entries[0].status).toBe("confirmed");
    expect(settlements.map((entry) => entry.status)).toEqual(["confirmed"]);
  });

  it("skips dismissed entries when reading the latest edit for a cell", async () => {
    const { ledger } = setup({ commit: () => Promise.reject(new Error("no")) });

    await ledger.commit(edit);
    expect(ledger.latest("synthetic-row", "limit")?.status).toBe("rejected");
    ledger.dismiss(ledger.entries[0].id);

    expect(ledger.latest("synthetic-row", "limit")).toBeNull();
    expect(ledger.entries[0].status).toBe("dismissed");
    expect(ledger.forRow("synthetic-row")).toHaveLength(1);
    expect(ledger.forRow("another-row")).toHaveLength(0);
  });

  it("reads the latest handlers on every call and stops notifying after unsubscribe", async () => {
    const { ledger, handlers } = setup();
    const listener = vi.fn();
    const unsubscribe = ledger.subscribe(listener);

    const replacement = vi.fn().mockResolvedValue(undefined);
    handlers.current = { ...handlers.current, commit: replacement };
    await ledger.commit(edit);
    expect(replacement).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledTimes(2);

    unsubscribe();
    await ledger.commit({ ...edit, prior: 20, value: 30 });
    expect(listener).toHaveBeenCalledTimes(2);
  });

  describe("a write that got no answer", () => {
    it("records an OptimisticUnlandedError as unlanded, not rejected, and settles once", async () => {
      const { ledger, settlements } = setup({
        commit: () => Promise.reject(new OptimisticUnlandedError("no answer came back"))
      });

      await ledger.commit(edit);

      expect(ledger.entries[0]).toMatchObject({ status: "unlanded", reason: "no answer came back" });
      expect(settlements.map((entry) => [entry.status, entry.reason])).toEqual([["unlanded", "no answer came back"]]);
    });

    it("does not revert a confirmed edit beneath a newer unlanded edit until that one is dismissed", async () => {
      const commit = vi.fn()
        .mockResolvedValueOnce(undefined)
        .mockRejectedValueOnce(new OptimisticUnlandedError("no answer came back"));
      const { ledger, handlers } = setup({ commit });
      await ledger.commit(edit);
      await ledger.commit({ ...edit, prior: 20, value: 30 });
      const [older, unlanded] = ledger.entries;

      expect(ledger.revertTargets(older.id)).toEqual([]);
      await ledger.revert(older.id, "Too early");
      expect(handlers.current.revert).not.toHaveBeenCalled();

      ledger.dismiss(unlanded.id);
      expect(ledger.revertTargets(older.id).map((entry) => entry.id)).toEqual([older.id]);
    });

    it("turns an unlanded edit into a confirmed one when the model is found holding its value", async () => {
      const { ledger, settlements } = setup({
        commit: () => Promise.reject(new OptimisticUnlandedError("no answer came back"))
      });
      ledger.reconcile("synthetic-row", "limit", 10);
      await ledger.commit(edit);

      // Still the prior: nothing is known yet.
      ledger.reconcile("synthetic-row", "limit", 10);
      expect(ledger.entries[0].status).toBe("unlanded");

      ledger.reconcile("synthetic-row", "limit", 20);
      expect(ledger.entries[0]).toMatchObject({ status: "confirmed", prior: 10, value: 20 });
      expect(ledger.entries[0].reason).toBeUndefined();
      expect(settlements.map((entry) => entry.status)).toEqual(["unlanded", "confirmed"]);
    });

    it("leaves an unlanded edit alone when the model was not in step with the screen at commit", async () => {
      const { ledger } = setup({
        commit: () => Promise.reject(new OptimisticUnlandedError("no answer came back"))
      });
      // The model was never reconciled at the prior, so its holding 20 proves nothing.
      await ledger.commit(edit);

      ledger.reconcile("synthetic-row", "limit", 20);

      expect(ledger.entries[0].status).toBe("unlanded");
    });

    it("keeps the edit confirmed with the fate marked unknown when the compensating write got no answer, and completes the revert once the model shows the prior", async () => {
      const { ledger, settlements } = setup({
        revert: () => Promise.reject(new OptimisticUnlandedError("no answer to the revert"))
      });
      await ledger.commit(edit);
      ledger.reconcile("synthetic-row", "limit", 20);

      await ledger.revert(ledger.entries[0].id, "Entered on the wrong row");

      expect(ledger.entries[0]).toMatchObject({
        status: "confirmed",
        revertError: "no answer to the revert",
        revertUnlanded: true
      });
      ledger.reconcile("synthetic-row", "limit", 20);
      expect(ledger.entries[0].status).toBe("confirmed");

      // A later read shows the prior: the compensating write landed. It is recorded with its reason.
      ledger.reconcile("synthetic-row", "limit", 10);
      expect(ledger.entries[0]).toMatchObject({ status: "reverted", reason: "Entered on the wrong row" });
      expect(ledger.entries[0].revertError).toBeUndefined();
      expect(ledger.entries[0].revertUnlanded).toBeUndefined();
      expect(settlements.map((entry) => entry.status)).toEqual(["confirmed", "reverted"]);
    });
  });

  describe("a batch revert that lands in part", () => {
    it("records the reverts that landed and leaves only the others confirmed, each with its own reason", async () => {
      const revert = vi.fn(async (targets: OptimisticEdit[]) => {
        throw new OptimisticRevertError("store went read-only", {
          landed: [targets[0].id],
          unlanded: [targets[2].id],
          reasons: { [targets[2].id]: "not attempted" }
        });
      });
      const { ledger, settlements } = setup({ revert });
      await ledger.commit({ ...edit, rowId: "a" });
      await ledger.commit({ ...edit, rowId: "b" });
      await ledger.commit({ ...edit, rowId: "c" });
      const [a, b, c] = ledger.entries;

      await ledger.revert([a.id, b.id, c.id], "End-of-day unwind");

      // Newest first: c was written, b failed with the message, a carries its own reason.
      expect(ledger.entries.map((entry) => [entry.rowId, entry.status])).toEqual([
        ["a", "confirmed"],
        ["b", "confirmed"],
        ["c", "reverted"]
      ]);
      expect(ledger.entries[2]).toMatchObject({ reason: "End-of-day unwind", revertedAt: expect.any(Number) });
      expect(ledger.entries[2].revertError).toBeUndefined();
      expect(ledger.entries[1]).toMatchObject({ revertError: "store went read-only" });
      expect(ledger.entries[1].revertUnlanded).toBeUndefined();
      expect(ledger.entries[0]).toMatchObject({ revertError: "not attempted", revertUnlanded: true });
      expect(settlements.filter((entry) => entry.status === "reverted").map((entry) => [entry.rowId, entry.reason])).toEqual([
        ["c", "End-of-day unwind"]
      ]);
    });
  });

  describe("reconcile", () => {
    const row = "synthetic-row";
    const statuses = (ledger: ReturnType<typeof setup>["ledger"]) => ledger.entries.map((entry) => entry.status);

    it("waits while the model has not caught up with a confirmed edit, then follows it", async () => {
      const { ledger } = setup();
      ledger.reconcile(row, "limit", 10);
      await ledger.commit(edit);

      ledger.reconcile(row, "limit", 10);
      expect(statuses(ledger)).toEqual(["confirmed"]);
      ledger.reconcile(row, "limit", 20);
      expect(statuses(ledger)).toEqual(["confirmed"]);
    });

    it("dismisses a confirmed edit once the model has held it and then moved on", async () => {
      const { ledger } = setup();
      await ledger.commit(edit);
      ledger.reconcile(row, "limit", 20);

      ledger.reconcile(row, "limit", 70);

      expect(statuses(ledger)).toEqual(["dismissed"]);
    });

    it("dismisses a confirmed edit when the model holds a value this ledger never wrote", async () => {
      const { ledger } = setup();
      await ledger.commit(edit);

      ledger.reconcile(row, "limit", 70);

      expect(statuses(ledger)).toEqual(["dismissed"]);
    });

    it("remembers that the model held an edit, so going back to the prior later is not mistaken for lag", async () => {
      const { ledger } = setup();
      await ledger.commit(edit);
      ledger.reconcile(row, "limit", 20);

      // Another write path put the original value back. A fresh cell asking is told the same.
      ledger.reconcile(row, "limit", 10);

      expect(statuses(ledger)).toEqual(["dismissed"]);
    });

    it("clears every stacked confirmed edit when another write path returns the field to the first prior", async () => {
      const { ledger } = setup();
      await ledger.commit(edit);
      ledger.reconcile(row, "limit", 20);
      await ledger.commit({ ...edit, prior: 20, value: 30 });
      ledger.reconcile(row, "limit", 30);

      ledger.reconcile(row, "limit", 10);

      expect(statuses(ledger)).toEqual(["dismissed", "dismissed"]);
      expect(ledger.latest(row, "limit")).toBeNull();
    });

    it("checks the confirmed edit beneath a reverted note, so it cannot be reverted over a newer value", async () => {
      const { ledger, handlers } = setup();
      await ledger.commit(edit);
      ledger.reconcile(row, "limit", 20);
      await ledger.commit({ ...edit, prior: 20, value: 30 });
      ledger.reconcile(row, "limit", 30);
      const [first, second] = ledger.entries;
      await ledger.revert(second.id, "Second one was wrong");
      ledger.reconcile(row, "limit", 20);
      expect(statuses(ledger)).toEqual(["confirmed", "reverted"]);

      // The full-form path writes 80 while the Reverted note is still on screen.
      ledger.reconcile(row, "limit", 80);

      // The stale confirmed edit is gone; the revert that did happen keeps its record.
      expect(statuses(ledger)).toEqual(["dismissed", "reverted"]);
      expect(ledger.revertTargets([first.id, second.id])).toEqual([]);
      await ledger.revert([first.id, second.id], "Batch");
      expect(handlers.current.revert).toHaveBeenCalledTimes(1);
    });

    it("checks the confirmed edit beneath a rejected note as well", async () => {
      const commit = vi.fn()
        .mockResolvedValueOnce(undefined)
        .mockRejectedValueOnce(new Error("limit too high"));
      const { ledger } = setup({ commit });
      await ledger.commit(edit);
      ledger.reconcile(row, "limit", 20);
      await ledger.commit({ ...edit, prior: 20, value: 900 });
      expect(statuses(ledger)).toEqual(["confirmed", "rejected"]);

      ledger.reconcile(row, "limit", 80);

      expect(statuses(ledger)).toEqual(["dismissed", "rejected"]);
    });

    it("takes older notes away with a superseded edit instead of uncovering them", async () => {
      const commit = vi.fn()
        .mockRejectedValueOnce(new Error("try again later"))
        .mockResolvedValueOnce(undefined);
      const { ledger } = setup({ commit });
      await ledger.commit(edit);
      await ledger.commit(edit);
      ledger.reconcile(row, "limit", 20);
      expect(statuses(ledger)).toEqual(["rejected", "confirmed"]);

      ledger.reconcile(row, "limit", 80);

      expect(statuses(ledger)).toEqual(["dismissed", "dismissed"]);
    });

    it("does not judge a revert before the model has been put back", async () => {
      const { ledger } = setup();
      await ledger.commit(edit);
      ledger.reconcile(row, "limit", 20);
      await ledger.commit({ ...edit, prior: 20, value: 30 });
      ledger.reconcile(row, "limit", 30);
      await ledger.revert(ledger.entries[1].id, "Second one was wrong");

      // The model still shows 30: the consumer has not applied the revert yet.
      ledger.reconcile(row, "limit", 30);
      expect(statuses(ledger)).toEqual(["confirmed", "reverted"]);

      ledger.reconcile(row, "limit", 20);
      expect(statuses(ledger)).toEqual(["confirmed", "reverted"]);
    });

    it("changes nothing while a write for the field is in flight, and leaves other fields alone", async () => {
      let release: () => void = () => {};
      const commit = vi.fn()
        .mockResolvedValueOnce(undefined)
        .mockResolvedValueOnce(undefined)
        .mockImplementationOnce(() => new Promise<void>((resolve) => { release = resolve; }));
      const { ledger } = setup({ commit });
      await ledger.commit(edit);
      await ledger.commit({ ...edit, field: "owner", prior: "a", value: "b" });
      ledger.reconcile(row, "limit", 20);
      const held = ledger.commit({ ...edit, prior: 20, value: 30 });
      const listener = vi.fn();
      ledger.subscribe(listener);

      ledger.reconcile(row, "limit", 80);

      expect(statuses(ledger)).toEqual(["confirmed", "confirmed", "pending"]);
      expect(listener).not.toHaveBeenCalled();
      release();
      await held;
    });

    it("emits nothing when the model agrees with the ledger", async () => {
      const { ledger } = setup();
      await ledger.commit(edit);
      const listener = vi.fn();
      ledger.subscribe(listener);

      ledger.reconcile(row, "limit", 20);
      ledger.reconcile(row, "limit", 20);
      ledger.reconcile("another-row", "limit", 5);

      expect(listener).not.toHaveBeenCalled();
    });
  });
});
