// Meridian Input — functional only. No radius, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-field{display:block;}
.mds-field__label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#5E666F);margin-bottom:5px;}
.mds-input{width:100%;box-sizing:border-box;height:32px;padding:7px 10px;
  border:1px solid var(--border,#E4E3DE);background:var(--bg-light,#FBFAF8);
  color:var(--text-primary,#22252A);font-family:var(--font-data);font-size:13px;}
.mds-input::placeholder{color:var(--text-disabled,#94999F);}
.mds-input:hover{border-color:var(--border-hover,#C6C3BB);}
.mds-input:focus{border-color:var(--border-focus,#A85436);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-input:disabled{background:var(--bg-medium,#EDEAE4);border-color:var(--border,#E4E3DE);color:var(--text-disabled,#94999F);opacity:.6;cursor:not-allowed;}
.mds-input--error{border-color:var(--red,#A8443C);}
.mds-field__error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#7E332D);margin-top:5px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "input");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Input({ label, error, className = "", ...rest }) {
  inject();
  const errorId = rest.id ? `${rest.id}--error` : undefined;
  const input = (
    <input
      className={`mds-input${error ? " mds-input--error" : ""}${className ? " " + className : ""}`}
      aria-invalid={error ? true : undefined}
      aria-describedby={error && errorId ? errorId : undefined}
      {...rest}
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
