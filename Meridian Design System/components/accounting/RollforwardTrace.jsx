// Meridian rollforward trace — opening balance, the movements that explain the
// change, closing balance, and the proof that they agree. Every movement can
// resolve to its calculation, journal, schedule, lot, rule, or evidence, so a
// carrying value is never an unexplained number. The component checks its own
// arithmetic and says "Out by …" when it does not tie.
import React from "react";
import { formatMoney } from "../core/format.js";
import { roundMoney } from "./money.js";
import { knownAmount, sumKnown } from "./knownAmounts.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-rf{width:100%;font-family:var(--font-body);font-size:12px;color:var(--text-secondary);}
.mds-rf__row{display:flex;align-items:baseline;gap:12px;min-height:26px;padding:3px 0;
  border-bottom:1px solid var(--border-divider);}
.mds-rf__row--open{border-bottom:1px solid var(--border-strong);}
.mds-rf__row--close{border-top:1px solid var(--border-strong);border-bottom:0;margin-top:2px;
  padding-top:7px;}
.mds-rf__lbl{flex:1;min-width:0;display:flex;align-items:baseline;gap:7px;}
.mds-rf__op{font-family:var(--font-data);color:var(--text-muted);width:10px;flex:0 0 10px;text-align:center;}
.mds-rf__txt{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-rf__row--open .mds-rf__txt,.mds-rf__row--close .mds-rf__txt{font-weight:var(--weight-semibold,600);
  color:var(--text-primary);}
.mds-rf__val{font-family:var(--font-data);font-variant-numeric:tabular-nums;text-align:right;
  color:var(--text-primary);white-space:nowrap;}
.mds-rf__row--open .mds-rf__val,.mds-rf__row--close .mds-rf__val{font-weight:var(--weight-semibold,600);}
.mds-rf__link{font:inherit;font-size:11px;background:none;border:0;padding:0;cursor:pointer;
  color:var(--link,#2A5F96);text-decoration:underline;text-underline-offset:2px;white-space:nowrap;}
.mds-rf__link:focus-visible{outline:2px solid var(--border-focus);outline-offset:2px;}
.mds-rf__note{font-family:var(--font-data);font-size:10.5px;color:var(--text-muted);}
.mds-rf__proof{display:flex;align-items:center;gap:8px;margin-top:8px;padding:5px 8px;
  border:1px solid var(--cond-confirmed-bd);background:var(--cond-confirmed-bg);
  border-radius:var(--radius-chip,2px);font-size:11px;color:var(--text-primary);}
.mds-rf__proof--off{border-color:var(--cond-exception-bd);background:var(--cond-exception-bg);
  color:var(--text-primary);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "rollforward-trace");
  el.textContent = css;
  document.head.appendChild(el);
}

export function RollforwardTrace({
  openingLabel = "Opening book value", opening = 0,
  movements = [], closingLabel = "Closing book value", closing,
  currency, decimals = 2, showProof = true, className = "", ...rest
}) {
  inject();
  const money = value => knownAmount(value)===null?"Missing":formatMoney(value, { currency, decimals, parens: true, zeroDash: true });
  const sum = sumKnown(movements.map(movement=>movement.value));
  const usable=knownAmount(opening)!==null&&sum!==null&&(closing===undefined||knownAmount(closing)!==null);
  const derived = usable?roundMoney(knownAmount(opening)+sum, decimals, "half-even"):null;
  const stated = closing===undefined?derived:knownAmount(closing);
  const diff = usable?roundMoney(stated-derived, decimals, "half-even"):null;
  const ties = usable&&Math.abs(diff)<Math.pow(10,-decimals)/2;
  return (
    <div className={`mds-rf${className ? " " + className : ""}`} {...rest}>
      <div className="mds-rf__row mds-rf__row--open">
        <span className="mds-rf__lbl"><span className="mds-rf__op" aria-hidden="true" /><span className="mds-rf__txt">{openingLabel}</span></span>
        <span className="mds-rf__val">{money(opening)}</span>
      </div>
      {movements.map((m, i) => {
        const v = knownAmount(m.value);
        const op = m.op || (v === null?"?":v === 0 ? "±" : v > 0 ? "+" : "−");
        return (
          <div className="mds-rf__row" key={m.id || m.label || i}>
            <span className="mds-rf__lbl">
              <span className="mds-rf__op" aria-hidden="true">{op}</span>
              <span className="mds-rf__txt">{m.label}</span>
              {m.note ? <span className="mds-rf__note">{m.note}</span> : null}
              {m.linkLabel && m.onOpen ? (
                <button type="button" className="mds-rf__link" onClick={m.onOpen}>{m.linkLabel}</button>
              ) : null}
            </span>
            <span className="mds-rf__val" style={m.tone ? { color: `var(--cond-${m.tone}-fg)` } : undefined}>
              {v===null?"Missing":v === 0 && m.emptyDash !== false ? "—" : money(Math.abs(v))}
            </span>
          </div>
        );
      })}
      <div className="mds-rf__row mds-rf__row--close">
        <span className="mds-rf__lbl"><span className="mds-rf__op" aria-hidden="true" /><span className="mds-rf__txt">{closingLabel}</span></span>
        <span className="mds-rf__val">{money(stated)}</span>
      </div>
      {showProof ? (
        <div className={`mds-rf__proof${ties ? "" : " mds-rf__proof--off"}`}>
          <span aria-hidden="true">{ties ? "✓" : "▲"}</span>
          {!usable?"Cannot prove rollforward — input missing; missing amounts are not zero":ties
            ? `Opening + movements = closing · ${movements.length} movements`
            : `Out by ${formatMoney(Math.abs(diff), { currency, decimals, parens: false })} — movements do not explain the closing balance`}
        </div>
      ) : null}
    </div>
  );
}
