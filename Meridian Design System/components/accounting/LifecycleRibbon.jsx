// Meridian lifecycle ribbon — the investment-event lifecycle made visible.
// One event moves through capture → accounting → settlement → custody → income
// → valuation → reporting; not every event uses every stage, but the grammar
// is the same across bonds, equities, funds, corporate actions, and
// impairments. `LifecycleLanes` is the seasoned-operator expansion: independent
// transaction / position / cash / income / book-value / reporting lanes on one
// timeline.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-lcr{display:flex;align-items:flex-start;width:100%;font-family:var(--font-body);}
.mds-lcr__stage{flex:1 1 0;min-width:0;display:flex;flex-direction:column;align-items:center;
  gap:6px;position:relative;background:none;border:0;padding:0 4px;font:inherit;text-align:center;}
button.mds-lcr__stage{cursor:pointer;}
button.mds-lcr__stage:focus-visible{outline:2px solid var(--border-focus);outline-offset:2px;}
.mds-lcr__rail{position:absolute;top:7px;height:1px;background:var(--border-strong);left:50%;width:100%;}
.mds-lcr__stage:last-child .mds-lcr__rail{display:none;}
.mds-lcr__dot{width:15px;height:15px;border-radius:50%;border:1.5px solid var(--border-strong);
  background:var(--bg-light);position:relative;z-index:1;display:flex;align-items:center;
  justify-content:center;font-size:9px;line-height:1;color:var(--text-on-accent);}
.mds-lcr__stage--done .mds-lcr__dot{background:var(--cond-confirmed-fg);border-color:var(--cond-confirmed-fg);}
.mds-lcr__stage--current .mds-lcr__dot{background:var(--interaction-selected,#496C8F);border-color:var(--interaction-selected,#496C8F);
  box-shadow:0 0 0 3px color-mix(in srgb,var(--brand-signal,#C56343) 18%,transparent);}
.mds-lcr__stage--blocked .mds-lcr__dot{background:var(--cond-exception-fg);border-color:var(--cond-exception-fg);}
.mds-lcr__stage--skipped .mds-lcr__dot{border-style:dashed;}
.mds-lcr__lbl{font-size:11.5px;font-weight:var(--weight-medium,500);color:var(--text-primary);line-height:14px;}
.mds-lcr__stage--pending .mds-lcr__lbl,.mds-lcr__stage--skipped .mds-lcr__lbl{color:var(--text-muted);font-weight:400;}
.mds-lcr__meta{font-family:var(--font-data);font-size:10px;color:var(--text-muted);line-height:13px;}
.mds-lcr__stage--sel .mds-lcr__lbl{color:var(--interaction-selected,#496C8F);}
.mds-lanes{display:grid;grid-template-columns:auto 1fr;gap:0 14px;width:100%;font-family:var(--font-body);}
.mds-lanes__n{font-size:var(--type-label,10px);font-weight:var(--weight-semibold,600);
  font-variant:all-small-caps;letter-spacing:.06em;color:var(--text-muted);padding:8px 0;
  border-bottom:1px solid var(--border-divider);white-space:nowrap;display:flex;align-items:center;}
.mds-lanes__track{display:flex;align-items:center;gap:0;padding:8px 0;border-bottom:1px solid var(--border-divider);
  min-width:0;}
.mds-lanes__n:last-of-type,.mds-lanes__track:last-of-type{border-bottom:0;}
.mds-lanes__step{flex:1 1 0;min-width:0;display:flex;align-items:center;gap:6px;}
.mds-lanes__mark{width:8px;height:8px;flex:0 0 auto;border-radius:50%;border:1px solid var(--border-strong);
  background:var(--bg-light);}
.mds-lanes__step--done .mds-lanes__mark{background:var(--cond-confirmed-fg);border-color:var(--cond-confirmed-fg);}
.mds-lanes__step--current .mds-lanes__mark{background:var(--interaction-selected,#496C8F);border-color:var(--interaction-selected,#496C8F);}
.mds-lanes__step--exception .mds-lanes__mark{background:var(--cond-exception-fg);border-color:var(--cond-exception-fg);}
.mds-lanes__st{font-size:11px;color:var(--text-secondary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-lanes__step--pending .mds-lanes__st{color:var(--text-muted);}
.mds-lanes__dash{flex:1;height:1px;background:var(--border);min-width:8px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "lifecycle-ribbon");
  el.textContent = css;
  document.head.appendChild(el);
}

export function LifecycleRibbon({ stages = [], selectedId, onSelect, className = "", ...rest }) {
  inject();
  return (
    <div className={`mds-lcr${className ? " " + className : ""}`} role="list" aria-label="Event lifecycle" {...rest}>
      {stages.map((s) => {
        const state = s.state || "pending";
        const Tag = onSelect ? "button" : "div";
        const extra = onSelect ? { type: "button", onClick: () => onSelect(s.id), "aria-pressed": selectedId === s.id } : {};
        return (
          <Tag
            key={s.id}
            role="listitem"
            className={`mds-lcr__stage mds-lcr__stage--${state}${selectedId === s.id ? " mds-lcr__stage--sel" : ""}`}
            {...extra}
          >
            <span className="mds-lcr__rail" aria-hidden="true" />
            <span className="mds-lcr__dot" aria-hidden="true">{state === "done" ? "✓" : ""}</span>
            <span className="mds-lcr__lbl">{s.label}</span>
            {s.meta ? <span className="mds-lcr__meta">{s.meta}</span> : null}
            <span style={{ position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0 0 0 0)" }}>{state}</span>
          </Tag>
        );
      })}
    </div>
  );
}

export function LifecycleLanes({ lanes = [], className = "", ...rest }) {
  inject();
  return (
    <div className={`mds-lanes${className ? " " + className : ""}`} {...rest}>
      {lanes.map((lane) => (
        <React.Fragment key={lane.label}>
          <div className="mds-lanes__n">{lane.label}</div>
          <div className="mds-lanes__track">
            {lane.steps.map((st, i) => (
              <div className={`mds-lanes__step mds-lanes__step--${st.state || "pending"}`} key={st.label + i}>
                <span className="mds-lanes__mark" aria-hidden="true" />
                <span className="mds-lanes__st">{st.label}</span>
                {i < lane.steps.length - 1 ? <span className="mds-lanes__dash" aria-hidden="true" /> : null}
              </div>
            ))}
          </div>
        </React.Fragment>
      ))}
    </div>
  );
}
