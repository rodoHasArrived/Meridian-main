import { Fragment, type InputHTMLAttributes, type KeyboardEvent, type ReactNode, useEffect, useId, useMemo, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import { usesTrackedCaps } from "@/design-system/label-case";

export interface ComboboxOption {
  value: string;
  label: string;
}

export interface ComboboxProps {
  options: Array<ComboboxOption | string>;
  value?: string;
  onChange?: (value: string) => void;
  label?: ReactNode;
  placeholder?: string;
  disabled?: boolean;
  emptyText?: string;
  className?: string;
  id?: string;
  required?: boolean;
  "aria-label"?: string;
  "aria-describedby"?: string;
  "aria-invalid"?: InputHTMLAttributes<HTMLInputElement>["aria-invalid"];
}

function normalize(options: Array<ComboboxOption | string>): ComboboxOption[] {
  return options.map((option) => (typeof option === "string" ? { value: option, label: option } : option));
}

function highlight(label: string, query: string): ReactNode {
  if (!query) {
    return label;
  }
  const index = label.toLowerCase().indexOf(query.toLowerCase());
  if (index === -1) {
    return label;
  }
  return (
    <Fragment>
      {label.slice(0, index)}
      <span className="bg-[var(--interaction-selected-surface)] text-foreground">{label.slice(index, index + query.length)}</span>
      {label.slice(index + query.length)}
    </Fragment>
  );
}

/**
 * Searchable single-select. Type to filter, Arrow keys to navigate, Enter to confirm,
 * Escape to close (keeping the current selection). Concrete: flat field, hairline border,
 * menu carries the only shadow.
 */
export function Combobox({
  options,
  value,
  onChange,
  label,
  placeholder = "Search…",
  disabled = false,
  emptyText = "No matches",
  className,
  id,
  required,
  "aria-label": ariaLabel,
  "aria-describedby": describedBy,
  "aria-invalid": invalid
}: ComboboxProps) {
  const generatedId = useId();
  const inputId = id ?? `${generatedId}-combobox`;
  const listboxId = `${inputId}-listbox`;
  const labelId = `${inputId}-label`;
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const items = useMemo(() => normalize(options), [options]);
  const selected = items.find((option) => option.value === value);
  const filtered = useMemo(
    () => {
      const search = query.toLowerCase();
      return search ? items.filter((option) => option.label.toLowerCase().includes(search) || option.value.toLowerCase().includes(search)) : items;
    },
    [items, query]
  );
  const isOpen = open && !disabled;
  const activeIndex = Math.max(0, Math.min(highlighted, filtered.length - 1));
  const optionId = (index: number) => `${listboxId}-option-${index}`;
  const activeOptionId = isOpen && filtered[activeIndex] ? optionId(activeIndex) : undefined;

  useEffect(() => {
    if (activeOptionId) {
      document.getElementById(activeOptionId)?.scrollIntoView?.({ block: "nearest" });
    }
  }, [activeOptionId]);

  useEffect(() => {
    if (typeof document === "undefined") {
      return undefined;
    }
    const handler = (event: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false);
        setQuery("");
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  useEffect(() => {
    setHighlighted(0);
  }, [query]);

  const commit = (option: ComboboxOption) => {
    if (disabled || inputRef.current?.matches(":disabled")) return;
    onChange?.(option.value);
    setOpen(false);
    setQuery("");
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.currentTarget.matches(":disabled")) return;
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        setOpen(true);
        setHighlighted((current) => isOpen ? Math.max(0, Math.min(current + 1, filtered.length - 1)) : Math.max(0, items.findIndex((option) => option.value === value)));
        break;
      case "ArrowUp":
        event.preventDefault();
        setOpen(true);
        setHighlighted((current) => isOpen ? Math.max(current - 1, 0) : Math.max(filtered.length - 1, 0));
        break;
      case "Home":
      case "End":
        if (!isOpen) return;
        event.preventDefault();
        setHighlighted(event.key === "Home" ? 0 : Math.max(filtered.length - 1, 0));
        break;
      case "Enter":
        if (!isOpen) return;
        event.preventDefault();
        if (filtered[activeIndex]) {
          commit(filtered[activeIndex]!);
        }
        break;
      case "Escape":
        if (!isOpen) return;
        event.preventDefault();
        event.stopPropagation();
        setOpen(false);
        setQuery("");
        break;
      case "Tab":
        setOpen(false);
        setQuery("");
        break;
      default:
        break;
    }
  };

  return (
    <div ref={rootRef} className={cn("relative w-full", className)}>
      {label ? (
        <label id={labelId} htmlFor={inputId} className={cn("mb-1.5 block text-[10px] font-semibold text-muted-foreground", usesTrackedCaps(label) && "font-mono uppercase tracking-[0.14em]")}>
          {label}
        </label>
      ) : null}
      <div
        className={cn(
          "flex h-9 items-center rounded-[var(--radius-button)] border border-border bg-[var(--ws-surface-raised)]",
          "focus-within:border-[var(--interaction-focus)] focus-within:ring-2 focus-within:ring-[var(--interaction-focus)]",
          disabled && "cursor-not-allowed opacity-55"
        )}
      >
        <input
          ref={inputRef}
          id={inputId}
          type="text"
          role="combobox"
          aria-expanded={isOpen}
          aria-controls={isOpen ? listboxId : undefined}
          aria-activedescendant={activeOptionId}
          aria-autocomplete="list"
          aria-label={ariaLabel}
          aria-describedby={describedBy}
          aria-invalid={invalid}
          required={required}
          autoComplete="off"
          disabled={disabled}
          placeholder={selected && !isOpen ? selected.label : placeholder}
          value={isOpen ? query : selected ? selected.label : ""}
          onFocus={() => {
            setHighlighted(Math.max(0, items.findIndex((option) => option.value === value)));
            setOpen(true);
          }}
          onClick={() => {
            if (!isOpen) {
              setHighlighted(Math.max(0, items.findIndex((option) => option.value === value)));
              setOpen(true);
            }
          }}
          onChange={(event) => {
            setQuery(event.target.value);
            setOpen(true);
          }}
          onBlur={() => {
            setOpen(false);
            setQuery("");
          }}
          onKeyDown={handleKeyDown}
          className="min-w-0 flex-1 bg-transparent px-2.5 font-mono text-sm text-foreground placeholder:text-muted-foreground/70 focus:outline-hidden"
        />
        <span aria-hidden="true" className="px-2.5 text-xs text-muted-foreground">
          ▾
        </span>
      </div>
      {isOpen ? (
        <div
          role="listbox"
          id={listboxId}
          aria-labelledby={label ? labelId : undefined}
          aria-label={label ? undefined : "Options"}
          className="absolute left-0 right-0 top-[calc(100%+4px)] z-[100] max-h-60 overflow-y-auto rounded-[var(--radius-overlay)] border border-border bg-popover shadow-[0_2px_6px_rgba(0,0,0,0.18)]"
        >
          {filtered.length === 0 ? (
            <div className="px-3 py-2.5 text-xs text-muted-foreground">{emptyText}</div>
          ) : (
            filtered.map((option, index) => {
              const isSelected = option.value === value;
              return (
                <div
                  key={option.value}
                  id={optionId(index)}
                  role="option"
                  aria-label={option.label}
                  aria-selected={isSelected}
                  onMouseEnter={() => setHighlighted(index)}
                  onMouseDown={(event) => event.preventDefault()}
                  onClick={() => commit(option)}
                  className={cn(
                    "flex cursor-pointer items-center gap-2 px-3 py-2 font-mono text-xs text-foreground",
                    index === activeIndex && "bg-[var(--ws-row-hover)]",
                    isSelected && "font-semibold text-[var(--interaction-link)]"
                  )}
                >
                  {highlight(option.label, query)}
                  {isSelected ? <span className="ml-auto text-[11px]">✓</span> : null}
                </div>
              );
            })
          )}
        </div>
      ) : null}
    </div>
  );
}
