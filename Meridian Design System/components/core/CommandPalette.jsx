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
.mds-cmd__scrim{position:fixed;inset:0;z-index:1000;display:flex;justify-content:center;
  align-items:flex-start;padding:14vh 16px 16px;background:var(--scrim, rgba(14,17,19,.5));}
.mds-cmd{width:100%;max-width:580px;display:flex;flex-direction:column;max-height:60vh;
  background:var(--bg-light,#FFFFFF);border:1px solid var(--border-strong,#AAB4BF);
  border-radius:var(--radius-card,2px);box-shadow:var(--shadow-menu,0 2px 6px rgba(0,0,0,.18));
  font-family:var(--font-body);overflow:hidden;}
.mds-cmd__field{display:flex;align-items:center;gap:10px;height:46px;padding:0 12px;flex:none;
  border-bottom:1px solid var(--border,#D7DCE2);background:var(--bg-medium,#F5F7FA);}
.mds-cmd__field:focus-within{outline:none;}
.mds-cmd__glyph{font-size:15px;color:var(--text-muted,#59636F);line-height:1;}
.mds-cmd__input{flex:1;min-width:0;height:100%;border:none;background:transparent;outline:none;
  color:var(--text-primary,#22272E);font-family:var(--font-data,monospace);font-size:14px;}
.mds-cmd__input::placeholder{color:var(--text-disabled,#889099);}
.mds-cmd__esc{flex:none;font-family:var(--font-data,monospace);font-size:10px;font-weight:600;
  color:var(--text-muted,#59636F);border:1px solid var(--border,#D7DCE2);
  background:var(--bg-light,#FFFFFF);padding:2px 6px;border-radius:var(--radius-button,2px);}
.mds-cmd__list{flex:1;min-height:0;overflow-y:auto;padding:4px 0;}
.mds-cmd__group{padding:8px 14px 3px;font-family:var(--font-body);font-size:9px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.08em;color:var(--text-muted,#59636F);}
.mds-cmd__row{display:flex;align-items:center;gap:10px;padding:7px 14px 7px 11px;cursor:pointer;
  border-left:3px solid transparent;}
.mds-cmd__row--hl{border-left-color:var(--accent,#2F6F8F);background:var(--bg-hover,#F1F4F7);}
.mds-cmd__row--danger.mds-cmd__row--hl{border-left-color:var(--red,#BA3F55);}
.mds-cmd__ico{flex:none;width:16px;height:16px;display:inline-flex;align-items:center;
  justify-content:center;color:var(--text-muted,#59636F);}
.mds-cmd__row--hl .mds-cmd__ico{color:var(--accent,#2F6F8F);}
.mds-cmd__row--danger .mds-cmd__ico{color:var(--red,#BA3F55);}
.mds-cmd__label{flex:1;min-width:0;font-size:13px;color:var(--text-primary,#22272E);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-cmd__row--danger .mds-cmd__label{color:var(--red-dim,#7d3940);}
.mds-cmd__hint{flex:none;font-family:var(--font-data,monospace);font-size:11px;
  color:var(--text-muted,#59636F);}
.mds-cmd__sc{flex:none;display:inline-flex;gap:3px;}
.mds-cmd__key{font-family:var(--font-data,monospace);font-size:10px;font-weight:600;
  color:var(--text-secondary,#4D5967);border:1px solid var(--border,#D7DCE2);
  background:var(--bg-light,#FFFFFF);padding:1px 5px;border-radius:var(--radius-button,2px);}
.mds-cmd__mark{color:var(--accent,#2F6F8F);font-weight:700;}
.mds-cmd__empty{padding:22px 14px;text-align:center;font-size:12px;color:var(--text-muted,#59636F);}
.mds-cmd__foot{flex:none;display:flex;gap:16px;padding:7px 14px;border-top:1px solid var(--border,#D7DCE2);
  background:var(--bg-medium,#F5F7FA);font-family:var(--font-data,monospace);font-size:10px;
  color:var(--text-muted,#59636F);}
.mds-cmd__foot kbd{font-family:inherit;font-weight:600;color:var(--text-secondary,#4D5967);}
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
}) {
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

  // filter + group, preserving a flat index for keyboard nav
  const { groups, flat } = React.useMemo(() => {
    const q = query.trim().toLowerCase();
    const match = (c) => {
      if (!q) return true;
      const hay = [c.label, c.group, c.hint, ...(c.keywords || [])].filter(Boolean).join(" ").toLowerCase();
      return hay.includes(q);
    };
    const filtered = commands.filter(match);
    const order = groupOrder || [...new Set(filtered.map((c) => c.group || "Commands"))];
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
  }, [commands, query, groupOrder]);

  React.useEffect(() => { setHl(0); }, [query]);
  React.useEffect(() => {
    const row = listRef.current && listRef.current.querySelector(`[data-i="${hl}"]`);
    if (row) row.scrollIntoView({ block: "nearest" });
  }, [hl]);

  if (!open) return null;

  const run = (c) => {
    if (!c) return;
    close();
    c.run && c.run();
  };

  const onKey = (e) => {
    if (e.key === "ArrowDown") { e.preventDefault(); setHl((h) => Math.min(h + 1, flat.length - 1)); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setHl((h) => Math.max(h - 1, 0)); }
    else if (e.key === "Enter") { e.preventDefault(); run(flat[hl]); }
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
            aria-controls="mds-cmd-list"
            aria-activedescendant={flat[hl] ? `mds-cmd-opt-${hl}` : undefined}
            placeholder={placeholder}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={onKey}
          />
          <span className="mds-cmd__esc">Esc</span>
        </div>
        <div className="mds-cmd__list" id="mds-cmd-list" role="listbox" ref={listRef}>
          {flat.length === 0 && <div className="mds-cmd__empty">{emptyText}</div>}
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
                    <span className="mds-cmd__label">{highlight(c.label, query.trim())}</span>
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
            <span><kbd>↵</kbd> open</span>
            <span><kbd>{isMac() ? "⌘K" : "Ctrl K"}</kbd> toggle</span>
          </div>
        )}
      </div>
    </div>
  );
}

CommandPalette.displayName = "CommandPalette";
