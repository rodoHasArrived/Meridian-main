// Meridian mock transport — the lying-but-honest network for the action contract.
//
// It exists so the four fates in `ActionEnvelope` are EXERCISED rather than asserted: latency,
// duplicate submits, a business rejection with a reason, a 500 whose answer is lost, and silence.
// It lives in tests/ on purpose. The design system ships the contract, not a transport; every
// template keeps its fixture.
//
// Answer mapping (this is the contract, in one table):
//   resolve { ok: true, id }      → landed
//   resolve { ok: false, reason } → rejected   (a decision was made and it was no)
//   throw / never resolve         → unlanded   (no decision reached the operator)
//
// Modes: "ok" · "reject" (4xx with a reason) · "error" (500 before the write) · "lost" (the write
// HAPPENED, the answer did not — the case that makes idempotency non-optional) · "silent" (no
// answer at all, so the envelope's timeout decides) · a function (instruction, attempt) => mode.
(function (root) {
  function createMockTransport(opts) {
    const cfg = Object.assign({
      latencyMs: 60,
      jitterMs: 0,
      mode: "ok",
      reason: "Order exceeds the desk limit · 5,000,000 USD",
      errorMessage: "HTTP 500 · order gateway",
      silentMs: 60000,
      idPrefix: "MOCK-",
    }, opts || {});
    const store = new Map(); // server-side idempotency: key → { id, at }
    const log = [];
    let seq = 0, attempts = 0;
    const wait = (ms) => new Promise((r) => setTimeout(r, ms));

    async function transport(instruction) {
      attempts += 1;
      const mode = typeof cfg.mode === "function" ? cfg.mode(instruction, attempts) : cfg.mode;
      const latency = cfg.latencyMs + (cfg.jitterMs ? Math.round(Math.random() * cfg.jitterMs) : 0);
      log.push({ at: Date.now(), key: instruction.idempotencyKey, action: instruction.action, attempt: instruction.attempt, mode });

      // The same key never creates a second record — a replay returns the first receipt. This is
      // why a retry after "unlanded" is safe and a fresh key is not.
      const seen = store.get(instruction.idempotencyKey);
      if (seen) { await wait(latency); return { ok: true, id: seen.id, duplicateOf: seen.id, replay: true }; }

      if (mode === "silent") { await wait(cfg.silentMs); return { ok: true, id: cfg.idPrefix + (++seq) }; }
      await wait(latency);
      if (mode === "reject") return { ok: false, reason: cfg.reason };
      if (mode === "error") throw new Error(cfg.errorMessage);
      if (mode === "lost") {
        const id = cfg.idPrefix + (++seq);
        store.set(instruction.idempotencyKey, { id, at: Date.now() });
        throw new Error("HTTP 502 · answer lost after the write");
      }
      const id = cfg.idPrefix + (++seq);
      store.set(instruction.idempotencyKey, { id, at: Date.now() });
      return { ok: true, id };
    }

    transport.store = store;
    transport.log = log;
    transport.calls = () => log.length;
    transport.landed = () => [...store.values()].map((v) => v.id);
    transport.setMode = (m) => { cfg.mode = m; return transport; };
    transport.setLatency = (ms) => { cfg.latencyMs = ms; return transport; };
    transport.reset = () => { store.clear(); log.length = 0; seq = 0; attempts = 0; return transport; };
    return transport;
  }

  root.MeridianMockTransport = { createMockTransport };
  if (typeof module !== "undefined" && module.exports) module.exports = { createMockTransport };
})(typeof window !== "undefined" ? window : globalThis);
