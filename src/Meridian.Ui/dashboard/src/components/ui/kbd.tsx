import { Fragment, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface KbdProps {
  /** A combo string (`"Ctrl+Enter"`, `"⌘ K"`) or an explicit list of key caps. */
  keys?: string | string[];
  variant?: "default" | "solid";
  className?: string;
  children?: ReactNode;
}

function parseKeys(keys?: string | string[], children?: ReactNode): string[] | null {
  if (Array.isArray(keys)) {
    return keys;
  }
  if (typeof keys === "string") {
    return keys.split(/\s*\+\s*|\s+/).filter(Boolean);
  }
  if (typeof children === "string") {
    return children.split(/\s*\+\s*|\s+/).filter(Boolean);
  }
  return null;
}

/**
 * Keyboard shortcut hint. Operator workstations are keyboard-driven; this renders crisp mono
 * key caps and combos (⌘K, Ctrl+Enter). Concrete: hairline border with a 2px "keycap" bottom
 * edge, mono type, 2px corners.
 */
export function Kbd({ keys, variant = "default", className, children }: KbdProps) {
  const parts = parseKeys(keys, children);
  const keyClass = cn(
    "inline-flex h-[18px] min-w-[18px] items-center justify-center rounded-[2px] border border-b-2 border-border px-1.5 font-mono text-[11px] font-semibold leading-none",
    variant === "solid" ? "bg-card text-foreground" : "bg-[#F3F6F9] text-muted-foreground"
  );

  return (
    <span className={cn("inline-flex items-center gap-0.5 align-middle", className)}>
      {parts ? (
        parts.map((key, index) => (
          <Fragment key={index}>
            {index > 0 ? (
              <span aria-hidden="true" className="px-px text-[10px] font-medium text-muted-foreground">
                +
              </span>
            ) : null}
            <kbd className={keyClass}>{key}</kbd>
          </Fragment>
        ))
      ) : (
        <kbd className={keyClass}>{children}</kbd>
      )}
    </span>
  );
}
