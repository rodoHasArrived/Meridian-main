// Meridian FormRow + FormGrid — layout primitives for forms in modals and settings panels.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-form-row{display:flex;flex-direction:column;gap:5px;}
.mds-form-row--horizontal{flex-direction:row;align-items:baseline;gap:12px;}
.mds-form-row__label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#6E7781);}
.mds-form-row--horizontal .mds-form-row__label{flex:0 0 120px;text-align:right;padding-top:9px;}
.mds-form-row__field{flex:1;min-width:0;}
.mds-form-row__hint{font-family:var(--font-body);font-size:11px;color:var(--text-muted,#6E7781);}
.mds-form-row__error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#983244);}
.mds-form-grid{display:grid;gap:14px 20px;}
.mds-form-grid--1{grid-template-columns:1fr;}
.mds-form-grid--2{grid-template-columns:1fr 1fr;}
.mds-form-grid--3{grid-template-columns:1fr 1fr 1fr;}
.mds-form-grid__span-2{grid-column:span 2;}
.mds-form-grid__span-3{grid-column:span 3;}
.mds-form-divider{height:1px;background:var(--border,#D7DCE2);margin:8px 0;}
.mds-form-section-label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-muted,#6E7781);
  padding-bottom:8px;border-bottom:1px solid var(--border,#D7DCE2);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","form");
  el.textContent = css;
  document.head.appendChild(el);
}

export function FormRow({ label, hint, error, horizontal = false, children, span }) {
  inject();
  return (
    <div className={`mds-form-row${horizontal ? " mds-form-row--horizontal" : ""}${span ? ` mds-form-grid__span-${span}` : ""}`}>
      {label && <span className="mds-form-row__label">{label}</span>}
      <div className="mds-form-row__field">{children}</div>
      {error && <span className="mds-form-row__error">{error}</span>}
      {hint && !error && <span className="mds-form-row__hint">{hint}</span>}
    </div>
  );
}

export function FormGrid({ cols = 2, children }) {
  inject();
  return (
    <div className={`mds-form-grid mds-form-grid--${Math.min(3, Math.max(1, cols))}`}>
      {children}
    </div>
  );
}

export function FormDivider() {
  inject();
  return <div className="mds-form-divider" />;
}

export function FormSectionLabel({ children }) {
  inject();
  return <div className="mds-form-section-label">{children}</div>;
}

// Alias so the bundler finds a named "Form" export from Form.jsx
export const Form = FormGrid;
