// Meridian Input — functional only. No radius, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-field{display:block;}
.mds-field__label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);margin-bottom:5px;}
.mds-input{width:100%;box-sizing:border-box;height:32px;padding:7px 10px;
  border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  color:var(--text-primary,#20252B);font-family:var(--font-data);font-size:13px;}
.mds-input::placeholder{color:var(--text-disabled,#A4ABB2);}
.mds-input:hover{border-color:var(--border-hover,#BEB8AD);}
.mds-input:focus{border-color:var(--border-focus,#2D6FB3);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-input:disabled{background:var(--bg-medium,#EBE8E1);border-color:var(--border,#D2CDC3);color:var(--text-disabled,#A4ABB2);opacity:.6;cursor:not-allowed;}
.mds-input--error{border-color:var(--red,#B54B42);}
.mds-field__error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#883832);margin-top:5px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "input");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Input({ label, error, className = "", ...rest }) {
  inject();
  const generatedId = React.useId();
  const inputId = rest.id || generatedId;
  const errorId = `${inputId}--error`;
  const describedBy = [rest["aria-describedby"], error ? errorId : null].filter(Boolean).join(" ") || undefined;
  const input = (
    <input
      className={`mds-input${error ? " mds-input--error" : ""}${className ? " " + className : ""}`}
      {...rest}
      id={inputId}
      aria-invalid={error ? true : rest["aria-invalid"]}
      aria-describedby={describedBy}
    />
  );
  if (!label && !error) return input;
  return (
    <label className="mds-field">
      {label && <span className="mds-field__label">{label}</span>}
      {input}
      {error && <span className="mds-field__error" id={errorId}>{error}</span>}
    </label>
  );
}
