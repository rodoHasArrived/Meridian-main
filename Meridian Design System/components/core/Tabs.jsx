// Meridian Tabs + TabPanel — standard tabbed section. Teal-blue bottom-border active indicator.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tabs{display:flex;flex-direction:column;}
.mds-tabs__bar{display:flex;gap:2px;border-bottom:1px solid var(--border,#D7DCE2);}
.mds-tabs__tab{appearance:none;border:none;background:transparent;cursor:pointer;
  font-family:var(--font-body);font-size:13px;color:var(--text-secondary,#4D5967);
  padding:9px 14px 11px;border-bottom:2px solid transparent;margin-bottom:-1px;
  display:inline-flex;align-items:center;gap:7px;white-space:nowrap;
  transition:color .12s,border-color .12s;}
.mds-tabs__tab:hover{color:var(--text-primary,#22272E);}
.mds-tabs__tab--active{color:var(--text-primary,#22272E);font-weight:600;
  border-bottom-color:var(--accent,#2F6F8F);}
.mds-tabs__tab:disabled{opacity:.45;cursor:not-allowed;}
.mds-tabs__count{font-family:var(--font-data);font-size:10px;color:var(--text-muted,#6E7781);
  border:1px solid var(--border,#D7DCE2);border-radius:3px;padding:0 5px;line-height:1.6;}
.mds-tabs__panel{display:none;}
.mds-tabs__panel--active{display:block;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","tabs");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Tabs({ tabs = [], defaultTab = 0, onChange, children }) {
  inject();
  const [active, setActive] = React.useState(defaultTab);

  const handle = (i) => {
    setActive(i);
    onChange?.(i, tabs[i]);
  };

  // Support both children-as-panels and tabs-array mode
  const panels = React.Children.toArray(children);

  return (
    <div className="mds-tabs">
      <div className="mds-tabs__bar" role="tablist">
        {tabs.map((tab, i) => {
          const label = tab.label ?? tab;
          const count = tab.count;
          const disabled = tab.disabled;
          return (
            <button
              key={i}
              role="tab"
              aria-selected={active === i}
              className={`mds-tabs__tab${active === i ? " mds-tabs__tab--active" : ""}`}
              onClick={() => handle(i)}
              disabled={disabled}
            >
              {label}
              {count != null && <span className="mds-tabs__count">{count}</span>}
            </button>
          );
        })}
      </div>
      {panels.map((panel, i) => (
        <div key={i} role="tabpanel"
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
