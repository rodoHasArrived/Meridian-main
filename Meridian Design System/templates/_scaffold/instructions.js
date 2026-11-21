// Meridian instructions — the pending-instruction store that outlives a screen (increment B;
// docs/SUITE_COHESION_4_4.md › E-4).
//
// `ActionEnvelope` states the fate of one instruction. `ActionQueue` keeps every unresolved one
// visible — but both live in a React tree, so navigating away from a screen mid-commit dropped the
// instruction whose whole purpose was to stay visible until it landed. An operator who clicks Post
// and then opens the ledger to look at the consequence should not thereby lose the receipt.
//
// This store sits above screen lifetime. It writes ONLY its own key, restores on load, and marks
// what it restored so a tray can say "carried over from Reconciliation".
//
// Durability is declared, never assumed:
//   durable.kind === "sessionStorage"  → survives navigation and reload in this tab
//   durable.kind === "memory"          → no store available; survives nothing, and says so
// There is no "probably persisted". A record this store cannot write is a record it does not claim.
//
//   MeridianInstructions.submit({ action, label, idempotencyKey, endpoint, payload, origin })
//   MeridianInstructions.settle(id, answer)      // answer = the transport's reply, read strictly
//   MeridianInstructions.unresolved()            // inflight · acknowledged-without-receipt · unlanded
//   MeridianInstructions.reconcile(probe)        // ask the adapter about unresolved keys
//   MeridianInstructions.subscribe(fn)           // → unsubscribe
(function (global) {
  "use strict";

  var KEY = "meridian.instructions.v1";
  var MAX = 40;

  var store = null, kind = "memory", note = "No session store available — instructions live only as long as this page.";
  try {
    var probe = global.sessionStorage;
    probe.setItem(KEY + ".probe", "1");
    probe.removeItem(KEY + ".probe");
    store = probe; kind = "sessionStorage";
    note = "Session store — instructions survive navigation and reload in this tab, not a new tab.";
  } catch (e) {
    if (global.console) console.warn("[Meridian] instructions: no sessionStorage (" + String((e && e.message) || e) + ") — the queue will not survive navigation, and the tray says so.");
  }

  var list = [];
  var subs = [];
  var seq = 0;

  function read() {
    if (!store) return [];
    try {
      var raw = store.getItem(KEY);
      var parsed = raw ? JSON.parse(raw) : [];
      return Object.prototype.toString.call(parsed) === "[object Array]" ? parsed : [];
    } catch (e) { return []; }
  }
  function write() {
    if (!store) return;
    try { store.setItem(KEY, JSON.stringify(list.slice(0, MAX))); } catch (e) { /* quota — the tray still shows the live list */ }
  }
  function emit() { subs.slice().forEach(function (fn) { try { fn(list.slice()); } catch (e) { /* a bad subscriber is not a lost instruction */ } }); }

  // Restore. Anything that was in flight when the screen unmounted has an UNKNOWN fate — the page
  // went away, the write may not have. It comes back as `unlanded` with that stated, never as
  // landed and never silently dropped.
  list = read().map(function (i) {
    if (i.state === "inflight") {
      return Object.assign({}, i, {
        state: "unlanded", restored: true,
        reason: "The screen that sent this instruction closed before an answer arrived — the fate is unknown. Retry with the same key, or reconcile against the system of record.",
      });
    }
    return Object.assign({}, i, { restored: true });
  });
  if (list.length) write();

  function stamp() {
    return (global.MeridianWorld && global.MeridianWorld.stamp)
      ? global.MeridianWorld.stamp()
      : new Date().toISOString().replace("T", " ").replace(/\.\d+Z$/, "Z");
  }

  /**
   * Read a transport answer. Deliberately the same strictness as `ActionEnvelope.readAnswer`:
   * `ok === true` and nothing less is a landing, the receipt id comes from the adapter, and a
   * refusal without a reason is not a decision.
   */
  function readAnswer(answer) {
    if (answer == null || typeof answer !== "object") {
      return { state: "unlanded", reason: "Adapter answered " + (answer === undefined ? "nothing" : JSON.stringify(answer)) + " — not an acknowledgement this system can read." };
    }
    if (answer.ok === false) {
      return answer.reason
        ? { state: "rejected", reason: String(answer.reason) }
        : { state: "unlanded", reason: "Adapter refused without a reason — not a decision an operator can act on." };
    }
    if (answer.ok !== true) return { state: "unlanded", reason: "Adapter answer carried no ok flag, so the response was never validated." };
    return { state: "landed", receiptId: answer.id || null, duplicateOf: answer.duplicateOf || null };
  }

  function submit(decl) {
    decl = decl || {};
    var key = decl.idempotencyKey || (String(decl.action || "instruction") + ":" + stamp());
    var existing = null;
    list.forEach(function (i) {
      if (i.idempotencyKey === key && (i.state === "inflight" || i.state === "landed")) existing = i;
    });
    // The key is the identity — across screens, not just across clicks.
    if (existing) return Object.assign({}, existing, { deduped: true });
    var rec = {
      id: "INS-" + (++seq) + "-" + Math.random().toString(36).slice(2, 6).toUpperCase(),
      action: decl.action || "instruction", label: decl.label || decl.action || "Instruction",
      endpoint: decl.endpoint || null, idempotencyKey: key, keyDeclared: !!decl.idempotencyKey,
      origin: decl.origin || null, originHref: decl.originHref || (global.location && global.location.pathname.split("/").pop()) || null,
      payload: decl.payload || null, state: "inflight", reason: null,
      receipt: { attempt: 1, at: stamp() }, sentAt: stamp(), restored: false,
    };
    list = [rec].concat(list).slice(0, MAX);
    write(); emit();
    return rec;
  }

  function settle(instructionId, answer) {
    var out = readAnswer(answer);
    list = list.map(function (i) {
      if (i.id !== instructionId) return i;
      return Object.assign({}, i, {
        state: out.state,
        reason: out.state === "landed" ? null : out.reason || null,
        receipt: {
          id: out.receiptId || null, at: stamp(), attempt: (i.receipt && i.receipt.attempt) || 1,
          duplicateOf: out.duplicateOf || undefined, unreceipted: out.state === "landed" && !out.receiptId ? true : undefined,
        },
      });
    });
    write(); emit();
    return byId(instructionId);
  }

  function retry(instructionId) {
    list = list.map(function (i) {
      if (i.id !== instructionId) return i;
      return Object.assign({}, i, {
        state: "inflight", reason: null, restored: false,
        receipt: { attempt: ((i.receipt && i.receipt.attempt) || 1) + 1, at: stamp() },
      });
    });
    write(); emit();
    return byId(instructionId);
  }

  function byId(instructionId) {
    var found = null;
    list.forEach(function (i) { if (i.id === instructionId) found = i; });
    return found;
  }

  /** inflight · unlanded · landed-without-a-receipt. What the tray must keep on screen. */
  function unresolved() {
    return list.filter(function (i) {
      return i.state === "inflight" || i.state === "unlanded" || (i.state === "landed" && !(i.receipt && i.receipt.id));
    });
  }

  /**
   * Durable record reconciliation. For every unresolved instruction, ask the adapter what it knows
   * about that idempotency key. `probe(instruction)` resolves the same shape a transport does — an
   * answer it cannot read leaves the instruction unresolved. Nothing here assumes a landing.
   */
  function reconcile(probe) {
    if (typeof probe !== "function") return Promise.resolve([]);
    var open = unresolved();
    return Promise.all(open.map(function (i) {
      return Promise.resolve()
        .then(function () { return probe(i); })
        .then(function (answer) { return { id: i.id, settled: settle(i.id, answer) }; })
        .catch(function (e) { return { id: i.id, settled: null, error: String((e && e.message) || e) }; });
    }));
  }

  function clear(instructionId) {
    list = list.filter(function (i) { return i.id !== instructionId; });
    write(); emit();
  }
  /** Forget settled-and-seen instructions. Never touches anything unresolved. */
  function forgetSettled() {
    list = list.filter(function (i) { return unresolved().indexOf(i) >= 0 || i.state === "rejected"; });
    write(); emit();
  }

  global.MeridianInstructions = {
    durable: { kind: kind, note: note, key: KEY },
    list: function () { return list.slice(); },
    unresolved: unresolved,
    byId: byId,
    submit: submit, settle: settle, retry: retry, reconcile: reconcile,
    clear: clear, forgetSettled: forgetSettled, readAnswer: readAnswer,
    subscribe: function (fn) { subs.push(fn); return function () { subs = subs.filter(function (f) { return f !== fn; }); }; },
  };
})(typeof window !== "undefined" ? window : this);
