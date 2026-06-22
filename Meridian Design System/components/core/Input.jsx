// Meridian text input — light institutional field on white paper, hairline border,
// teal-blue focus ring. Mono values (ids, symbols, quantities). Optional label + error.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-field{display:block;}
.mds-field__label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#6E7781);margin-bottom:5px;}
.mds-input{width:100%;box-sizing:border-box;height:34px;padding:7px 10px;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-button,6px);
  background:var(--bg-light,#fff);color:var(--text-primary,#22272E);
  font-family:var(--font-data);font-size:13px;
  transition:border-color .12s ease,box-shadow .12s ease;}
.mds-input::placeholder{color:var(--text-disabled,#9AA4AF);}
.mds-input:hover{border-color:var(--border-hover,#B8C2CC);}
.mds-input:focus{outline:none;border-color:var(--border-focus,#2F6F8F);
  box-shadow:0 0 0 3px rgba(47,111,143,.20), inset 0 0 0 1px rgba(47,111,143,.15);}
.mds-input:disabled{opacity:.5;cursor:not-allowed;background:var(--bg-medium,#F5F7FA);}
.mds-input--error{border-color:var(--red,#BA3F55);}
.mds-input--error:focus{box-shadow:0 0 0 2px rgba(186,63,85,.20);}
.mds-field__error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#983244);margin-top:5px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "input");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Input({ label, error, className = "", ...rest }) {
  inject();
  const input = (
    <input
      className={`mds-input${error ? " mds-input--error" : ""}${className ? " " + className : ""}`}
      aria-invalid={error ? true : undefined}
      {...rest}
    />
  );
  if (!label && !error) return input;
  return (
    <label className="mds-field">
      {label && <span className="mds-field__label">{label}</span>}
      {input}
      {error && <span className="mds-field__error">{error}</span>}
    </label>
  );
}
