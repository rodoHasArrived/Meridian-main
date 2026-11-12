// Meridian EditableCell (Concrete) — an inline editable value with validation.
//
// Native TypeScript port of the Meridian Design System `components/data/EditableCell.jsx`:
// click or Enter to edit; Enter, Tab or blur commits once the validator passes; Escape cancels and
// restores the value. It is the display half of OptimisticCell and can be used on its own.
//
// Kept from the package: the keyboard contract, the string draft, the validator shape and the
// "stay in edit mode with the message" failure behaviour.
// Fixed in the port, because the dashboard's accessibility and correctness bar is higher:
// - Space also opens the editor; the control states `aria-disabled` when it cannot be edited.
// - A validation failure is announced (`role="alert"`) and wired to the input (`aria-invalid`,
//   `aria-describedby`), instead of being a red border only.
// - One editing session commits at most once (Tab followed by its own blur used to commit twice).
// - The draft is re-read from `value` every time the editor opens.
// - Enter and Escape hand focus back to the cell; Escape does not close an enclosing drawer.
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from "react";
import { injectStyle } from "../operations/inject-style";

const CSS = `
/* The dashboard's --border is an HSL channel list, not a CSS color: use the workstation roles. */
.mds-editable{position:relative;display:inline-block;min-width:0;max-width:100%;}
/* 24px in both directions: a one-character value must still be a full-size target. */
.mds-editable__display{display:inline-flex;align-items:baseline;justify-content:center;gap:6px;box-sizing:border-box;
  min-width:24px;min-height:24px;max-width:100%;padding:3px 6px;cursor:pointer;user-select:none;border:1px solid var(--ws-border,var(--border-color,#D2CDC3));
  border-radius:var(--radius-chip,2px);color:var(--text-primary,#20252B);
  font-family:var(--font-data,monospace);font-size:12px;line-height:1.3;font-variant-numeric:tabular-nums;
  transition:background-color 100ms ease;}
.mds-editable__display:hover{background:var(--bg-hover,#EDEFF0);}
.mds-editable__display:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-editable__display[aria-disabled="true"]{cursor:default;border-color:transparent;}
.mds-editable__display[aria-disabled="true"]:hover{background:transparent;}
.mds-editable__input{box-sizing:border-box;width:100%;min-width:6rem;min-height:28px;padding:4px 8px;
  border:1px solid var(--border-strong,#AAA49A);border-radius:var(--radius-button,4px);
  background:var(--ws-surface-raised,var(--bg-light,#F8F7F3));color:var(--text-primary,#20252B);
  font-family:var(--font-data,monospace);font-size:12px;font-variant-numeric:tabular-nums;}
.mds-editable__input:focus{border-color:var(--interaction-focus,#2D6FB3);outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-editable__input--error{border-color:var(--red,#B54B42);}
.mds-editable__hint{margin:4px 0 0;font-family:var(--font-body,system-ui,sans-serif);font-size:12px;line-height:1.35;
  color:var(--text-muted,#606870);}
.mds-editable__hint--error{color:var(--severity-blocked-fg,#883832);font-weight:600;}
@media (prefers-reduced-motion:reduce){.mds-editable__display{transition:none;}}
@media (forced-colors:active){
  .mds-editable__display{border-color:ButtonText;}
  .mds-editable__display[aria-disabled="true"]{border-color:GrayText;}
  .mds-editable__input--error{border-color:Mark;}
}
`;

export interface EditableCellProps {
  /** The committed value. The editor's draft is re-read from it each time editing starts. */
  value: string | number | null | undefined;
  /** Observe every keystroke of the draft. */
  onChange?: (draft: string) => void;
  /** Called once per editing session with the validated draft. Always a string, also in number mode. */
  onCommit: (draft: string) => void;
  /** Return `true` to accept the draft, or the message to show while the editor stays open. */
  validator?: (draft: string) => true | string;
  /** @default "text" */
  mode?: "text" | "number";
  placeholder?: string;
  className?: string;
  /** Display content; defaults to the value, or an em dash when there is none. */
  children?: ReactNode;
  /** @default false */
  disabled?: boolean;
  /** Guidance shown under the open editor. */
  hint?: string;
  /** Accessible name of the cell. @default `Edit ${value}` */
  ariaLabel?: string;
  /** Receives the display element, so an owner can return focus to the cell. */
  displayRef?: RefObject<HTMLDivElement | null>;
}

