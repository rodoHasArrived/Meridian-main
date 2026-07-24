// Meridian TagInput — chip-list entry for symbols, recipients, keywords. Enter/comma commits
// the draft; Backspace on an empty draft removes the last chip. Square chips, mono values
// (symbols are data). Controlled: value is a string array.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tags{display:flex;flex-wrap:wrap;gap:4px;align-items:center;min-height:32px;box-sizing:border-box;
  padding:3px 6px;border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#fff);cursor:text;}
.mds-tags:focus-within{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
.mds-tags--disabled{background:var(--bg-medium,#EBEFF4);cursor:not-allowed;}
.mds-tags__chip{display:inline-flex;align-items:center;gap:5px;padding:2px 4px 2px 7px;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-chip,2px);
  background:var(--bg-medium,#EBEFF4);font-family:var(--font-data);font-size:11px;
  color:var(--text-primary,#22272E);white-space:nowrap;}
.mds-tags__x{appearance:none;border:none;background:transparent;cursor:pointer;padding:0;
  width:14px;height:14px;line-height:1;font-size:12px;color:var(--text-muted,#59636F);
  display:inline-flex;align-items:center;justify-content:center;}
.mds-tags__x:hover{color:var(--red,#BA3F55);}
.mds-tags__x:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:0;}
.mds-tags__input{flex:1;min-width:90px;border:none;outline:none;background:transparent;
  font-family:var(--font-data);font-size:12px;color:var(--text-primary,#22272E);height:24px;}
.mds-tags__input::placeholder{color:var(--text-disabled,#889099);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "taginput");
  el.textContent = css;
  document.head.appendChild(el);
}

export function TagInput({
  value = [],
  onChange,
  placeholder = "Add…",
  uppercase = false,
  validate = null,
  disabled = false,
  "aria-label": ariaLabel = "Tags",
}) {
  inject();
  const [draft, setDraft] = React.useState("");
  const inputRef = React.useRef(null);
  const commit = () => {
    let t = draft.trim().replace(/,+$/, "");
    if (!t) return;
    if (uppercase) t = t.toUpperCase();
    if (validate && !validate(t)) return;
    if (!value.includes(t) && onChange) onChange([...value, t]);
    setDraft("");
  };
  const remove = (i) => onChange && onChange(value.filter((_, j) => j !== i));
  const onKeyDown = (e) => {
    if (e.key === "Enter" || e.key === ",") { e.preventDefault(); commit(); }
    else if (e.key === "Backspace" && !draft && value.length) remove(value.length - 1);
  };
  return (
    <div
      className={`mds-tags${disabled ? " mds-tags--disabled" : ""}`}
      onClick={() => !disabled && inputRef.current && inputRef.current.focus()}
    >
      {value.map((t, i) => (
        <span key={t + i} className="mds-tags__chip">
          {t}
          {!disabled && (
            <button
              type="button"
              className="mds-tags__x"
              aria-label={`Remove ${t}`}
              onClick={(e) => { e.stopPropagation(); remove(i); }}
            >×</button>
          )}
        </span>
      ))}
      <input
        ref={inputRef}
        className="mds-tags__input"
        aria-label={ariaLabel}
        value={draft}
        placeholder={value.length ? "" : placeholder}
        disabled={disabled}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={onKeyDown}
        onBlur={commit}
      />
    </div>
  );
}
