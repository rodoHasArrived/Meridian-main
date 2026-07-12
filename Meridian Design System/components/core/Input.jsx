// Meridian Input — functional only. No radius, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-field{display:block;}
.mds-field__label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);margin-bottom:5px;}
.mds-input{width:100%;box-sizing:border-box;height:32px;padding:7px 10px;
  border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#fff);
  color:var(--text-primary,#22272E);font-family:var(--font-data);font-size:13px;}
.mds-input::placeholder{color:var(--text-disabled,#889099);}
.mds-input:hover{border-color:var(--border-hover,#B8C2CC);}
.mds-input:focus{border-color:var(--border-focus,#2F6F8F);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-input:disabled{background:var(--bg-medium,#F5F7FA);border-color:var(--border,#D7DCE2);color:var(--text-disabled,#889099);opacity:.6;cursor:not-allowed;}
.mds-input--error{border-color:var(--red,#BA3F55);}
.mds-field__error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#8C2F40);margin-top:5px;}
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