/**
 * EditableCell — an inline value that turns into an input.
 *
 * @example
 * <EditableCell value={row.owner} onCommit={(owner) => save(row.id, owner)} validator={(v) => v.trim() ? true : "Cannot be empty"} />
 */
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
  ariaLabel,
  displayRef
}: EditableCellProps) {
  injectStyle("editable-cell", CSS);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(String(value ?? ""));
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const ownDisplayRef = useRef<HTMLDivElement | null>(null);
  const display = displayRef ?? ownDisplayRef;
  // True from the moment the editor opens until its one commit or its cancel.
  const sessionOpen = useRef(false);
  const restoreFocus = useRef(false);
  const hintId = useId();

  const start = () => {
    if (disabled) {
      return;
    }
    sessionOpen.current = true;
    setDraft(String(value ?? ""));
    setError(null);
    setEditing(true);
  };

  const commit = (returnFocus: boolean) => {
    if (!sessionOpen.current) {
      return;
    }
    const verdict = validator ? validator(draft) : true;
    if (verdict !== true) {
      setError(verdict || "Invalid input");
      return;
    }
    sessionOpen.current = false;
    restoreFocus.current = returnFocus;
    setError(null);
    setEditing(false);
    onCommit(draft);
  };

  const cancel = () => {
    sessionOpen.current = false;
    restoreFocus.current = true;
    setDraft(String(value ?? ""));
    setError(null);
    setEditing(false);
  };

  const handleInputKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      event.preventDefault();
      commit(true);
    } else if (event.key === "Escape") {
      // Only the editor closes; an enclosing drawer or dialog stays open.
      event.preventDefault();
      event.stopPropagation();
      cancel();
    } else if (event.key === "Tab") {
      commit(false);
    }
  };

  const handleDisplayKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      start();
    }
  };

  useEffect(() => {
    if (editing) {
      inputRef.current?.focus();
      inputRef.current?.select();
    } else if (restoreFocus.current) {
      restoreFocus.current = false;
      display.current?.focus();
    }
  }, [editing, display]);

  const describedBy = error || hint ? hintId : undefined;

  return (
    <div className={`mds-editable ${editing ? "mds-editable--active" : ""} ${className}`.trim()}>
      {editing ? (
        <>
          <input
            ref={inputRef}
            type={mode === "number" ? "number" : "text"}
            className={`mds-editable__input ${error ? "mds-editable__input--error" : ""}`.trim()}
            value={draft}
            aria-label={ariaLabel ?? "Edit value"}
            aria-invalid={error ? true : undefined}
            aria-describedby={describedBy}
            placeholder={placeholder}
            onChange={(event) => {
              setDraft(event.target.value);
              setError(null);
              onChange?.(event.target.value);
            }}
            onBlur={() => commit(false)}
            onKeyDown={handleInputKeyDown}
          />
          {error ? (
            <p id={hintId} role="alert" className="mds-editable__hint mds-editable__hint--error">{error}</p>
          ) : hint ? (
            <p id={hintId} className="mds-editable__hint">{hint}</p>
          ) : null}
        </>
      ) : (
        <div
          ref={display}
          className="mds-editable__display"
          role="button"
          tabIndex={disabled ? -1 : 0}
          aria-disabled={disabled ? true : undefined}
          aria-label={ariaLabel ?? `Edit ${value == null || value === "" ? "empty value" : value}`}
          onClick={start}
          onKeyDown={handleDisplayKeyDown}
        >
          {children ?? (value == null || value === "" ? "—" : value)}
        </div>
      )}
    </div>
  );
}
