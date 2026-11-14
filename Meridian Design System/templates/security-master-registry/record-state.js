// Shared per-record state between the registry and the detail page (fieldEdits, auditExtra, notesById, overrides, rejected).
window.SMR_STATE = (() => {
  const KEY = "smr.recordState.v1";
  const EMPTY = { fieldEdits: {}, auditExtra: {}, notesById: {}, overrides: {}, rejected: {} };
  const read = () => { try { return { ...EMPTY, ...(JSON.parse(localStorage.getItem(KEY) || "null") || {}) }; } catch (e) { return { ...EMPTY }; } };
  const write = (patch) => { const next = { ...read() }; for (const k of Object.keys(EMPTY)) if (patch[k]) next[k] = patch[k]; localStorage.setItem(KEY, JSON.stringify(next)); };
  const subscribe = (fn) => { const h = (e) => { if (e.key === KEY) fn(read()); }; window.addEventListener("storage", h); return () => window.removeEventListener("storage", h); };
  return { KEY, EMPTY, read, write, subscribe };
})();
