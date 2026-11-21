// Meridian AsyncCombobox — type-ahead picker over a large or server-backed option set.
// The "pick one of 40k instruments" control the system guaranteed it would need: debounced
// async loading, a windowed option list (only visible rows render), keyboard nav, and a
// quiet loading/empty/error footer. Controlled value; you own the fetcher.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-cbx{position:relative;font-family:var(--font-body);}
.mds-cbx__field{display:flex;align-items:center;gap:6px;height:30px;padding:0 8px;
  border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);}
.mds-cbx__field:focus-within{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-1px;}
.mds-cbx__input{flex:1;min-width:0;border:none;outline:none;background:transparent;
  font-family:var(--font-data);font-size:12px;color:var(--text-primary,#20252B);}
.mds-cbx__caret{color:var(--text-muted,#606870);font-size:10px;flex:none;}
.mds-cbx__pop{position:absolute;left:0;right:0;top:calc(100% + 3px);z-index:var(--z-dropdown,100);
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border-strong,#AAA49A);
  box-shadow:var(--shadow-menu);max-height:264px;overflow-y:auto;}
.mds-cbx__opt{padding:7px 10px;font-size:12px;color:var(--text-primary,#20252B);cursor:pointer;
  display:flex;align-items:baseline;gap:8px;box-sizing:border-box;}
.mds-cbx__opt--on{background:var(--bg-active,#E7EDF2);}
.mds-cbx__opt-sym{font-family:var(--font-data);font-weight:600;flex:none;min-width:56px;}
.mds-cbx__opt-label{color:var(--text-secondary,#535C65);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-cbx__foot{padding:8px 10px;font-size:11px;color:var(--text-muted,#606870);
  font-family:var(--font-body);text-align:center;}
.mds-cbx__foot--err{color:var(--red-dim,#883832);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "asynccombobox");
  el.textContent = css;
  document.head.appendChild(el);
}

const ROW_H = 32;

export function AsyncCombobox({
  value = null, onChange, fetchOptions, getKey = (o) => o.value, getLabel = (o) => o.label,
  getSecondary = (o) => o.secondary, placeholder = "Search…", debounceMs = 220, minChars = 0,
  emptyLabel = "No matches", maxRows = 200, id, disabled = false,
  "aria-label": ariaLabel = "Search options",
}) {
  inject();
  const [open, setOpen] = React.useState(false);
  const [text, setText] = React.useState("");
  const [options, setOptions] = React.useState([]);
  const [status, setStatus] = React.useState("idle"); // idle | loading | ready | empty | error
  const [hl, setHl] = React.useState(0);
  const [scrollTop, setScrollTop] = React.useState(0);
  const rootRef = React.useRef(null);
  const listRef = React.useRef(null);
  const reqRef = React.useRef(0);
  const generatedId = React.useId();
  const inputId = id || generatedId;
  const listId = `${inputId}-options`;
  React.useEffect(() => { if (disabled) setOpen(false); }, [disabled]);

  React.useEffect(() => {
    if (!open || disabled) return;
    if (text.length < minChars) { setOptions([]); setStatus("idle"); return; }
    const reqId = ++reqRef.current;
    let current = true;
    const controller = typeof AbortController !== "undefined" ? new AbortController() : { abort() {}, signal: undefined };
    setStatus("loading");
    setOptions([]);
    setHl(0);
    setScrollTop(0);
    const t = setTimeout(async () => {
      try {
        const res = await fetchOptions(text, controller.signal);
        if (!current || reqId !== reqRef.current) return;
        const rows = (res || []).slice(0, maxRows);
        setOptions(rows);
        setStatus(rows.length ? "ready" : "empty");
        setHl(0);
      } catch (e) {
        if (e && e.name === "AbortError") return;
        if (!current || reqId !== reqRef.current) return;
        setOptions([]);
        setStatus("error");
      }
    }, debounceMs);
    return () => { current = false; controller.abort(); clearTimeout(t); };
  }, [text, open, minChars, debounceMs, maxRows, fetchOptions, disabled]);

  React.useEffect(() => {
    if (!open) return;
    const onDown = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false); };
    const onFocus = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("focusin", onFocus);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("focusin", onFocus); };
  }, [open]);

  const commit = (opt) => {
    if (disabled) return;
    onChange && onChange(opt);
    setText(getLabel(opt) != null ? String(getSecondary(opt) ? getKey(opt) : getLabel(opt)) : "");
    setOpen(false);
  };

  const onKeyDown = (e) => {
    if (disabled) return;
    if (!open && (e.key === "ArrowDown" || e.key === "ArrowUp" || e.key === "Enter")) { e.preventDefault(); setOpen(true); return; }
    if (e.key === "ArrowDown") { e.preventDefault(); setHl((h) => Math.max(0, Math.min(options.length - 1, h + 1))); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setHl((h) => Math.max(0, h - 1)); }
    else if (open && e.key === "Home") { e.preventDefault(); setHl(0); }
    else if (open && e.key === "End") { e.preventDefault(); setHl(Math.max(0, options.length - 1)); }
    else if (e.key === "Enter") { e.preventDefault(); if (options[hl]) commit(options[hl]); }
    else if (e.key === "Escape" && open) { e.preventDefault(); e.stopPropagation(); setOpen(false); }
    else if (e.key === "Tab") { setOpen(false); }
  };

  // Windowed list: only render the visible slice.
  const viewport = 264;
  const start = Math.max(0, Math.floor(scrollTop / ROW_H) - 4);
  const end = Math.min(options.length, start + Math.ceil(viewport / ROW_H) + 8);
  const pad = start * ROW_H;

  React.useEffect(() => {
    // keep highlighted row in view
    if (!listRef.current) return;
    const top = hl * ROW_H, bottom = top + ROW_H;
    const st = listRef.current.scrollTop, h = listRef.current.clientHeight;
    if (top < st) listRef.current.scrollTop = top;
    else if (bottom > st + h) listRef.current.scrollTop = bottom - h;
  }, [hl]);

  const display = value && !open ? String(getSecondary(value) ? getKey(value) : getLabel(value)) : text;

  return (
    <div className="mds-cbx" ref={rootRef}>
      <div className="mds-cbx__field">
        <input
          id={inputId} disabled={disabled} aria-label={ariaLabel}
          className="mds-cbx__input" role="combobox" aria-expanded={open} aria-autocomplete="list"
          aria-controls={open ? listId : undefined}
          aria-activedescendant={open && status === "ready" && hl >= start && hl < end && options[hl] ? `${listId}-${hl}` : undefined}
          value={display} placeholder={placeholder}
          onChange={(e) => { setText(e.target.value); setOpen(true); }}
          onFocus={() => { if (!disabled) setOpen(true); }} onKeyDown={onKeyDown}
        />
        <span className="mds-cbx__caret" aria-hidden="true">▾</span>
      </div>
      {open && (
        <div className="mds-cbx__pop" id={listId} ref={listRef} role="listbox" aria-label={ariaLabel}
          onScroll={(e) => setScrollTop(e.currentTarget.scrollTop)}>
          {status === "ready" && (
            <div style={{ height: options.length * ROW_H, position: "relative" }}>
              <div style={{ transform: `translateY(${pad}px)` }}>
                {options.slice(start, end).map((o, i) => {
                  const idx = start + i;
                  return (
                    <div key={getKey(o)} id={`${listId}-${idx}`} role="option" aria-selected={idx === hl}
                      className={`mds-cbx__opt${idx === hl ? " mds-cbx__opt--on" : ""}`}
                      style={{ height: ROW_H }}
                      onMouseEnter={() => setHl(idx)} onMouseDown={(e) => { e.preventDefault(); commit(o); }}>
                      <span className="mds-cbx__opt-sym">{getSecondary(o) ? getKey(o) : getLabel(o)}</span>
                      {getSecondary(o) != null && <span className="mds-cbx__opt-label">{getSecondary(o)}</span>}
                    </div>
                  );
                })}
              </div>
            </div>
          )}
          {status === "loading" && <div className="mds-cbx__foot">Searching…</div>}
          {status === "empty" && <div className="mds-cbx__foot">{emptyLabel}</div>}
          {status === "error" && <div className="mds-cbx__foot mds-cbx__foot--err">Search failed — try again</div>}
          {status === "idle" && minChars > 0 && <div className="mds-cbx__foot">Type at least {minChars} character{minChars > 1 ? "s" : ""}</div>}
        </div>
      )}
    </div>
  );
}
