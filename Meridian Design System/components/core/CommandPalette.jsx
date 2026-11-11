// Meridian CommandPalette — the Ctrl-K operator command surface promised by the
// WorkstationTopbar. Grouped, keyboard-first results with a 4px left-inset selection
// (no full-row background swap), mono shortcuts, section headers, ≤6 rows then scroll.
// Functional: hard corners, hairline structure, one accent, the only shadow is the float.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-cmd__scrim{position:fixed;inset:0;z-index:var(--z-modal,200);display:flex;justify-content:center;
  align-items:flex-start;padding:14vh 16px 16px;background:var(--scrim, rgba(30,28,26,.5));}
.mds-cmd{width:100%;max-width:580px;display:flex;flex-direction:column;max-height:60vh;
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border-strong,#AAA49A);
  border-radius:var(--radius-card,2px);box-shadow:var(--shadow-menu,0 2px 6px rgba(0,0,0,.18));
  font-family:var(--font-body);overflow:hidden;}
.mds-cmd__field{display:flex;align-items:center;gap:10px;height:46px;padding:0 12px;flex:none;
  border-bottom:1px solid var(--border,#D2CDC3);background:var(--bg-medium,#EBE8E1);}
.mds-cmd__field:focus-within{outline:none;}
.mds-cmd__glyph{font-size:15px;color:var(--text-muted,#6B737B);line-height:1;}
.mds-cmd__input{flex:1;min-width:0;height:100%;border:none;background:transparent;outline:none;
  color:var(--text-primary,#20252B);font-family:var(--font-data,monospace);font-size:14px;}
.mds-cmd__input::placeholder{color:var(--text-disabled,#A4ABB2);}
.mds-cmd__esc{flex:none;font-family:var(--font-data,monospace);font-size:10px;font-weight:600;
  color:var(--text-muted,#6B737B);border:1px solid var(--border,#D2CDC3);
  background:var(--bg-light,#F8F7F3);padding:2px 6px;border-radius:var(--radius-button,2px);}
.mds-cmd__list{flex:1;min-height:0;overflow-y:auto;padding:4px 0;}
.mds-cmd__group{padding:8px 14px 3px;font-family:var(--font-body);font-size:9px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.08em;color:var(--text-muted,#6B737B);}
.mds-cmd__row{display:flex;align-items:center;gap:10px;padding:7px 14px 7px 11px;cursor:pointer;
  border-left:3px solid transparent;}
.mds-cmd__row--hl{border-left-color:var(--brand-signal,#C56343);background:var(--bg-hover,#EDEFF0);}
.mds-cmd__row--danger.mds-cmd__row--hl{border-left-color:var(--red,#B54B42);}
.mds-cmd__ico{flex:none;width:16px;height:16px;display:inline-flex;align-items:center;
  justify-content:center;color:var(--text-muted,#6B737B);}
.mds-cmd__row--hl .mds-cmd__ico{color:var(--brand-signal,#C56343);}
.mds-cmd__row--danger .mds-cmd__ico{color:var(--red,#B54B42);}
.mds-cmd__label{flex:1;min-width:0;font-size:13px;color:var(--text-primary,#20252B);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-cmd__row--danger .mds-cmd__label{color:var(--red-dim,#7d3940);}
.mds-cmd__hint{flex:none;font-family:var(--font-data,monospace);font-size:11px;
  color:var(--text-muted,#6B737B);}
.mds-cmd__sc{flex:none;display:inline-flex;gap:3px;}
.mds-cmd__key{font-family:var(--font-data,monospace);font-size:10px;font-weight:600;
  color:var(--text-secondary,#535C65);border:1px solid var(--border,#D2CDC3);
  background:var(--bg-light,#F8F7F3);padding:1px 5px;border-radius:var(--radius-button,2px);}
.mds-cmd__mark{color:var(--brand-signal,#C56343);font-weight:700;}
.mds-cmd__meta{margin-left:8px;font-family:var(--font-data,monospace);font-size:10.5px;color:var(--text-muted,#6B737B);}
.mds-cmd__verbs{flex:none;display:none;gap:4px;}
.mds-cmd__row--hl .mds-cmd__verbs{display:inline-flex;}
.mds-cmd__verb{appearance:none;cursor:pointer;font:600 10px var(--font-body);padding:2px 7px;border:1px solid var(--border,#D2CDC3);
  background:var(--bg-light,#F8F7F3);color:var(--text-secondary,#535C65);border-radius:var(--radius-button,2px);}
.mds-cmd__verb[data-on="true"]{border-color:var(--brand-signal,#C56343);color:var(--text-primary,#20252B);background:var(--bg,#F2F0EB);}
.mds-cmd__empty{padding:22px 14px;text-align:center;font-size:12px;color:var(--text-muted,#6B737B);}
.mds-cmd__foot{flex:none;display:flex;gap:16px;padding:7px 14px;border-top:1px solid var(--border,#D2CDC3);
  background:var(--bg-medium,#EBE8E1);font-family:var(--font-data,monospace);font-size:10px;
  color:var(--text-muted,#6B737B);}
.mds-cmd__foot kbd{font-family:inherit;font-weight:600;color:var(--text-secondary,#535C65);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "command-palette");
  el.textContent = css;
  document.head.appendChild(el);
}

function isMac() {
  return typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent || "");
}

// Hook: wires the global Ctrl/Cmd+K toggle and exposes open/close/setOpen.
export function useCommandPalette(initial = false) {
  const [open, setOpen] = React.useState(initial);
  React.useEffect(() => {
    const onKey = (e) => {
      const k = (e.key || "").toLowerCase();
      if (k === "k" && (e.metaKey || e.ctrlKey)) {
        e.preventDefault();
        setOpen((o) => !o);
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, []);
  return {
    open,
    setOpen,
    openPalette: React.useCallback(() => setOpen(true), []),
    closePalette: React.useCallback(() => setOpen(false), []),
  };
}

function highlight(label, q) {
  if (!q) return label;
  const i = label.toLowerCase().indexOf(q.toLowerCase());
  if (i === -1) return label;
  return (
    <>
      {label.slice(0, i)}
      <span className="mds-cmd__mark">{label.slice(i, i + q.length)}</span>
      {label.slice(i + q.length)}
    </>
  );
}

function renderShortcut(sc) {
  const parts = Array.isArray(sc) ? sc : String(sc).split("+");
  return (
    <span className="mds-cmd__sc">
      {parts.map((p, i) => <kbd key={i} className="mds-cmd__key">{p}</kbd>)}
    </span>
  );
}

// D-2 · recency ranking. Session-scoped: what an operator ran this session floats up; nothing
// about their behaviour leaves the tab.
const RECENT_KEY = "mds.command-palette.recent";
function readRecent(scope) {
  try { return JSON.parse(sessionStorage.getItem(RECENT_KEY + "." + scope) || "{}"); } catch { return {}; }
}
function bumpRecent(scope, id) {
  try {
    const r = readRecent(scope); r[id] = { n: (r[id]?.n || 0) + 1, at: Date.now() };
    sessionStorage.setItem(RECENT_KEY + "." + scope, JSON.stringify(r));
  } catch { /* ranking is never load-bearing */ }
}
function score(c, q, recent) {
  const label = (c.label || "").toLowerCase();
  let s = 0;
  if (q) {
    if (label === q) s += 100; else if (label.startsWith(q)) s += 60; else if (label.includes(q)) s += 30;
    else if ((c.keywords || []).some((k) => String(k).toLowerCase().startsWith(q))) s += 20; else s += 5;
  }
  const r = recent[c.id];
  if (r) s += Math.min(r.n, 5) * 4 + Math.max(0, 10 - (Date.now() - r.at) / 60000);
  return s;
}

function parseHotkey(hotkey) {
  if (!hotkey) return null;
  const parts = String(hotkey).toLowerCase().split("+");
  return { mod: parts.includes("mod") || parts.includes("ctrl") || parts.includes("cmd"), key: parts[parts.length - 1] };
}

export function CommandPalette({
  open: controlledOpen,
  defaultOpen = false,
  onOpenChange,
  onClose,
  hotkey = "mod+k",
  commands = [],
  placeholder = "Search symbols, runs, commands…",
  emptyText = "No matches",
  groupOrder = null,
  footer = true,
  resolve,
  resolveMinLength = 2,
  resolveDebounceMs = 120,
  resolveGroup = "Objects",
  scope = "global",
  rankByRecency = true,
}) {
  const uid = React.useId();
  inject();
  const isControlled = controlledOpen != null;
  const [internalOpen, setInternalOpen] = React.useState(defaultOpen);
  const open = isControlled ? controlledOpen : internalOpen;
  const setOpen = React.useCallback((v) => {
    if (!isControlled) setInternalOpen(v);
    onOpenChange && onOpenChange(v);
    if (!v) onClose && onClose();
  }, [isControlled, onOpenChange, onClose]);
  const close = React.useCallback(() => setOpen(false), [setOpen]);

  const [query, setQuery] = React.useState("");
  const [hl, setHl] = React.useState(0);
  const inputRef = React.useRef(null);
  const listRef = React.useRef(null);
  const restoreRef = React.useRef(null);
  const [resolved, setResolved] = React.useState({ q: "", items: [], pending: false });
  const [verb, setVerb] = React.useState(0);

  // Built-in global hotkey toggle — no external hook required.
  React.useEffect(() => {
    const hk = parseHotkey(hotkey);
    if (!hk) return;
    const onKey = (e) => {
      const k = (e.key || "").toLowerCase();
      if (k === hk.key && (!hk.mod || e.metaKey || e.ctrlKey)) {
        e.preventDefault();
        setOpen(!open);
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [hotkey, open, setOpen]);

  React.useEffect(() => {
    if (open) {
      restoreRef.current = document.activeElement;
      setQuery("");
      setHl(0);
      const t = setTimeout(() => inputRef.current && inputRef.current.focus(), 0);
      return () => clearTimeout(t);
    } else if (restoreRef.current && restoreRef.current.focus) {
      restoreRef.current.focus();
    }
  }, [open]);

  // D-2 · async object resolution: type a symbol, a case id, a report name — the screen answers.
  React.useEffect(() => {
    if (!open || !resolve) return undefined;
    const q = query.trim();
    if (q.length < resolveMinLength) { setResolved({ q, items: [], pending: false }); return undefined; }
    let alive = true;
    setResolved((r) => ({ ...r, pending: true }));
    const t = setTimeout(async () => {
      try {
        const items = await Promise.resolve(resolve(q));
        if (alive) setResolved({ q, items: (items || []).map((c) => ({ ...c, group: c.group || resolveGroup })), pending: false });
      } catch { if (alive) setResolved({ q, items: [], pending: false }); }
    }, resolveDebounceMs);
    return () => { alive = false; clearTimeout(t); };
  }, [open, query, resolve, resolveMinLength, resolveDebounceMs, resolveGroup]);

  // filter + rank + group, preserving a flat index for keyboard nav
  const { groups, flat } = React.useMemo(() => {
    const q = query.trim().toLowerCase();
    const match = (c) => {
      if (!q) return true;
      const hay = [c.label, c.group, c.hint, ...(c.keywords || [])].filter(Boolean).join(" ").toLowerCase();
      return hay.includes(q);
    };
    const recent = rankByRecency ? readRecent(scope) : {};
    const pool = resolved.q === query.trim() ? [...commands, ...resolved.items] : commands;
    let filtered = pool.filter((c) => c.group === resolveGroup && resolved.items.includes(c) ? true : match(c));
    if (rankByRecency || q) filtered = filtered.map((c, i) => [c, score(c, q, recent), i]).sort((a, b) => b[1] - a[1] || a[2] - b[2]).map((x) => x[0]);
    const seen = new Set(filtered.map((c) => c.group || "Commands"));
    const order = groupOrder ? [...groupOrder.filter((g) => seen.has(g)), ...[...seen].filter((g) => !groupOrder.includes(g))]
      : (!q && rankByRecency && Object.keys(recent).length ? ["Recent", ...[...seen].filter((g) => g !== "Recent")] : [...seen]);
    if (!q && rankByRecency) {
      const rec = filtered.filter((c) => recent[c.id]).slice(0, 4);
      if (rec.length) { filtered = [...rec.map((c) => ({ ...c, group: "Recent", _recent: true })), ...filtered]; if (!order.includes("Recent")) order.unshift("Recent"); }
    }
    const byGroup = new Map();
    for (const c of filtered) {
      const g = c.group || "Commands";
      if (!byGroup.has(g)) byGroup.set(g, []);
      byGroup.get(g).push(c);
    }
    const groups = [];
    const flat = [];
    for (const g of order) {
      const items = byGroup.get(g);
      if (!items || !items.length) continue;
      groups.push({ group: g, items });
      for (const it of items) flat.push(it);
    }
    return { groups, flat };
  }, [commands, query, groupOrder, resolved, scope, rankByRecency, resolveGroup]);

  React.useEffect(() => { setHl(0); setVerb(0); }, [query]);
  React.useEffect(() => { setVerb(0); }, [hl]);
  React.useEffect(() => {
    const row = listRef.current && listRef.current.querySelector(`[data-i="${hl}"]`);
    if (row) row.scrollIntoView({ block: "nearest" });
  }, [hl]);

  if (!open) return null;

  // A row may carry verbs (open · pin · attest). Enter runs the highlighted verb, or `run` when none.
  const run = (c, v) => {
    if (!c) return;
    if (rankByRecency && c.id) bumpRecent(scope, c.id);
    close();
    const verbs = c.verbs || c.actions || [];
    const chosen = verbs.length ? verbs[Math.min(v ?? 0, verbs.length - 1)] : null;
    if (chosen && chosen.run) chosen.run(c); else c.run && c.run();
  };

  const onKey = (e) => {
    const cur = flat[hl]; const nv = (cur && (cur.verbs || cur.actions) || []).length;
    if (e.key === "ArrowDown") { e.preventDefault(); setHl((h) => Math.min(h + 1, flat.length - 1)); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setHl((h) => Math.max(h - 1, 0)); }
    else if (e.key === "ArrowRight" && nv > 1 && e.target.selectionStart === e.target.value.length) { e.preventDefault(); setVerb((v) => (v + 1) % nv); }
    else if (e.key === "ArrowLeft" && nv > 1 && verb > 0) { e.preventDefault(); setVerb((v) => (v - 1 + nv) % nv); }
    else if (e.key === "Tab" && nv > 1) { e.preventDefault(); setVerb((v) => (v + (e.shiftKey ? nv - 1 : 1)) % nv); }
    else if (e.key === "Enter") { e.preventDefault(); run(cur, verb); }
    else if (e.key === "Escape") { e.preventDefault(); close(); }
    else if (e.key === "Tab") { e.preventDefault(); } // trap: the input is the palette's only tab stop
  };

  let i = -1;
  return (
    <div className="mds-cmd__scrim" onMouseDown={(e) => { if (e.target === e.currentTarget) close(); }}>
      <div className="mds-cmd" role="dialog" aria-modal="true" aria-label="Command palette">
        <div className="mds-cmd__field">
          <span className="mds-cmd__glyph" aria-hidden="true">⌕</span>
          <input
            ref={inputRef}
            className="mds-cmd__input"
            role="combobox"
            aria-expanded="true"
            aria-controls={`${uid}-list`}
            aria-activedescendant={flat[hl] ? `mds-cmd-opt-${hl}` : undefined}
            placeholder={placeholder}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={onKey}
          />
          <span className="mds-cmd__esc">Esc</span>
        </div>
        <div className="mds-cmd__list" id={`${uid}-list`} role="listbox" ref={listRef}>
          {flat.length === 0 && <div className="mds-cmd__empty">{resolved.pending ? "Resolving…" : emptyText}</div>}
          {groups.map((g) => (
            <div key={g.group} role="group" aria-label={g.group}>
              <div className="mds-cmd__group">{g.group}</div>
              {g.items.map((c) => {
                i += 1;
                const idx = i;
                return (
                  <div
                    key={c.id}
                    id={`mds-cmd-opt-${idx}`}
                    data-i={idx}
                    role="option"
                    aria-selected={idx === hl}
                    className={`mds-cmd__row${idx === hl ? " mds-cmd__row--hl" : ""}${c.danger ? " mds-cmd__row--danger" : ""}`}
                    onMouseEnter={() => setHl(idx)}
                    onMouseDown={(e) => { e.preventDefault(); run(c); }}
                  >
                    {c.icon !== undefined && <span className="mds-cmd__ico" aria-hidden="true">{c.icon}</span>}
                    <span className="mds-cmd__label">{highlight(c.label, query.trim())}{c.meta ? <span className="mds-cmd__meta">{c.meta}</span> : null}</span>
                    {(c.verbs || c.actions) && (c.verbs || c.actions).length ? <span className="mds-cmd__verbs" role="group" aria-label="Actions">
                      {(c.verbs || c.actions).map((v, vi) => <button key={v.id || v.label} type="button" tabIndex={-1} className="mds-cmd__verb" data-on={idx === hl && vi === verb}
                        onMouseDown={(e) => { e.preventDefault(); e.stopPropagation(); run(c, vi); }} onMouseEnter={() => { setHl(idx); setVerb(vi); }}>{v.label}</button>)}
                    </span> : null}
                    {c.hint && !c.shortcut && <span className="mds-cmd__hint">{c.hint}</span>}
                    {c.shortcut && renderShortcut(c.shortcut)}
                  </div>
                );
              })}
            </div>
          ))}
        </div>
        {footer && (
          <div className="mds-cmd__foot">
            <span><kbd>↑↓</kbd> navigate</span>
            <span><kbd>↵</kbd> run</span>
            {(flat[hl]?.verbs || flat[hl]?.actions || []).length > 1 ? <span><kbd>⇥</kbd> verb</span> : null}
            <span><kbd>{isMac() ? "⌘K" : "Ctrl K"}</kbd> toggle</span>
          </div>
        )}
      </div>
    </div>
  );
}

CommandPalette.displayName = "CommandPalette";
