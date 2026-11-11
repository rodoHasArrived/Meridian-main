// Meridian GlobalSearch — the masthead search field, made real (docs/SUITE_COHESION_4_4.md › B-1).
// The topbar has always rendered a full-width button reading "Search symbols, runs, commands…" and
// on every screen in the suite it did nothing. This is what it opens: a typeahead over the current
// screen's objects plus every destination in the app map, grouped, keyboard-first.
//
// Mounted automatically by templates/_scaffold/ds-base.js — screens write nothing. Mount it by hand
// only when you want it somewhere other than the masthead.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-gsearch__scrim{position:fixed;inset:0;z-index:2147481000;background:var(--scrim,rgba(17,23,29,.32));
  display:flex;justify-content:center;align-items:flex-start;padding-top:56px;}
.mds-gsearch{width:min(680px,calc(100vw - 32px));max-height:min(60vh,520px);display:flex;flex-direction:column;
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border-strong,#AAA49A);
  box-shadow:var(--shadow-menu,0 12px 32px rgba(17,23,29,.24));font-family:var(--font-body);}
.mds-gsearch__field{display:flex;align-items:center;gap:9px;padding:0 12px;height:44px;flex:none;
  border-bottom:1px solid var(--border,#D2CDC3);}
.mds-gsearch__field:focus-within{outline:var(--focus-ring,2px solid var(--interaction-focus,#2D6FB3));outline-offset:-2px;}
.mds-gsearch__glyph{color:var(--text-muted,#606870);font-size:14px;line-height:1;}
.mds-gsearch__input{flex:1;min-width:0;appearance:none;border:none;outline:none;background:transparent;
  font-family:var(--font-body);font-size:14px;color:var(--text-primary,#20252B);}
.mds-gsearch__input::placeholder{color:var(--text-muted,#606870);}
.mds-gsearch__scope{font-family:var(--font-data);font-size:9.5px;font-weight:600;letter-spacing:.04em;
  text-transform:uppercase;color:var(--text-muted,#606870);border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-chip,2px);padding:2px 6px;white-space:nowrap;flex:none;}
.mds-gsearch__list{overflow:auto;padding:4px 0;}
.mds-gsearch__group{padding:8px 12px 3px;font-family:var(--font-data);font-size:9.5px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-muted,#606870);}
.mds-gsearch__row{display:grid;grid-template-columns:1fr max-content;gap:10px;align-items:center;
  width:100%;text-align:left;appearance:none;border:none;background:transparent;cursor:pointer;
  padding:6px 12px;font-family:var(--font-body);font-size:13px;color:var(--text-secondary,#535C65);
  border-left:2px solid transparent;}
.mds-gsearch__row:hover{background:var(--bg-hover,#EDEFF0);}
.mds-gsearch__row[data-active="true"]{background:var(--sidebar-active,#E7EDF2);
  border-left-color:var(--sidebar-active-ind,#C56343);color:var(--text-primary,#20252B);}
.mds-gsearch__label{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-gsearch__label mark{background:var(--brand-signal-ghost,#F6E6DF);color:var(--brand-signal-ink,#944A32);
  font-weight:600;padding:0 1px;}
.mds-gsearch__meta{font-family:var(--font-data);font-size:10.5px;color:var(--text-muted,#606870);
  white-space:nowrap;font-variant-numeric:tabular-nums;}
.mds-gsearch__empty{padding:22px 14px;text-align:center;font-size:12.5px;color:var(--text-muted,#606870);
  line-height:1.6;}
.mds-gsearch__empty b{display:block;color:var(--text-secondary,#535C65);font-weight:600;margin-bottom:3px;}
.mds-gsearch__foot{flex:none;display:flex;align-items:center;gap:12px;padding:6px 12px;
  border-top:1px solid var(--border,#D2CDC3);background:var(--bg-medium,#EBE8E1);
  font-family:var(--font-data);font-size:10px;color:var(--text-muted,#606870);}
.mds-gsearch__foot kbd{font-family:var(--font-data);font-size:9.5px;border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-chip,2px);padding:1px 4px;background:var(--bg-light,#F8F7F3);margin-right:4px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "globalsearch");
  el.textContent = css;
  document.head.appendChild(el);
}

/** Highlight the matched span without dangerouslySetInnerHTML. */
function mark(text, q) {
  const s = String(text ?? "");
  if (!q) return s;
  const i = s.toLowerCase().indexOf(q.toLowerCase());
  if (i < 0) return s;
  return [s.slice(0, i), React.createElement("mark", { key: "m" }, s.slice(i, i + q.length)), s.slice(i + q.length)];
}

/**
 * `resolve(query)` returns the CURRENT SCREEN's objects as
 * [{ id, group, label, meta?, run() }] — the same row shape the CommandPalette takes, so a screen
 * that already declares `objects` in its operator layer is searchable with no extra work.
 * `destinations` are app-map screens: [{ id, label, group, meta?, run() }].
 */
export function GlobalSearch({
  open = false, onClose, resolve, destinations = [], scopeLabel = "This screen",
  placeholder = "Search this screen, or go to…", emptyHint,
}) {
  inject();
  const [q, setQ] = React.useState("");
  const [active, setActive] = React.useState(0);
  const inputRef = React.useRef(null);
  const listRef = React.useRef(null);

  React.useEffect(() => { if (open) { setQ(""); setActive(0); setTimeout(() => inputRef.current && inputRef.current.focus(), 0); } }, [open]);

  const rows = React.useMemo(() => {
    const out = [];
    const term = q.trim();
    if (term && typeof resolve === "function") {
      let found = [];
      try { found = resolve(term) || []; } catch (e) { found = []; }
      for (const r of found) out.push({ ...r, group: r.group || "Objects" });
    }
    const dests = term
      ? destinations.filter((d) => (d.label + " " + (d.meta || "")).toLowerCase().includes(term.toLowerCase()))
      : destinations;
    for (const d of dests.slice(0, term ? 8 : 40)) out.push({ ...d, group: "Go to" });
    return out;
  }, [q, resolve, destinations]);

  React.useEffect(() => { setActive(0); }, [q]);

  const run = React.useCallback((row) => {
    if (!row) return;
    onClose && onClose();
    setTimeout(() => { try { row.run && row.run(); } catch (e) { console.error("GlobalSearch: row action failed", e); } }, 0);
  }, [onClose]);

  const onKeyDown = (e) => {
    if (e.key === "Escape") { e.preventDefault(); onClose && onClose(); return; }
    if (e.key === "ArrowDown" || (e.key === "n" && e.ctrlKey)) { e.preventDefault(); setActive((i) => Math.min(rows.length - 1, i + 1)); return; }
    if (e.key === "ArrowUp" || (e.key === "p" && e.ctrlKey)) { e.preventDefault(); setActive((i) => Math.max(0, i - 1)); return; }
    if (e.key === "Enter") { e.preventDefault(); run(rows[active]); }
  };

  React.useEffect(() => {
    const node = listRef.current && listRef.current.querySelector('[data-active="true"]');
    if (node && node.parentNode) {
      const box = listRef.current.getBoundingClientRect(), r = node.getBoundingClientRect();
      if (r.bottom > box.bottom) listRef.current.scrollTop += r.bottom - box.bottom;
      else if (r.top < box.top) listRef.current.scrollTop -= box.top - r.top;
    }
  }, [active]);

  if (!open) return null;

  let lastGroup = null;
  const body = [];
  rows.forEach((r, i) => {
    if (r.group !== lastGroup) { body.push(React.createElement("div", { className: "mds-gsearch__group", key: "g" + i }, r.group)); lastGroup = r.group; }
    body.push(React.createElement("button", {
      key: r.id || i, type: "button", className: "mds-gsearch__row", "data-active": i === active,
      onMouseEnter: () => setActive(i), onClick: () => run(r),
    },
      React.createElement("span", { className: "mds-gsearch__label" }, mark(r.label, q.trim())),
      r.meta ? React.createElement("span", { className: "mds-gsearch__meta" }, r.meta) : React.createElement("span")
    ));
  });

  return (
    <div className="mds-gsearch__scrim" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose && onClose(); }}>
      <div className="mds-gsearch" role="dialog" aria-modal="true" aria-label="Search">
        <div className="mds-gsearch__field">
          <span className="mds-gsearch__glyph" aria-hidden="true">⌕</span>
          <input ref={inputRef} className="mds-gsearch__input" value={q} placeholder={placeholder}
            onChange={(e) => setQ(e.target.value)} onKeyDown={onKeyDown} aria-label="Search" />
          <span className="mds-gsearch__scope">{scopeLabel}</span>
        </div>
        <div className="mds-gsearch__list" ref={listRef}>
          {body.length ? body : (
            <div className="mds-gsearch__empty">
              <b>{q.trim() ? `Nothing matches “${q.trim()}”` : "Type to search"}</b>
              {emptyHint || "Objects come from the screen you are on. Destinations come from the app map."}
            </div>
          )}
        </div>
        <div className="mds-gsearch__foot">
          <span><kbd>↑</kbd><kbd>↓</kbd>move</span>
          <span><kbd>↵</kbd>open</span>
          <span><kbd>esc</kbd>close</span>
          <span style={{ marginLeft: "auto" }}>{rows.length} result{rows.length === 1 ? "" : "s"}</span>
        </div>
      </div>
    </div>
  );
}
