// Meridian Select — functional only. No radius, no shadows, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-sel-wrap{position:relative;display:inline-block;width:100%;}
.mds-sel-btn{width:100%;height:32px;padding:7px 32px 7px 10px;
  border:1px solid var(--border,#E4E3DE);background:var(--bg-light,#FBFAF8);
  color:var(--text-primary,#22252A);font-family:var(--font-data);font-size:13px;
  text-align:left;cursor:pointer;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-sel-btn:hover{border-color:var(--border-hover,#C6C3BB);}
.mds-sel-btn:focus,.mds-sel-btn[aria-expanded="true"]{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-sel-btn--placeholder{color:var(--text-disabled,#94999F);}
.mds-sel-caret{position:absolute;right:10px;top:50%;transform:translateY(-50%);pointer-events:none;color:var(--text-muted,#5E666F);}
.mds-sel-menu{position:absolute;top:calc(100% + 4px);left:0;right:0;z-index:100;
  background:var(--bg-light,#FBFAF8);border:1px solid var(--border,#E4E3DE);
  overflow:hidden;max-height:240px;overflow-y:auto;}
.mds-sel-opt{padding:8px 12px;font-family:var(--font-data);font-size:12px;
  color:var(--text-primary,#22252A);cursor:pointer;display:flex;align-items:center;gap:8px;}
.mds-sel-opt[aria-disabled="true"]{opacity:.5;cursor:not-allowed;}
.mds-sel-opt:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.mds-sel-opt:hover{background:var(--bg-hover,#F0EEE9);}
.mds-sel-opt--active{background:var(--bg-active,#F2E3DB);color:var(--accent-dim,#8C4429);font-weight:600;}
.mds-sel-opt--active::after{content:"✓";margin-left:auto;font-size:11px;}
.mds-sel-divider{height:1px;background:var(--border,#E4E3DE);margin:4px 0;}
.mds-sel-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#5E666F);margin-bottom:5px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","select");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Select({ label, options = [], value, onChange, placeholder = "Select…", disabled = false,
  id, "aria-label": ariaLabel, "aria-labelledby": ariaLabelledBy, "aria-describedby": ariaDescribedBy, "aria-invalid": ariaInvalid }) {
  inject();
  const identity = React.useId();
  const controlId = id || `${identity}-control`;
  const labelId = `${identity}-label`;
  const valueId = `${identity}-value`;
  const menuId = `${identity}-options`;
  const [open, setOpen] = React.useState(false);
  const [active, setActive] = React.useState(0);
  const ref = React.useRef(null);
  const trigger = React.useRef(null);
  const optionRefs = React.useRef(new Map());
  const enabled = options.map((option, index) => ({ option, index })).filter(({ option }) => option !== "---" && !option.disabled);

  React.useEffect(() => {
    const handler = (event) => { if (ref.current && !ref.current.contains(event.target)) setOpen(false); };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);
  React.useEffect(() => { if (open) optionRefs.current.get(active)?.focus(); }, [open, active]);

  const selected = options.find((option) => option !== "---" && (option.value ?? option) === value);
  const displayLabel = selected ? (selected.label ?? selected) : null;
  const labelledBy = ariaLabelledBy || (!ariaLabel && label ? `${labelId} ${valueId}` : undefined);
  const close = () => { setOpen(false); trigger.current?.focus(); };
  const show = (edge = "selected") => {
    if (disabled || enabled.length === 0) return;
    const selectedIndex = enabled.find(({ option }) => (option.value ?? option) === value)?.index;
    setActive(edge === "last" ? enabled[enabled.length - 1].index : edge === "first" ? enabled[0].index : selectedIndex ?? enabled[0].index);
    setOpen(true);
  };
  const choose = (index) => {
    const option = options[index];
    if (option == null || option === "---" || option.disabled) return;
    onChange?.(option.value ?? option);
    close();
  };
  const onTriggerKeyDown = (event) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault(); show(event.key === "ArrowUp" ? "last" : "selected");
    } else if (event.key === "Escape" && open) { event.preventDefault(); close(); }
  };
  const onMenuKeyDown = (event) => {
    const index = Math.max(0, enabled.findIndex((entry) => entry.index === active));
    let next;
    if (event.key === "ArrowDown") next = (index + 1) % enabled.length;
    else if (event.key === "ArrowUp") next = (index - 1 + enabled.length) % enabled.length;
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = enabled.length - 1;
    else if (event.key === "Enter" || event.key === " ") { event.preventDefault(); choose(active); return; }
    else if (event.key === "Escape") { event.preventDefault(); close(); return; }
    else if (event.key === "Tab") { close(); return; }
    else return;
    event.preventDefault(); setActive(enabled[next].index);
  };

  return (
    <div ref={ref}>
      {label && <label id={labelId} htmlFor={controlId} className="mds-sel-label">{label}</label>}
      <div className="mds-sel-wrap">
        <button id={controlId} ref={trigger} type="button"
          className={`mds-sel-btn${!displayLabel ? " mds-sel-btn--placeholder" : ""}`}
          aria-label={ariaLabel} aria-labelledby={labelledBy} aria-describedby={ariaDescribedBy} aria-invalid={ariaInvalid}
          aria-expanded={open} aria-haspopup="listbox" aria-controls={open ? menuId : undefined}
          disabled={disabled || enabled.length === 0}
          onClick={() => open ? close() : show()} onKeyDown={onTriggerKeyDown}>
          <span id={valueId}>{displayLabel ?? placeholder}</span>
        </button>
        <span className="mds-sel-caret" aria-hidden="true">▾</span>
        {open && (
          <div id={menuId} className="mds-sel-menu" role="listbox"
            aria-label={ariaLabel || label || "Options"} aria-labelledby={ariaLabelledBy}
            onKeyDown={onMenuKeyDown}>
            {options.map((option, index) => {
              if (option === "---") return <div key={index} className="mds-sel-divider" aria-hidden="true" />;
              const optionValue = option.value ?? option;
              return <div key={index} role="option" aria-selected={optionValue === value} aria-disabled={option.disabled || undefined}
                tabIndex={option.disabled ? -1 : active === index ? 0 : -1}
                ref={(element) => { if (element) optionRefs.current.set(index, element); else optionRefs.current.delete(index); }}
                className={`mds-sel-opt${optionValue === value ? " mds-sel-opt--active" : ""}`}
                onFocus={() => setActive(index)} onClick={() => choose(index)}>
                {option.label ?? option}
              </div>;
            })}
          </div>
        )}
      </div>
    </div>
  );
}
