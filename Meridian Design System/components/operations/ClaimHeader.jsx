// Meridian claim header — step 1 and 4 of the reading-path contract on one
// element: a testable claim (not a topic label), the caveat/purpose line, and
// the state chips that qualify it. Controls belong in `children`, adjacent to
// the evidence they change.
import React from "react";
import { conditionMeta } from "./conditions";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-claim{display:flex;align-items:flex-end;gap:20px;flex-wrap:wrap;width:100%;
  font-family:var(--font-body);padding:0 0 12px;border-bottom:1px solid var(--border);}
.mds-claim--flush{border-bottom:0;padding-bottom:0;}
.mds-claim__main{flex:1 1 420px;min-width:0;}
.mds-claim__ctx{display:flex;align-items:center;gap:7px;font-family:var(--font-data);
  font-size:var(--type-label,10px);font-weight:var(--weight-semibold,600);font-variant:all-small-caps;
  letter-spacing:.06em;color:var(--text-muted);margin-bottom:6px;flex-wrap:wrap;}
.mds-claim__ctx i{font-style:normal;opacity:.5;}
.mds-claim__h{font-family:var(--font-display);font-size:var(--type-page-title,24px);
  line-height:1.18;font-weight:var(--weight-semibold,600);color:var(--text-primary);margin:0;
  text-wrap:pretty;max-width:34ch;}
.mds-claim--wide .mds-claim__h{max-width:none;}
.mds-claim__note{font-size:12.5px;line-height:18px;color:var(--text-muted);margin:6px 0 0;
  max-width:64ch;text-wrap:pretty;}
.mds-claim__side{display:flex;align-items:center;gap:8px 10px;flex-wrap:wrap;justify-content:flex-end;}
.mds-claim__states{display:flex;align-items:center;gap:6px;flex-wrap:wrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "claim-header");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ClaimHeader({
  context, claim, note, states = [], wide = false, flush = false, headingLevel = 1, children, className = "", ...rest
}) {
  inject();
  // The claim is the page heading by default. A screen that already names itself with an <h1>
  // passes headingLevel={2}; two visible <h1>s left a screen-reader user without one page title.
  const Heading = "h" + (headingLevel >= 1 && headingLevel <= 6 ? Math.round(headingLevel) : 1);
  const ctx = Array.isArray(context) ? context : context ? [context] : [];
  return (
    <header
      className={`mds-claim${wide ? " mds-claim--wide" : ""}${flush ? " mds-claim--flush" : ""}${className ? " " + className : ""}`}
      {...rest}
    >
      <div className="mds-claim__main">
        {ctx.length ? (
          <div className="mds-claim__ctx">
            {ctx.map((c, i) => (
              <React.Fragment key={c + i}>
                {i ? <i aria-hidden="true">/</i> : null}
                <span>{c}</span>
              </React.Fragment>
            ))}
          </div>
        ) : null}
        <Heading className="mds-claim__h">{claim}</Heading>
        {note ? <p className="mds-claim__note">{note}</p> : null}
      </div>
      {(states.length || children) ? (
        <div className="mds-claim__side">
          {states.length ? (
            <div className="mds-claim__states">
              {states.map((s, i) => {
                const m = conditionMeta(s.condition);
                return (
                  <span
                    key={s.label || i}
                    title={m.note}
                    style={{
                      display: "inline-flex", alignItems: "center", gap: 6, minHeight: 22, padding: "2px 8px",
                      border: `1px solid var(--cond-${m.key}-bd)`, background: `var(--cond-${m.key}-bg)`,
                      borderRadius: "var(--radius-chip,2px)", fontSize: 11, color: "var(--text-secondary)",
                      whiteSpace: "nowrap",
                    }}
                  >
                    <span aria-hidden="true" style={{ fontSize: 9, color: `var(--cond-${m.key}-fg)` }}>{m.glyph}</span>
                    {s.label}
                    {s.detail ? <span style={{ fontFamily: "var(--font-data)", fontSize: 10, color: "var(--text-muted)" }}>{s.detail}</span> : null}
                  </span>
                );
              })}
            </div>
          ) : null}
          {children}
        </div>
      ) : null}
    </header>
  );
}
