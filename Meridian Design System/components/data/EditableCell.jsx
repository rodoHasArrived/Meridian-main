// Meridian EditableCell — inline editable field with validation. Click to edit, Tab/Enter to
// commit, Escape to cancel. Error state shown as a red border (no separate error message).
// Supports text, number, currency, and custom validators. Used in tables for fast triage.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-editable{position:relative;display:inline-block;width:100%;}
.mds-editable__display{padding:4px 6px;cursor:pointer;user-select:none;
  border:1px solid transparent;border-radius:2px;
  transition:background-color 100ms ease;}
.mds-editable__display:hover{background:var(--bg-hover,#EDEFF0);}
.mds-editable--active .mds-editable__display{display:none;}
.mds-editable__input{width:100%;box-sizing:border-box;height:28px;padding:4px 8px;
  border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-button,2px);
  background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);
  font-family:inherit;font-size:inherit;font-variant-numeric:inherit;
  transition:border-color 100ms ease,box-shadow 100ms ease;}
.mds-editable__input:focus{border-color:var(--interaction-focus,#2D6FB3);outline:var(--focus-ring);outline-offset:-2px;}
.mds-editable__input--error{border-color:var(--red,#B54B42);}
.mds-editable__input--error:focus{outline:2px solid color-mix(in srgb,var(--red) 55%,transparent);outline-offset:-2px;}
.mds-editable__hint{position:absolute;top:100%;left:0;margin-top:4px;
  font-family:var(--font-body);font-size:9px;color:var(--text-muted,#606870);
  white-space:nowrap;pointer-events:none;opacity:0;transition:opacity 100ms ease;}
.mds-editable--active .mds-editable__hint{opacity:1;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "editable-cell");
  el.textContent = css;
  document.head.appendChild(el);
}

export function EditableCell({
  value,
  onChange,
  onCommit,
  validator,
  mode = "text",
  placeholder = "",
  className = "",
  children,
  disabled = false,
  hint = "",
}) {
  inject();
  const [editing, setEditing] = React.useState(false);
  const [draft, setDraft] = React.useState(String(value ?? ""));
  const [error, setError] = React.useState(null);
  const inputRef = React.useRef(null);
  const displayRef = React.useRef(null);
  const returnFocus = React.useRef(false);
  const hintId = React.useId();
  React.useEffect(() => { if (!editing) setDraft(String(value ?? "")); }, [value, editing]);
  const startEdit = () => { if (disabled) return; setDraft(String(value ?? "")); setError(null); setEditing(true); };

  const validate = (val) => {
    if (validator) {
      const result = validator(val);
      if (result !== true) {
        setError(result || "Invalid input");
        return false;
      }
    }
    setError(null);
    return true;
  };

  const commit = () => {
    if (!validate(draft)) return false;
    onCommit?.(draft);
    setEditing(false);
    return true;
  };

  const cancel = () => {
    setDraft(String(value ?? ""));
    setError(null);
    setEditing(false);
  };

  const handleKeyDown = (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      if (commit()) returnFocus.current = true;
    } else if (e.key === "Escape") {
      e.preventDefault();
      returnFocus.current = true;
      cancel();
    } else if (e.key === "Tab") {
      commit();
    }
  };

  const handleChange = (e) => {
    const val = e.target.value;
    setDraft(val);
    onChange?.(val);
    setError(null);
  };

  React.useEffect(() => {
    if (editing && inputRef.current) {
      inputRef.current.focus();
      inputRef.current.select?.();
    }
    if (!editing && returnFocus.current) { returnFocus.current = false; displayRef.current?.focus(); }
  }, [editing]);

  return (
    <div className={`mds-editable ${editing ? "mds-editable--active" : ""} ${className}`}>
      {!editing && (
        <div
          ref={displayRef}
          className="mds-editable__display"
          onClick={startEdit}
          role="button"
          tabIndex={disabled ? -1 : 0}
          aria-disabled={disabled || undefined}
          onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); startEdit(); } }}
        >
          {children ?? value ?? "—"}
        </div>
      )}
      {editing && (
        <>
          <input
            ref={inputRef}
            type={mode === "number" ? "number" : "text"}
            className={`mds-editable__input ${error ? "mds-editable__input--error" : ""}`}
            value={draft}
            aria-label="Edit value"
            aria-invalid={error ? true : undefined}
            aria-describedby={error || hint ? hintId : undefined}
            onChange={handleChange}
            onBlur={commit}
            onKeyDown={handleKeyDown}
            placeholder={placeholder}
            disabled={disabled}
          />
          {(error || hint) && (
            <div className="mds-editable__hint" id={hintId}>
              {error || hint}
            </div>
          )}
        </>
      )}
    </div>
  );
}
