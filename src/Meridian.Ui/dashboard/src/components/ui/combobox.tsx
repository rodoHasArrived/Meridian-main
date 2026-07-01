import { Fragment, type KeyboardEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { cn } from "@/lib/utils";

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
      <span className="bg-primary/15 text-primary">{label.slice(index, index + query.length)}</span>
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
  id
}: ComboboxProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);

  const items = useMemo(() => normalize(options), [options]);
  const selected = items.find((option) => option.value === value);
  const filtered = useMemo(
    () => (query ? items.filter((option) => option.label.toLowerCase().includes(query.toLowerCase())) : items),
    [items, query]
  );

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
    onChange?.(option.value);
    setOpen(false);
    setQuery("");
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        setOpen(true);
        setHighlighted((current) => Math.min(current + 1, filtered.length - 1));
        break;
      case "ArrowUp":
        event.preventDefault();
        setHighlighted((current) => Math.max(current - 1, 0));
        break;
      case "Enter":
        event.preventDefault();
        if (filtered[highlighted]) {
          commit(filtered[highlighted]!);
        }
        break;
      case "Escape":
        setOpen(false);
        setQuery("");
        break;
      default:
        break;
    }
  };

  const listboxId = id ? `${id}-listbox` : undefined;

  return (
    <div ref={rootRef} className={cn("relative w-full", className)}>
      {label ? (
        <span className="mb-1.5 block font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {label}
        </span>
      ) : null}
      <div
        className={cn(
          "flex h-9 items-center rounded-[2px] border border-border bg-[#F3F6F9]",
          "focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/40",
          disabled && "cursor-not-allowed opacity-55"
        )}
      >
        <input
          id={id}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-autocomplete="list"
          disabled={disabled}
          placeholder={selected && !open ? selected.label : placeholder}
          value={open ? query : selected ? selected.label : ""}
          onFocus={() => setOpen(true)}
          onChange={(event) => {
            setQuery(event.target.value);
            setOpen(true);
          }}
          onKeyDown={handleKeyDown}
          className="min-w-0 flex-1 bg-transparent px-2.5 font-mono text-sm text-foreground placeholder:text-muted-foreground/70 focus:outline-none"
        />
        <span aria-hidden="true" className="px-2.5 text-xs text-muted-foreground">
          ▾
        </span>
      </div>
      {open ? (
        <div
          role="listbox"
          id={listboxId}
          className="absolute left-0 right-0 top-[calc(100%+4px)] z-[100] max-h-60 overflow-y-auto rounded-[2px] border border-border bg-popover shadow-[0_2px_6px_rgba(0,0,0,0.18)]"
        >
          {filtered.length === 0 ? (
            <div className="px-3 py-2.5 text-xs text-muted-foreground">{emptyText}</div>
          ) : (
            filtered.map((option, index) => {
              const isSelected = option.value === value;
              return (
                <div
                  key={option.value}
                  role="option"
                  aria-selected={isSelected}
                  onMouseEnter={() => setHighlighted(index)}
                  onClick={() => commit(option)}
                  className={cn(
                    "flex cursor-pointer items-center gap-2 px-3 py-2 font-mono text-xs text-foreground",
                    index === highlighted && "bg-[#EAEEF3]",
                    isSelected && "font-semibold text-primary"
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
