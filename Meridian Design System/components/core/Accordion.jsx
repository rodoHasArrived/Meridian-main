// Meridian Accordion — collapsible sections. Single or multi-open.
// Functional only: no radius, no shadows, minimal motion.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-acc{border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#FAFBFC);}
.mds-acc-item + .mds-acc-item{border-top:1px solid var(--border-divider,#E5E9EE);}
.mds-acc-hd{display:flex;align-items:center;gap:10px;width:100%;padding:12px 14px;
  background:transparent;border:none;cursor:pointer;text-align:left;
  font-family:var(--font-body);font-size:13px;font-weight:600;color:var(--text-primary,#22272E);}
.mds-acc-hd:hover{background:var(--bg-hover,#F1F4F7);}
.mds-acc-hd:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.mds-acc-caret{flex:0 0 auto;color:var(--text-muted,#59636F);font-size:11px;
  transition:transform 120ms ease;}
.mds-acc-caret--open{transform:rotate(90deg);}
.mds-acc-title{flex:1;}
.mds-acc-badge{flex:0 0 auto;font-family:var(--font-data);font-size:11px;font-weight:500;
  color:var(--text-muted,#59636F);font-variant-numeric:tabular-nums;}
.mds-acc-body{padding:0 14px 14px 34px;font-family:var(--font-body);font-size:13px;
  line-height:20px;color:var(--text-secondary,#4D5967);}
@media (prefers-reduced-motion: reduce){.mds-acc-caret{transition:none;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "accordion");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Accordion({ items = [], multi = false, defaultOpen = [] }) {
  inject();
  const [open, setOpen] = React.useState(() => new Set(defaultOpen));
  const toggle = (i) => setOpen((prev) => {
    const next = multi ? new Set(prev) : new Set();
    prev.has(i) ? next.delete(i) : next.add(i);
    return next;
  });

  return (
    <div className="mds-acc">
      {items.map((item, i) => {
        const isOpen = open.has(i);
        return (
          <div key={i} className="mds-acc-item">
            <button
              type="button"
              className="mds-acc-hd"
              aria-expanded={isOpen}
              onClick={() => toggle(i)}
            >
              <span className={`mds-acc-caret${isOpen ? " mds-acc-caret--open" : ""}`}>▸</span>
              <span className="mds-acc-title">{item.title}</span>
              {item.badge != null && <span className="mds-acc-badge">{item.badge}</span>}
            </button>
            {isOpen && <div className="mds-acc-body">{item.content}</div>}
          </div>
        );
      })}
    </div>
  );
}

Accordion.displayName = "Accordion";
