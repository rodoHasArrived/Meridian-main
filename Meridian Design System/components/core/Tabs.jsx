// Meridian Tabs + TabPanel — standard tabbed section. Terracotta bottom-border active indicator.
// Full tab keyboard contract: roving tabindex, Left/Right/Home/End (disabled tabs skipped),
// wired tab↔panel ids. Controlled via `value` or uncontrolled via `defaultTab`.
// A11y (3.5): aria-controls is set only when the panel actually renders — a tabs-array used as a
// bare switcher (no panel children) no longer emits references to ids that do not exist.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tabs{display:flex;flex-direction:column;}
.mds-tabs__bar{display:flex;gap:2px;border-bottom:1px solid var(--border,#D2CDC3);}
.mds-tabs__tab{appearance:none;border:none;background:transparent;cursor:pointer;
  font-family:var(--font-body);font-size:13px;color:var(--text-secondary,#535C65);
  padding:9px 14px 11px;border-bottom:2px solid transparent;margin-bottom:-1px;
  display:inline-flex;align-items:center;gap:7px;white-space:nowrap;}
.mds-tabs__tab:hover{color:var(--text-primary,#20252B);}
.mds-tabs__tab:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:calc(-1 * var(--focus-ring-offset,1px));}
.mds-tabs__tab--active{color:var(--text-primary,#20252B);font-weight:600;
  border-bottom-color:var(--brand-signal,#C56343);}
.mds-tabs__tab:disabled{cursor:not-allowed;color:var(--text-disabled,#A4ABB2);}
.mds-tabs__count{font-family:var(--font-data);font-size:10px;color:var(--text-muted,#6B737B);
  border:1px solid var(--border,#D2CDC3);padding:0 5px;line-height:1.6;}
.mds-tabs__panel{display:none;}
.mds-tabs__panel--active{display:block;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","tabs");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Tabs({ tabs = [], value, defaultTab = 0, onChange, children }) {
  inject();
  const uid = React.useId();
  const [internal, setInternal] = React.useState(defaultTab);
  const controlled = value != null;
  const active = controlled ? value : internal;
  const barRef = React.useRef(null);

  const select = (i) => {
    if (!controlled) setInternal(i);
    onChange?.(i, tabs[i]);
  };

  const isDisabled = (i) => !!(tabs[i] && tabs[i].disabled);

  // Roving focus: arrows move + select (selection follows focus), skipping disabled tabs.
  const handleKey = (e, i) => {
    const n = tabs.length;
    if (!n) return;
    let next = null;
    const step = (from, dir) => {
      let j = from;
      do { j = (j + dir + n) % n; } while (isDisabled(j) && j !== from);
      return isDisabled(j) ? from : j;
    };
    switch (e.key) {
      case "ArrowRight": next = step(i, 1); break;
      case "ArrowLeft": next = step(i, -1); break;
      case "Home": next = isDisabled(0) ? step(0, 1) : 0; break;
      case "End": next = isDisabled(n - 1) ? step(n - 1, -1) : n - 1; break;
      default: return;
    }
    e.preventDefault();
    select(next);
    barRef.current?.querySelector(`[data-idx="${next}"]`)?.focus();
  };

  // Support both children-as-panels and tabs-array mode
  const panels = React.Children.toArray(children);

  return (
    <div className="mds-tabs">
      <div className="mds-tabs__bar" role="tablist" ref={barRef}>
        {tabs.map((tab, i) => {
          const label = tab.label ?? tab;
          const count = tab.count;
          return (
            <button
              key={i}
              type="button"
              role="tab"
              id={`${uid}-tab-${i}`}
              data-idx={i}
              aria-selected={active === i}
              aria-controls={panels[i] ? `${uid}-panel-${i}` : undefined}
              tabIndex={active === i ? 0 : -1}
              className={`mds-tabs__tab${active === i ? " mds-tabs__tab--active" : ""}`}
              onClick={() => select(i)}
              onKeyDown={(e) => handleKey(e, i)}
              disabled={isDisabled(i)}
            >
              {label}
              {count != null && <span className="mds-tabs__count">{count}</span>}
            </button>
          );
        })}
      </div>
      {panels.map((panel, i) => (
        <div key={i} role="tabpanel" id={`${uid}-panel-${i}`} aria-labelledby={`${uid}-tab-${i}`}
          className={`mds-tabs__panel${active === i ? " mds-tabs__panel--active" : ""}`}>
          {panel}
        </div>
      ))}
    </div>
  );
}

export function TabPanel({ children }) {
  // Thin wrapper so consumers can write <TabPanel> content </TabPanel>
  return <>{children}</>;
}
