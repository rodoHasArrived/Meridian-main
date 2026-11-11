// Meridian ReturnPath — the way back out of an object (docs/SUITE_COHESION_4_4.md › E-2).
//
// Handoffs in the suite were one-way: reconciliation → accounting, close → tie-out. The returns
// were never built, so a journal entry could not open the break that caused it and an account
// sheet could not open its own reconciliation. Operators work in loops, not chains — this is the
// component that closes them.
//
// Feed it `templates/_scaffold/app-map.js`'s loops for the object in hand:
//   <ReturnPath loops={MeridianAppMap.loops("entry", entry)} />
// A loop only appears when the object actually carries the link, so an unresolved break offers no
// "entry that resolved this" — the affordance is never a dead promise.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-return{display:flex;flex-wrap:wrap;align-items:center;gap:6px;font-family:var(--font-body);}
.mds-return__label{font-family:var(--font-data);font-size:9.5px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-muted,#6B737B);
  margin-right:2px;white-space:nowrap;}
.mds-return__btn{appearance:none;display:inline-flex;align-items:center;gap:6px;cursor:pointer;
  padding:3px 9px 3px 7px;border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);
  background:var(--bg-light,#F8F7F3);font-family:var(--font-body);font-size:11.5px;
  color:var(--text-secondary,#535C65);text-align:left;text-decoration:none;transition:border-color .1s ease,background-color .1s ease;}
.mds-return__btn:hover{border-color:var(--brand-signal,#C56343);background:var(--brand-signal-ghost,#F6E6DF);
  color:var(--text-primary,#20252B);}
.mds-return__btn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-return__arrow{color:var(--brand-signal,#C56343);font-size:11px;line-height:1;flex:none;}
.mds-return__id{font-family:var(--font-data);font-size:10.5px;color:var(--text-muted,#6B737B);
  font-variant-numeric:tabular-nums;}
.mds-return--stack{flex-direction:column;align-items:stretch;gap:4px;}
.mds-return--stack .mds-return__btn{width:100%;}
.mds-return__empty{font-size:11.5px;color:var(--text-muted,#6B737B);font-style:italic;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "returnpath");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * `loops` — [{ to, id, verb, href?, open? }] straight from `MeridianAppMap.loops(type, obj)`.
 * Renders nothing when there are none: an object with no way back should show no control, not an
 * empty row (pass `emptyText` if the absence itself is worth stating).
 */
export function ReturnPath({ loops = [], label = "Back to", layout = "inline", emptyText, showIds = true }) {
  inject();
  if (!loops.length) {
    return emptyText ? <div className="mds-return"><span className="mds-return__empty">{emptyText}</span></div> : null;
  }
  return (
    <div className={`mds-return${layout === "stack" ? " mds-return--stack" : ""}`}>
      {label ? <span className="mds-return__label">{label}</span> : null}
      {loops.map((l, i) => {
        const body = (
          <React.Fragment>
            <span className="mds-return__arrow" aria-hidden="true">↩</span>
            <span>{l.verb}</span>
            {showIds && l.id != null ? <span className="mds-return__id">{l.id}</span> : null}
          </React.Fragment>
        );
        // A real href when the loop has one, so middle-click and "open in new tab" work — the
        // whole point of carrying object identity in the URL.
        return l.href
          ? <a key={l.to + i} className="mds-return__btn" href={l.href}
               onClick={l.open ? (e) => { if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return; e.preventDefault(); l.open(); } : undefined}>{body}</a>
          : <button key={l.to + i} type="button" className="mds-return__btn" onClick={l.open}>{body}</button>;
      })}
    </div>
  );
}
