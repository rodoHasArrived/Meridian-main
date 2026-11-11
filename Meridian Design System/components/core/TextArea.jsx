// Meridian TextArea — multi-line text input, functional only.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-textarea-field{display:block;}
.mds-textarea-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);margin-bottom:5px;}
.mds-textarea{width:100%;box-sizing:border-box;padding:7px 10px;
  border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  color:var(--text-primary,#20252B);font-family:var(--font-body);font-size:13px;
  line-height:1.4;resize:vertical;min-height:100px;}
.mds-textarea::placeholder{color:var(--text-disabled,#A4ABB2);}
.mds-textarea:hover{border-color:var(--border-hover,#BEB8AD);}
.mds-textarea:focus{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-textarea:disabled{opacity:.5;cursor:not-allowed;background:var(--bg-medium,#EBE8E1);}
.mds-textarea--error{border-color:var(--red,#B54B42);}
.mds-textarea-error{font-family:var(--font-body);font-size:11px;color:var(--red-dim,#883832);margin-top:5px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "textarea");
  el.textContent = css;
  document.head.appendChild(el);
}

export function TextArea({ label, error, rows = 4, className = "", ...rest }) {
  inject();
  const textarea = (
    <textarea
      className={`mds-textarea${error ? " mds-textarea--error" : ""}${className ? " " + className : ""}`}
      rows={rows}
      aria-invalid={error ? true : undefined}
      {...rest}
    />
  );
  if (!label && !error) return textarea;
  return (
    <label className="mds-textarea-field">
      {label && <span className="mds-textarea-label">{label}</span>}
      {textarea}
      {error && <span className="mds-textarea-error">{error}</span>}
    </label>
  );
}
