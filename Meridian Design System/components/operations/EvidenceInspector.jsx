// Meridian evidence inspector — the synchronized right rail of an analytical
// workspace. It answers "what is selected, where did it come from, how certain
// is it, and what do I do next" without restating the primary view.
// Compose: <EvidenceInspector> selected block → <EvidenceSection> field lists,
// data-state contract, model range → <ProvenanceTrace> source-to-output chain.
import React from "react";
import { conditionMeta } from "./conditions";
import { ValueEnvelope } from "./ValueEnvelope";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-insp{display:flex;flex-direction:column;background:var(--bg-light);border:1px solid var(--border);
  border-radius:var(--radius-card,4px);font-family:var(--font-body);color:var(--text-secondary);
  width:100%;max-width:100%;overflow:hidden;}
.mds-insp__head{padding:12px 14px 12px;border-bottom:1px solid var(--border-divider);}
.mds-insp__eyebrow{font-size:var(--type-label,10px);font-weight:var(--weight-semibold,600);
  font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-muted);margin-bottom:8px;}
.mds-insp__sel{border-left:var(--inspector-rail,4px) solid var(--viz-select-rail);
  background:var(--viz-select-surface);padding:10px 12px;}
.mds-insp__val{font-family:var(--font-data);font-size:22px;font-weight:var(--weight-semibold,600);
  color:var(--text-primary);line-height:1.1;font-variant-numeric:tabular-nums;}
.mds-insp__cap{font-family:var(--font-data);font-size:11px;color:var(--text-secondary);margin-top:5px;}
.mds-insp__empty{padding:10px 12px;border:1px dashed var(--border);border-radius:var(--radius-chip,2px);
  font-size:12px;color:var(--text-muted);}
.mds-insp__body{display:flex;flex-direction:column;}
.mds-sec{padding:12px 14px;border-bottom:1px solid var(--border-divider);}
.mds-sec:last-child{border-bottom:0;}
.mds-sec__t{display:flex;align-items:center;gap:8px;font-size:var(--type-label,10px);
  font-weight:var(--weight-semibold,600);font-variant:all-small-caps;letter-spacing:.05em;
  color:var(--text-muted);margin-bottom:8px;}
