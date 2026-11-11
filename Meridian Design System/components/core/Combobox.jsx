// Meridian Combobox — searchable single-select. Type to filter, arrows to navigate.
// Functional only: no transitions. Field geometry matches Input (32px) so the two line up in a row.
// Full ARIA 1.2 combobox: unique ids per instance, aria-activedescendant, listbox option ids,
// managed scroll (the menu scrolls, focus never leaves the input).
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-cb-wrap{position:relative;width:100%;}
.mds-cb-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);margin-bottom:5px;}
.mds-cb-req{color:var(--red,#B54B42);margin-left:2px;}
.mds-cb-field{display:flex;align-items:center;width:100%;box-sizing:border-box;height:32px;
  border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);}
.mds-cb-field:hover{border-color:var(--border-hover,#BEB8AD);}
.mds-cb-field:focus-within{border-color:var(--border-focus,#2D6FB3);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-cb-field--error{border-color:var(--red,#B54B42);}
.mds-cb-field--disabled{background:var(--bg-medium,#EBE8E1);opacity:.6;cursor:not-allowed;}
.mds-cb-input{flex:1;min-width:0;height:100%;padding:7px 10px;border:none;background:transparent;
  color:var(--text-primary,#20252B);font-family:var(--font-data);font-size:13px;outline:none;
  text-overflow:ellipsis;}
.mds-cb-input::placeholder{color:var(--text-disabled,#A4ABB2);}
.mds-cb-input:disabled{cursor:not-allowed;}
.mds-cb-btn{flex:0 0 auto;height:100%;min-width:24px;min-height:24px;box-sizing:border-box;padding:0 8px;border:0;background:none;margin:0;
  color:var(--text-muted,#606870);font-family:var(--font-data);font-size:11px;cursor:pointer;
  display:inline-flex;align-items:center;}
.mds-cb-btn:hover{color:var(--text-primary,#20252B);}
.mds-cb-btn:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.mds-cb-btn:disabled{cursor:not-allowed;color:var(--text-disabled,#A4ABB2);}
.mds-cb-menu{position:absolute;top:calc(100% + 4px);left:0;right:0;z-index:var(--z-dropdown,100);
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);
  max-height:240px;overflow-y:auto;box-shadow:var(--shadow-menu,0 4px 12px rgba(0,0,0,.10));
  border-radius:var(--radius-overlay,6px);}
.mds-cb-opt{padding:8px 12px;font-family:var(--font-data);font-size:12px;
  color:var(--text-primary,#20252B);cursor:pointer;display:flex;align-items:center;gap:8px;}
.mds-cb-opt--hl{background:var(--bg-hover,#EDEFF0);}
.mds-cb-opt--active{background:var(--bg-active,#E7EDF2);color:var(--interaction-selected,#496C8F);font-weight:600;}
.mds-cb-opt--active .mds-cb-check{margin-left:auto;font-size:11px;}
.mds-cb-opt--disabled{color:var(--text-disabled,#A4ABB2);cursor:not-allowed;}
.mds-cb-opt--disabled.mds-cb-opt--hl{background:transparent;}
.mds-cb-optmeta{margin-left:auto;font-size:11px;color:var(--text-muted,#606870);font-variant-numeric:tabular-nums;}
.mds-cb-opt--active .mds-cb-optmeta{color:inherit;}
.mds-cb-group{padding:6px 12px 4px;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);
  border-top:1px solid var(--border-divider,#E2DED5);}
.mds-cb-group:first-child{border-top:0;}
.mds-cb-empty{padding:10px 12px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#606870);}
.mds-cb-error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#883832);margin-top:5px;}
.mds-cb-mark{background:var(--brand-signal-ghost,#F6E6DF);color:var(--brand-signal,#C56343);}
.mds-cb-sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "combobox");
  el.textContent = css;
  document.head.appendChild(el);
}

let uid = 0;
// React 18 has useId; keep a counter fallback so the component also works under 17.
function useUid() {
  if (React.useId) return React.useId().replace(/:/g, "");
  return React.useMemo(() => "mdscb" + ++uid, []);
}

function highlight(label, query) {
  if (!query) return label;
  const idx = label.toLowerCase().indexOf(query.toLowerCase());
  if (idx === -1) return label;
  return (
    <>
      {label.slice(0, idx)}
      <span className="mds-cb-mark">{label.slice(idx, idx + query.length)}</span>
      {label.slice(idx + query.length)}
    </>
  );
}

export function Combobox({
  label,
  options = [],
  value,
  onChange,
  placeholder = "Search…",
  disabled = false,
  emptyText = "No matches",
  error,
  required = false,
  clearable = false,
  openOnFocus = false,
  filter,
  onQueryChange,
  id,
  name,
  "aria-label": ariaLabel,
  ...rest
}) {
  inject();
  const auto = useUid();
  const base = id || auto;
  const listId = `${base}-listbox`;
  const labelId = `${base}-label`;
  const errorId = `${base}-error`;
  const optId = (i) => `${base}-opt-${i}`;

  const [open, setOpen] = React.useState(false);
  const [query, setQuery] = React.useState("");
  const [hl, setHl] = React.useState(0);
  const rootRef = React.useRef(null);
  const inputRef = React.useRef(null);
  const menuRef = React.useRef(null);

  const norm = React.useMemo(
    () => options.map((o) => (o && typeof o === "object"
      ? { value: o.value, label: String(o.label ?? o.value), meta: o.meta, group: o.group, disabled: !!o.disabled }
      : { value: o, label: String(o) })),
    [options]
  );
  const selected = React.useMemo(() => norm.find((o) => o.value === value), [norm, value]);
  const filtered = React.useMemo(() => {
    if (!query) return norm;
    if (filter) return norm.filter((o) => filter(o, query));
    const q = query.toLowerCase();
    return norm.filter((o) => o.label.toLowerCase().includes(q));
  }, [norm, query, filter]);
  React.useEffect(() => { if (disabled) setOpen(false); }, [disabled]);

  const firstEnabled = (list) => list.findIndex((o) => !o.disabled);
  const stepHl = (dir) => {
    setHl((h) => {
      for (let i = h + dir; i >= 0 && i < filtered.length; i += dir) if (!filtered[i].disabled) return i;
      return h;
    });
  };

  const openMenu = () => {
    if (disabled || open) return;
    // Land the highlight on the current selection, not blindly on row 0.
    const at = selected ? filtered.findIndex((o) => o.value === selected.value) : -1;
    setHl(at >= 0 ? at : Math.max(0, firstEnabled(filtered)));
    setOpen(true);
  };
  const closeMenu = () => { setOpen(false); setQuery(""); onQueryChange?.(""); };

  // Close on outside pointer AND on focus leaving the component (Tab out).
  React.useEffect(() => {
    if (!open) return;
    const onDown = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) closeMenu(); };
    const onFocusIn = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) closeMenu(); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("focusin", onFocusIn);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("focusin", onFocusIn); };
  }, [open]);

  React.useEffect(() => { setHl(Math.max(0, firstEnabled(filtered))); }, [query]);

  // Keep the active option in view by scrolling the MENU — focus stays on the input, so this is
  // manual scrollTop math rather than a focus-driven scroll.
  React.useEffect(() => {
    if (!open || !menuRef.current) return;
    const el = menuRef.current.querySelector(`#${CSS.escape(optId(hl))}`);
    if (!el) return;
    const m = menuRef.current;
    if (el.offsetTop < m.scrollTop) m.scrollTop = el.offsetTop;
    else if (el.offsetTop + el.offsetHeight > m.scrollTop + m.clientHeight) {
      m.scrollTop = el.offsetTop + el.offsetHeight - m.clientHeight;
    }
  }, [hl, open]);

  const commit = (opt) => {
    if (opt.disabled || disabled) return;
    onChange?.(opt.value);
    closeMenu();
    inputRef.current?.focus();
  };
  const clear = () => { onChange?.(undefined); setQuery(""); onQueryChange?.(""); inputRef.current?.focus(); };

  const handleKey = (e) => {
    const k = e.key;
    if (k === "ArrowDown") { e.preventDefault(); open ? stepHl(1) : openMenu(); return; }
    if (k === "ArrowUp") { e.preventDefault(); open ? stepHl(-1) : openMenu(); return; }
    if (!open && (k === "Enter" || k === " ") && !query) { e.preventDefault(); openMenu(); return; }
    if (!open) return;
    if (k === "Home") { e.preventDefault(); setHl(Math.max(0, firstEnabled(filtered))); return; }
    if (k === "End") { e.preventDefault(); for (let i = filtered.length - 1; i >= 0; i--) if (!filtered[i].disabled) { setHl(i); break; } return; }
    if (k === "PageDown") { e.preventDefault(); setHl((h) => Math.min(filtered.length - 1, h + 8)); return; }
    if (k === "PageUp") { e.preventDefault(); setHl((h) => Math.max(0, h - 8)); return; }
    if (k === "Enter") { e.preventDefault(); if (filtered[hl]) commit(filtered[hl]); return; }
    if (k === "Escape") { e.preventDefault(); e.stopPropagation(); closeMenu(); return; }
    if (k === "Tab") closeMenu();
  };

  let lastGroup = null;
  return (
    <div ref={rootRef} {...rest}>
      {label && (
        <label className="mds-cb-label" id={labelId} htmlFor={base}>
          {label}{required && <span className="mds-cb-req" aria-hidden="true">*</span>}
        </label>
      )}
      <div className="mds-cb-wrap">
        <div className={`mds-cb-field${error ? " mds-cb-field--error" : ""}${disabled ? " mds-cb-field--disabled" : ""}`}>
          <input
            ref={inputRef}
            id={base}
            name={name}
            type="text"
            autoComplete="off"
            className="mds-cb-input"
            role="combobox"
            aria-expanded={open}
            aria-controls={open ? listId : undefined}
            aria-autocomplete="list"
            aria-activedescendant={open && filtered[hl] ? optId(hl) : undefined}
            aria-labelledby={label ? labelId : undefined}
            aria-label={!label ? ariaLabel : undefined}
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? errorId : undefined}
            aria-required={required || undefined}
            disabled={disabled}
            placeholder={selected && !query ? selected.label : placeholder}
            value={open ? query : (selected ? selected.label : "")}
            onFocus={() => { if (openOnFocus) openMenu(); }}
            onClick={() => openMenu()}
            onChange={(e) => { setQuery(e.target.value); onQueryChange?.(e.target.value); if (!open) openMenu(); }}
            onKeyDown={handleKey}
          />
          {clearable && selected && !disabled && (
            <button type="button" className="mds-cb-btn" onClick={clear} aria-label="Clear selection">✕</button>
          )}
          <button
            type="button"
            className="mds-cb-btn"
            disabled={disabled}
            tabIndex={-1}
            aria-label={open ? "Close options" : "Open options"}
            onClick={() => (open ? closeMenu() : openMenu())}
          >▾</button>
        </div>
        {open && (
          <div className="mds-cb-menu" role="listbox" id={listId} ref={menuRef} aria-label={label || ariaLabel}>
            {filtered.length === 0 && <div className="mds-cb-empty">{emptyText}</div>}
            {filtered.map((opt, i) => {
              const head = opt.group && opt.group !== lastGroup ? ((lastGroup = opt.group), opt.group) : null;
              return (
                <React.Fragment key={String(opt.value)}>
                  {head && <div className="mds-cb-group" role="presentation">{head}</div>}
                  <div
                    id={optId(i)}
                    role="option"
                    aria-selected={opt.value === value}
                    aria-disabled={opt.disabled || undefined}
                    className={`mds-cb-opt${i === hl ? " mds-cb-opt--hl" : ""}${opt.value === value ? " mds-cb-opt--active" : ""}${opt.disabled ? " mds-cb-opt--disabled" : ""}`}
                    onMouseEnter={() => !opt.disabled && setHl(i)}
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => commit(opt)}
                  >
                    <span>{highlight(opt.label, query)}</span>
                    {opt.meta != null && <span className="mds-cb-optmeta">{opt.meta}</span>}
                    {opt.value === value && <span className="mds-cb-check" aria-hidden="true">✓</span>}
                  </div>
                </React.Fragment>
              );
            })}
          </div>
        )}
      </div>
      {error && <div className="mds-cb-error" id={errorId}>{error}</div>}
      <span className="mds-cb-sr" role="status" aria-live="polite">
        {open ? `${filtered.length} ${filtered.length === 1 ? "result" : "results"}` : ""}
      </span>
    </div>
  );
}

Combobox.displayName = "Combobox";
