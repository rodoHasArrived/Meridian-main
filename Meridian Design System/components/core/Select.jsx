// Meridian Select — functional only. No radius, no shadows, no transitions.
// Full listbox keyboard contract: arrows/Home/End/typeahead, Enter/Space select,
// Esc close, aria-activedescendant; flips above the trigger near the viewport edge.
// A11y (3.5): a fully named select-only combobox — trigger role="combobox" named by its label
// AND current value (APG pattern), aria-controls wired to the listbox, `ariaLabel` fallback so
// an unlabeled instance is never anonymous.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-sel-wrap{position:relative;display:inline-block;width:100%;}
.mds-sel-btn{width:100%;height:32px;padding:7px 32px 7px 10px;
  border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  color:var(--text-primary,#20252B);font-family:var(--font-data);font-size:13px;
  text-align:left;cursor:pointer;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-sel-btn:hover{border-color:var(--border-hover,#BEB8AD);}
.mds-sel-btn:focus,.mds-sel-btn[aria-expanded="true"]{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:var(--focus-ring-offset,1px);}
.mds-sel-btn:disabled{background:var(--bg-medium,#EBE8E1);color:var(--text-disabled,#A4ABB2);cursor:not-allowed;}
.mds-sel-btn--placeholder{color:var(--text-disabled,#A4ABB2);}
.mds-sel-caret{position:absolute;right:10px;top:50%;transform:translateY(-50%);pointer-events:none;color:var(--text-muted,#6B737B);}
.mds-sel-menu{position:absolute;top:calc(100% + 4px);left:0;right:0;z-index:var(--z-dropdown,100);
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);
  max-height:240px;overflow-y:auto;}
.mds-sel-menu--above{top:auto;bottom:calc(100% + 4px);}
.mds-sel-opt{padding:8px 12px;font-family:var(--font-data);font-size:12px;
  color:var(--text-primary,#20252B);cursor:pointer;display:flex;align-items:center;gap:8px;}
.mds-sel-opt--focus{background:var(--bg-hover,#EDEFF0);}
.mds-sel-opt--active{background:var(--bg-active,#E7EDF2);color:var(--interaction-selected,#496C8F);font-weight:600;}
.mds-sel-opt--active::after{content:"✓";margin-left:auto;font-size:11px;}
.mds-sel-opt--disabled{color:var(--text-disabled,#A4ABB2);cursor:not-allowed;}
.mds-sel-divider{height:1px;background:var(--border,#D2CDC3);margin:4px 0;}
.mds-sel-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#6B737B);margin-bottom:5px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","select");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Select({ label, options = [], value, onChange, placeholder = "Select…", disabled = false, ariaLabel }) {
  inject();
  const uid = React.useId();
  const [open, setOpen] = React.useState(false);
  const [above, setAbove] = React.useState(false);
  const ref = React.useRef(null);
  const menuRef = React.useRef(null);
  const typeahead = React.useRef({ str: "", t: 0 });

  // Flat model: keep original indices so ids stay stable; dividers aren't options.
  const items = options.map((opt, i) => opt === "---"
    ? { divider: true, i }
    : { i, value: opt.value ?? opt, label: opt.label ?? opt, disabled: !!opt.disabled });
  const enabled = items.filter(it => !it.divider && !it.disabled);
  const selIdx = enabled.findIndex(it => it.value === value);
  const [focusIdx, setFocusIdx] = React.useState(-1); // index into `enabled`

  React.useEffect(() => {
    const handler = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const openMenu = () => {
    if (disabled) return;
    setFocusIdx(selIdx >= 0 ? selIdx : 0);
    const r = ref.current?.getBoundingClientRect();
    setAbove(!!r && window.innerHeight - r.bottom < 260 && r.top > 260);
    setOpen(true);
  };

  // Keep the focused option in view while arrowing.
  React.useEffect(() => {
    if (!open || focusIdx < 0 || !menuRef.current) return;
    const el = menuRef.current.querySelector(`[data-eidx="${focusIdx}"]`);
    if (!el) return;
    const m = menuRef.current;
    if (el.offsetTop < m.scrollTop) m.scrollTop = el.offsetTop;
    else if (el.offsetTop + el.offsetHeight > m.scrollTop + m.clientHeight)
      m.scrollTop = el.offsetTop + el.offsetHeight - m.clientHeight;
  }, [open, focusIdx]);

  const commit = (it) => { onChange?.(it.value); setOpen(false); };

  const handleKey = (e) => {
    if (disabled) return;
    if (!open) {
      if (["Enter", " ", "ArrowDown", "ArrowUp"].includes(e.key)) { e.preventDefault(); openMenu(); }
      return;
    }
    switch (e.key) {
      case "Escape": e.preventDefault(); setOpen(false); break;
      case "Tab": setOpen(false); break;
      case "ArrowDown": e.preventDefault(); setFocusIdx(i => Math.min(i + 1, enabled.length - 1)); break;
      case "ArrowUp": e.preventDefault(); setFocusIdx(i => Math.max(i - 1, 0)); break;
      case "Home": e.preventDefault(); setFocusIdx(0); break;
      case "End": e.preventDefault(); setFocusIdx(enabled.length - 1); break;
      case "Enter": case " ": e.preventDefault(); if (enabled[focusIdx]) commit(enabled[focusIdx]); break;
      default: {
        if (e.key.length === 1 && !e.metaKey && !e.ctrlKey && !e.altKey) {
          const now = Date.now();
          const ta = typeahead.current;
          ta.str = (now - ta.t < 600 ? ta.str : "") + e.key.toLowerCase();
          ta.t = now;
          const hit = enabled.findIndex(it => String(it.label).toLowerCase().startsWith(ta.str));
          if (hit >= 0) setFocusIdx(hit);
        }
      }
    }
  };

  const displayLabel = items.find(it => !it.divider && it.value === value)?.label ?? null;
  const activeId = open && focusIdx >= 0 && enabled[focusIdx] ? `${uid}-opt-${enabled[focusIdx].i}` : undefined;

  return (
    <div ref={ref}>
      {label && <span className="mds-sel-label" id={`${uid}-label`}>{label}</span>}
      <div className="mds-sel-wrap">
        <button
          type="button"
          id={`${uid}-btn`}
          className={`mds-sel-btn${!displayLabel ? " mds-sel-btn--placeholder" : ""}`}
          role="combobox"
          aria-expanded={open}
          aria-haspopup="listbox"
          aria-controls={`${uid}-menu`}
          aria-labelledby={label ? `${uid}-label ${uid}-btn` : undefined}
          aria-label={!label ? (ariaLabel || placeholder) : undefined}
          aria-activedescendant={activeId}
          disabled={disabled}
          onClick={() => (open ? setOpen(false) : openMenu())}
          onKeyDown={handleKey}
        >
          {displayLabel ?? placeholder}
        </button>
        <span className="mds-sel-caret" aria-hidden="true">▾</span>
        {open && (
          <div ref={menuRef} id={`${uid}-menu`} className={`mds-sel-menu${above ? " mds-sel-menu--above" : ""}`}
            role="listbox" aria-labelledby={label ? `${uid}-label` : undefined} aria-label={!label ? (ariaLabel || placeholder) : undefined}>
            {items.map((it) => {
              if (it.divider) return <div key={it.i} className="mds-sel-divider" />;
              const eidx = enabled.indexOf(it);
              const cls = `mds-sel-opt${it.value === value ? " mds-sel-opt--active" : ""}${eidx === focusIdx ? " mds-sel-opt--focus" : ""}${it.disabled ? " mds-sel-opt--disabled" : ""}`;
              return (
                <div key={it.i} id={`${uid}-opt-${it.i}`} role="option"
                  aria-selected={it.value === value} aria-disabled={it.disabled || undefined}
                  data-eidx={eidx >= 0 ? eidx : undefined} className={cls}
                  onMouseEnter={() => eidx >= 0 && setFocusIdx(eidx)}
                  onClick={() => { if (!it.disabled) commit(it); }}
                >
                  {it.label}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
