// Meridian MultiSelect — pick SEVERAL values from a list (books, desks, strategies, tags).
// Chips in the field, a checkbox listbox below, type to filter. Functional only, no transitions.
//
// 4.1: this used to be a bulk-selection toolbar that duplicated data/BulkActionBar — and whose
// "Select all" was wired to clear. That role now belongs to BulkActionBar alone; MultiSelect is
// the multi-pick control Select.prompt.md has always pointed at.
import React from "react";
import { Checkbox } from "./Checkbox";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ms-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);margin-bottom:5px;}
.mds-ms-req{color:var(--red,#B54B42);margin-left:2px;}
.mds-ms-wrap{position:relative;width:100%;}
.mds-ms-field{display:flex;align-items:center;gap:4px;width:100%;box-sizing:border-box;
  min-height:32px;padding:3px 3px 3px 6px;border:1px solid var(--border,#D2CDC3);
  background:var(--bg-light,#F8F7F3);cursor:text;flex-wrap:wrap;}
.mds-ms-field:hover{border-color:var(--border-hover,#BEB8AD);}
.mds-ms-field:focus-within{border-color:var(--border-focus,#2D6FB3);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-ms-field--error{border-color:var(--red,#B54B42);}
.mds-ms-field--disabled{background:var(--bg-medium,#EBE8E1);opacity:.6;cursor:not-allowed;}
.mds-ms-chip{display:inline-flex;align-items:center;gap:5px;height:22px;padding:0 4px 0 7px;
  background:var(--bg-active,#E7EDF2);border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-chip,2px);font-family:var(--font-data);font-size:11px;
  color:var(--text-primary,#20252B);max-width:180px;}
.mds-ms-chip__t{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-ms-chip__x{border:0;background:none;padding:0 1px;margin:0;cursor:pointer;font-size:10px;
  color:var(--text-muted,#606870);line-height:1;}
.mds-ms-chip__x:hover{color:var(--red,#B54B42);}
.mds-ms-chip__x:focus-visible{outline:var(--focus-ring);outline-offset:1px;}
.mds-ms-more{font-family:var(--font-data);font-size:11px;color:var(--text-muted,#606870);padding:0 2px;}
.mds-ms-input{flex:1;min-width:60px;height:24px;border:none;background:transparent;outline:none;
  color:var(--text-primary,#20252B);font-family:var(--font-data);font-size:13px;}
.mds-ms-input::placeholder{color:var(--text-disabled,#A4ABB2);}
.mds-ms-btn{flex:0 0 auto;height:24px;padding:0 6px;border:0;background:none;margin:0;
  color:var(--text-muted,#606870);font-size:11px;cursor:pointer;display:inline-flex;align-items:center;}
.mds-ms-btn:hover{color:var(--text-primary,#20252B);}
.mds-ms-btn:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.mds-ms-menu{position:absolute;top:calc(100% + 4px);left:0;right:0;z-index:var(--z-dropdown,100);
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);max-height:260px;
  overflow-y:auto;box-shadow:var(--shadow-menu,0 4px 12px rgba(0,0,0,.10));border-radius:var(--radius-overlay,6px);}
.mds-ms-menu--above{top:auto;bottom:calc(100% + 4px);}
.mds-ms-bulk{display:flex;gap:8px;padding:6px 10px;border-bottom:1px solid var(--border-divider,#E2DED5);
  position:sticky;top:0;background:var(--bg-light,#F8F7F3);z-index:1;}
.mds-ms-bulk button{border:0;background:none;padding:0;margin:0;cursor:pointer;
  font-family:var(--font-body);font-size:11px;color:var(--link,#2A5F96);text-decoration:underline;}
.mds-ms-bulk button:disabled{color:var(--text-disabled,#A4ABB2);cursor:not-allowed;text-decoration:none;}
.mds-ms-bulk button:focus-visible{outline:var(--focus-ring);outline-offset:1px;}
.mds-ms-count{margin-left:auto;font-family:var(--font-data);font-size:10px;color:var(--text-muted,#606870);}
.mds-ms-opt{display:flex;align-items:center;gap:8px;padding:6px 10px;cursor:pointer;
  font-family:var(--font-data);font-size:12px;color:var(--text-primary,#20252B);}
.mds-ms-opt--hl{background:var(--bg-hover,#EDEFF0);}
.mds-ms-opt--disabled{color:var(--text-disabled,#A4ABB2);cursor:not-allowed;}
.mds-ms-opt--disabled.mds-ms-opt--hl{background:transparent;}
.mds-ms-opt__t{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-ms-opt__m{font-size:11px;color:var(--text-muted,#606870);font-variant-numeric:tabular-nums;}
.mds-ms-group{padding:6px 10px 4px;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);
  border-top:1px solid var(--border-divider,#E2DED5);}
.mds-ms-group:first-child{border-top:0;}
.mds-ms-empty{padding:10px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#606870);}
.mds-ms-error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#883832);margin-top:5px;}
.mds-ms-sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "multiselect");
  el.textContent = css;
  document.head.appendChild(el);
}

let uid = 0;
function useUid() {
  if (React.useId) return React.useId().replace(/:/g, "");
  return React.useMemo(() => "mdsms" + ++uid, []);
}

export function MultiSelect({
  label,
  options = [],
  value = [],
  onChange,
  placeholder = "Search…",
  disabled = false,
  required = false,
  error,
  emptyText = "No matches",
  maxChips = 4,
  maxSelected,
  showBulk = true,
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
  const optId = (i) => `${base}-opt-${i}`;
  const errorId = `${base}-error`;

  const [open, setOpen] = React.useState(false);
  const [query, setQuery] = React.useState("");
  const [hl, setHl] = React.useState(0);
  const [above, setAbove] = React.useState(false);
  const rootRef = React.useRef(null);
  const inputRef = React.useRef(null);
  const menuRef = React.useRef(null);

  const norm = React.useMemo(
    () => options.map((o) => (o && typeof o === "object"
      ? { value: o.value, label: String(o.label ?? o.value), meta: o.meta, group: o.group, disabled: !!o.disabled }
      : { value: o, label: String(o) })),
    [options]
  );
  const selectedSet = React.useMemo(() => new Set(value), [value]);
  const selectedOpts = React.useMemo(
    () => value.map((v) => norm.find((o) => o.value === v) || { value: v, label: String(v) }),
    [value, norm]
  );
  const filtered = React.useMemo(() => {
    if (!query) return norm;
    if (filter) return norm.filter((o) => filter(o, query));
    const q = query.toLowerCase();
    return norm.filter((o) => o.label.toLowerCase().includes(q));
  }, [norm, query, filter]);

  const atLimit = maxSelected != null && value.length >= maxSelected;
  React.useEffect(() => { if (disabled) setOpen(false); }, [disabled]);

  React.useEffect(() => {
    if (!open) return;
    const onDown = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) closeMenu(); };
    const onFocusIn = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) closeMenu(); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("focusin", onFocusIn);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("focusin", onFocusIn); };
  }, [open]);

  React.useLayoutEffect(() => {
    if (!open || !menuRef.current) return;
    const r = menuRef.current.getBoundingClientRect();
    setAbove(r.bottom > window.innerHeight && r.top - r.height > 0);
  }, [open]);

  React.useEffect(() => { setHl(0); }, [query]);

  // Keep the active option in view — focus stays in the input, so scroll the menu by hand.
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

  const closeMenu = () => { setOpen(false); setQuery(""); onQueryChange?.(""); };
  const openMenu = () => { if (!disabled) setOpen(true); };

  const toggle = (opt) => {
    if (opt.disabled || disabled) return;
    const has = selectedSet.has(opt.value);
    if (!has && atLimit) return;
    onChange?.(has ? value.filter((v) => v !== opt.value) : [...value, opt.value]);
  };
  const removeAt = (v) => { if (!disabled) onChange?.(value.filter((x) => x !== v)); };

  // Bulk acts on what is CURRENTLY FILTERED — "select all" after typing "EUR" means those, which
  // is the whole reason the search box and the bulk row sit together.
  const selectableNow = filtered.filter((o) => !o.disabled);
  const allShownSelected = selectableNow.length > 0 && selectableNow.every((o) => selectedSet.has(o.value));
  const selectAllShown = () => {
    if (disabled) return;
    let next = [...value];
    for (const o of selectableNow) {
      if (next.includes(o.value)) continue;
      if (maxSelected != null && next.length >= maxSelected) break;
      next.push(o.value);
    }
    onChange?.(next);
  };
  const clearShown = () => {
    if (disabled) return;
    const shown = new Set(selectableNow.map((o) => o.value));
    onChange?.(value.filter((v) => !shown.has(v)));
  };

  const stepHl = (dir) => setHl((h) => {
    for (let i = h + dir; i >= 0 && i < filtered.length; i += dir) if (!filtered[i].disabled) return i;
    return h;
  });

  const onKeyDown = (e) => {
    const k = e.key;
    if (k === "ArrowDown") { e.preventDefault(); open ? stepHl(1) : openMenu(); return; }
    if (k === "ArrowUp") { e.preventDefault(); open ? stepHl(-1) : openMenu(); return; }
    if (k === "Backspace" && !query && value.length) { e.preventDefault(); removeAt(value[value.length - 1]); return; }
    if (!open) return;
    if (k === "Home") { e.preventDefault(); setHl(0); return; }
    if (k === "End") { e.preventDefault(); setHl(filtered.length - 1); return; }
    if (k === "Enter" || (k === " " && !query)) {
      e.preventDefault();
      if (filtered[hl]) toggle(filtered[hl]);
      return;
    }
    if (k === "Escape") { e.preventDefault(); e.stopPropagation(); closeMenu(); return; }
    if (k === "Tab") closeMenu();
  };

  const overflow = Math.max(0, selectedOpts.length - maxChips);
  const chips = selectedOpts.slice(0, maxChips);
  let lastGroup = null;

  return (
    <div ref={rootRef} {...rest}>
      {label && (
        <label className="mds-ms-label" htmlFor={base}>
          {label}{required && <span className="mds-ms-req" aria-hidden="true">*</span>}
        </label>
      )}
      <div className="mds-ms-wrap">
        <div
          className={`mds-ms-field${error ? " mds-ms-field--error" : ""}${disabled ? " mds-ms-field--disabled" : ""}`}
          onClick={() => { if (!disabled) { inputRef.current?.focus(); openMenu(); } }}
        >
          {chips.map((o) => (
            <span key={String(o.value)} className="mds-ms-chip">
              <span className="mds-ms-chip__t">{o.label}</span>
              {!disabled && (
                <button type="button" className="mds-ms-chip__x" aria-label={`Remove ${o.label}`}
                  onClick={(e) => { e.stopPropagation(); removeAt(o.value); }}>✕</button>
              )}
            </span>
          ))}
          {overflow > 0 && <span className="mds-ms-more">+{overflow}</span>}
          <input
            ref={inputRef}
            id={base}
            name={name}
            type="text"
            autoComplete="off"
            className="mds-ms-input"
            role="combobox"
            aria-expanded={open}
            aria-controls={open ? listId : undefined}
            aria-autocomplete="list"
            aria-activedescendant={open && filtered[hl] ? optId(hl) : undefined}
            aria-label={!label ? ariaLabel : undefined}
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? errorId : undefined}
            aria-required={required || undefined}
            disabled={disabled}
            placeholder={value.length ? "" : placeholder}
            value={query}
            onChange={(e) => { setQuery(e.target.value); onQueryChange?.(e.target.value); openMenu(); }}
            onKeyDown={onKeyDown}
          />
          {value.length > 0 && !disabled && (
            <button type="button" className="mds-ms-btn" tabIndex={-1} aria-label="Clear all"
              onClick={(e) => { e.stopPropagation(); onChange?.([]); inputRef.current?.focus(); }}>✕</button>
          )}
          <button type="button" className="mds-ms-btn" disabled={disabled} tabIndex={-1}
            aria-label={open ? "Close options" : "Open options"}
            onClick={(e) => { e.stopPropagation(); open ? closeMenu() : openMenu(); }}>▾</button>
        </div>
        {open && (
          <div ref={menuRef} className={`mds-ms-menu${above ? " mds-ms-menu--above" : ""}`}
            >
            {showBulk && (
              <div className="mds-ms-bulk">
                <button type="button" disabled={allShownSelected || atLimit} onClick={selectAllShown}>
                  {query ? "Select matches" : "Select all"}
                </button>
                <button type="button" disabled={!selectableNow.some((o) => selectedSet.has(o.value))} onClick={clearShown}>
                  Clear
                </button>
                <span className="mds-ms-count">
                  {value.length} selected{maxSelected != null ? ` / ${maxSelected}` : ""}
                </span>
              </div>
            )}
            {filtered.length === 0 && <div className="mds-ms-empty">{emptyText}</div>}
            <div role="listbox" aria-multiselectable="true" id={listId} aria-label={label || ariaLabel || "Options"}>
            {filtered.map((opt, i) => {
              const on = selectedSet.has(opt.value);
              const blocked = opt.disabled || (!on && atLimit);
              const head = opt.group && opt.group !== lastGroup ? ((lastGroup = opt.group), opt.group) : null;
              return (
                <React.Fragment key={String(opt.value)}>
                  {head && <div className="mds-ms-group" role="presentation">{head}</div>}
                  <div
                    id={optId(i)}
                    role="option"
                    aria-selected={on}
                    aria-disabled={blocked || undefined}
                    className={`mds-ms-opt${i === hl ? " mds-ms-opt--hl" : ""}${blocked ? " mds-ms-opt--disabled" : ""}`}
                    onMouseEnter={() => !blocked && setHl(i)}
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => toggle(opt)}
                  >
                    <span aria-hidden="true" style={{ display: "flex", pointerEvents: "none" }}>
                      <span className={`mds-checkbox-box${on ? " mds-checkbox-box--checked" : ""}`}>{on ? "✓" : ""}</span>
                    </span>
                    <span className="mds-ms-opt__t">{opt.label}</span>
                    {opt.meta != null && <span className="mds-ms-opt__m">{opt.meta}</span>}
                  </div>
                </React.Fragment>
              );
            })}
            </div>
          </div>
        )}
      </div>
      {error && <div className="mds-ms-error" id={errorId}>{error}</div>}
      <span className="mds-ms-sr" role="status" aria-live="polite">
        {value.length} selected{atLimit ? `, limit of ${maxSelected} reached` : ""}
      </span>
    </div>
  );
}

MultiSelect.displayName = "MultiSelect";
