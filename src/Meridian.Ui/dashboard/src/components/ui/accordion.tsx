import { type KeyboardEvent, type ReactNode, useRef, useState } from "react";
import { ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";

export interface AccordionItem {
  id?: string;
  title: ReactNode;
  content: ReactNode;
  badge?: ReactNode;
  disabled?: boolean;
}

export interface AccordionProps {
  items: AccordionItem[];
  multi?: boolean;
  defaultOpen?: number[];
  className?: string;
}

/**
 * Collapsible sections (single or multi-open). Concrete: flat, hairline-bordered container
 * with divider rules between items; caret rotates on open. Keyboard nav per the Concrete
 * matrix — Arrow keys move between headers, Enter/Space toggles.
 */
export function Accordion({ items, multi = false, defaultOpen = [], className }: AccordionProps) {
  const [open, setOpen] = useState<Set<number>>(() => new Set(defaultOpen));
  const headerRefs = useRef<Array<HTMLButtonElement | null>>([]);

  const toggle = (index: number) => {
    setOpen((previous) => {
      const next = multi ? new Set(previous) : new Set<number>();
      if (previous.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });
  };

  const focusHeader = (index: number) => {
    const enabledIndices = items.map((item, i) => (item.disabled ? -1 : i)).filter((i) => i >= 0);
    if (enabledIndices.length === 0) {
      return;
    }
    const position = enabledIndices.indexOf(index);
    const target = position < 0 ? enabledIndices[0]! : index;
    headerRefs.current[target]?.focus();
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const enabledIndices = items.map((item, i) => (item.disabled ? -1 : i)).filter((i) => i >= 0);
    const position = enabledIndices.indexOf(index);
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        focusHeader(enabledIndices[(position + 1) % enabledIndices.length]!);
        break;
      case "ArrowUp":
        event.preventDefault();
        focusHeader(enabledIndices[(position - 1 + enabledIndices.length) % enabledIndices.length]!);
        break;
      case "Home":
        event.preventDefault();
        focusHeader(enabledIndices[0]!);
        break;
      case "End":
        event.preventDefault();
        focusHeader(enabledIndices[enabledIndices.length - 1]!);
        break;
      default:
        break;
    }
  };

  return (
    <div className={cn("rounded-[2px] border border-border bg-card", className)}>
      {items.map((item, index) => {
        const isOpen = open.has(index);
        const panelId = `${item.id ?? "accordion"}-panel-${index}`;
        return (
          <div key={item.id ?? index} className={cn(index > 0 && "border-t border-border")}>
            <button
              ref={(node) => {
                headerRefs.current[index] = node;
              }}
              type="button"
              aria-expanded={isOpen}
              aria-controls={panelId}
              disabled={item.disabled}
              onClick={() => !item.disabled && toggle(index)}
              onKeyDown={(event) => handleKeyDown(event, index)}
              className={cn(
                "flex w-full items-center gap-2.5 px-3.5 py-3 text-left text-sm font-semibold text-foreground transition-colors [outline-offset:-2px]",
                "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                item.disabled ? "cursor-not-allowed opacity-55" : "hover:bg-[#EAEEF3]"
              )}
            >
              <ChevronRight
                aria-hidden="true"
                className={cn("h-3.5 w-3.5 shrink-0 text-muted-foreground transition-transform duration-150", isOpen && "rotate-90")}
              />
              <span className="flex-1">{item.title}</span>
              {item.badge != null ? (
                <span className="shrink-0 font-mono text-[11px] tabular-nums text-muted-foreground">{item.badge}</span>
              ) : null}
            </button>
            {isOpen ? (
              <div id={panelId} className="px-3.5 pb-3.5 pl-[2.125rem] text-sm leading-5 text-muted-foreground">
                {item.content}
              </div>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}