.mds-sec__t button{margin-left:auto;font:inherit;font-size:10px;color:var(--brand-signal,#C56343);background:none;
  border:0;padding:0;cursor:pointer;text-transform:none;letter-spacing:0;}
.mds-sec__note{font-size:11px;line-height:15px;color:var(--text-muted);margin-top:8px;text-wrap:pretty;}
.mds-fields{display:grid;grid-template-columns:auto 1fr;gap:5px 12px;align-items:baseline;}
.mds-fields__k{font-size:12px;color:var(--text-muted);white-space:nowrap;}
.mds-fields__v{font-family:var(--font-data);font-size:12px;color:var(--text-primary);text-align:right;
  font-variant-numeric:tabular-nums;overflow-wrap:anywhere;}
.mds-trace{display:flex;flex-direction:column;}
.mds-trace__step{display:grid;grid-template-columns:16px 1fr;gap:8px;padding:0 0 12px;position:relative;}
.mds-trace__step:last-child{padding-bottom:0;}
.mds-trace__step:last-child .mds-trace__line{display:none;}
.mds-trace__mark{width:11px;height:11px;border-radius:50%;border:1.5px solid var(--series-prior);
  background:var(--bg-light);margin-top:2px;justify-self:center;}
.mds-trace__step--done .mds-trace__mark{background:var(--series-prior);}
.mds-trace__step--current .mds-trace__mark{border-color:var(--interaction-selected,#496C8F);background:var(--interaction-selected,#496C8F);
  box-shadow:0 0 0 3px color-mix(in srgb,var(--brand-signal,#C56343) 18%,transparent);}
.mds-trace__line{position:absolute;left:7.5px;top:15px;bottom:-1px;width:1px;background:var(--border);}
.mds-trace__lbl{font-size:12px;font-weight:var(--weight-medium,500);color:var(--text-primary);}
.mds-trace__meta{font-family:var(--font-data);font-size:10.5px;color:var(--text-muted);margin-top:2px;}
button.mds-trace__body{text-align:left;background:none;border:0;padding:0;cursor:pointer;font:inherit;width:100%;}
button.mds-trace__body:hover .mds-trace__lbl{color:var(--brand-signal,#C56343);}
button.mds-trace__body:focus-visible{outline:2px solid var(--border-focus);outline-offset:2px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "evidence-inspector");
  el.textContent = css;
  document.head.appendChild(el);
}

export function EvidenceInspector({
  eyebrow = "Selected observation", value, caption, condition, emptyMessage = "Select a datum to inspect its evidence.",
  children, className = "", ...rest
}) {
  inject();
  const m = condition ? conditionMeta(condition) : null;
  return (
    <aside className={`mds-insp${className ? " " + className : ""}`} aria-label="Evidence inspector" {...rest}>
      <div className="mds-insp__head">
        <div className="mds-insp__eyebrow">{eyebrow}</div>
        {value !== null && value !== undefined && value !== "" || m?.key === "restricted" ? (
          <div className="mds-insp__sel">
            <div className="mds-insp__val">{m?.key === "restricted" ? "Restricted" : m?.key === "missing" ? "Missing" : m?.key === "na" ? "Not applicable" : value}</div>
            {caption && m?.key !== "restricted" ? <div className="mds-insp__cap">{caption}</div> : null}
            {m ? <div className="mds-insp__cap" style={{ color: "var(--text-secondary)" }}>{m.glyph} {m.label}</div> : null}
          </div>
        ) : (
          <div className="mds-insp__empty">{emptyMessage}</div>
        )}
      </div>
      <div className="mds-insp__body">{children}</div>
    </aside>
  );
}

export function EvidenceSection({ title, action, onAction, fields, note, children, className = "", ...rest }) {
  inject();
  // A field figure with any facet renders through ValueEnvelope, so the condition arrives as a
  // glyph with a title rather than a tint alone (VALUE_ENVELOPE rule 4). `tone` is the legacy
  // one-facet spelling and still works — it becomes the envelope's condition.
  const fieldEnvelope = (f) => f.envelope || (f.tone ? { condition: f.tone } : null);
  return (
    <section className={`mds-sec${className ? " " + className : ""}`} {...rest}>
      {title ? (
        <h3 className="mds-sec__t">
          {title}
          {action && onAction ? <button type="button" onClick={onAction}>{action}</button> : null}
        </h3>
      ) : null}
      {fields ? (
        <div className="mds-fields">
          {fields.map((f, i) => (
            <React.Fragment key={f.label || i}>
              <span className="mds-fields__k">{f.label}</span>
              <span className="mds-fields__v">
                {fieldEnvelope(f)
                  ? <ValueEnvelope {...fieldEnvelope(f)} value={f.value} align="right"
                      label={fieldEnvelope(f).label || f.label} />
                  : f.value}
              </span>
            </React.Fragment>
          ))}
        </div>
      ) : null}
      {children}
      {note ? <p className="mds-sec__note">{note}</p> : null}
    </section>
  );
}

export function ProvenanceTrace({ steps = [], selectedId, onSelect, className = "", ...rest }) {
  inject();
  return (
    <ol className={`mds-trace${className ? " " + className : ""}`} style={{ listStyle: "none", margin: 0, padding: 0 }} {...rest}>
      {steps.map((s, i) => {
        const id = s.id || s.label || i;
        const state = s.state || (selectedId && id === selectedId ? "current" : "done");
        const body = (
          <>
            <div className="mds-trace__lbl">{s.label}</div>
            {s.meta ? <div className="mds-trace__meta">{s.meta}</div> : null}
          </>
        );
        return (
          <li className={`mds-trace__step mds-trace__step--${state}`} key={id}>
            <span className="mds-trace__line" aria-hidden="true" />
            <span className="mds-trace__mark" aria-hidden="true" />
            {onSelect ? (
              <button type="button" className="mds-trace__body" onClick={() => onSelect(id)}>{body}</button>
            ) : (
              <div>{body}</div>
            )}
          </li>
        );
      })}
    </ol>
  );
}
