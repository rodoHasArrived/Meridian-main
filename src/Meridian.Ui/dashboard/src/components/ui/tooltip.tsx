import { type HTMLAttributes, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * CSS-only tooltip that appears on hover or keyboard focus of the wrapped child.
 *
 * **Sides:** `"top"` (default), `"bottom"`, `"left"`, `"right"`. Each variant positions
 * the popover and its directional arrow automatically.
 *
 * **Trigger mechanism:** the tooltip becomes visible via `group-hover:opacity-100` and
 * `group-focus-within:opacity-100` on a wrapping `<span class="group">`. This means:
 * - No JavaScript is involved — it cannot be shown or hidden programmatically.
 * - It works for `:focus` (keyboard) but not for triggered states like "Copied!" feedback.
 *   For dynamic/triggered tooltips, manage visibility with state and a conditional render.
 *
 * `TooltipLabel` is a companion `<span>` styled to match the tooltip popover font —
 * useful for inline label text adjacent to an icon.
 *
 * @example
 * <Tooltip content="Refresh execution snapshot" side="bottom">
 *   <Button size="sm" variant="ghost" aria-label="Refresh">
 *     <RefreshCcw className="h-4 w-4" />
 *   </Button>
 * </Tooltip>
 */
type TooltipSide = "top" | "bottom" | "left" | "right";

interface TooltipProps {
  content: ReactNode;
  side?: TooltipSide;
  className?: string;
  children: ReactNode;
}

const sideClasses: Record<TooltipSide, string> = {
  top: "bottom-full left-1/2 mb-2 -translate-x-1/2",
  bottom: "top-full left-1/2 mt-2 -translate-x-1/2",
  left: "right-full top-1/2 mr-2 -translate-y-1/2",
  right: "left-full top-1/2 ml-2 -translate-y-1/2"
};

const arrowSideClasses: Record<TooltipSide, string> = {
  top: "top-full left-1/2 -translate-x-1/2 border-t-[hsl(var(--border))] border-x-transparent border-b-transparent",
  bottom: "bottom-full left-1/2 -translate-x-1/2 border-b-[hsl(var(--border))] border-x-transparent border-t-transparent",
  left: "left-full top-1/2 -translate-y-1/2 border-l-[hsl(var(--border))] border-y-transparent border-r-transparent",
  right: "right-full top-1/2 -translate-y-1/2 border-r-[hsl(var(--border))] border-y-transparent border-l-transparent"
};

export function Tooltip({ children, className, content, side = "top" }: TooltipProps) {
  return (
    <span className="group relative inline-flex">
      {children}
      <span
        role="tooltip"
        className={cn(
          "pointer-events-none absolute z-50 whitespace-nowrap",
          "rounded-md border border-border bg-popover px-2.5 py-1.5",
          "font-mono text-[11px] text-popover-foreground shadow-float",
          "opacity-0 transition-opacity duration-150 group-hover:opacity-100 group-focus-within:opacity-100",
          sideClasses[side],
          className
        )}
      >
        {content}
        <span
          aria-hidden="true"
          className={cn(
            "absolute h-0 w-0 border-4",
            arrowSideClasses[side]
          )}
        />
      </span>
    </span>
  );
}

export function TooltipLabel({ className, ...props }: HTMLAttributes<HTMLSpanElement>) {
  return (
    <span
      className={cn("font-mono text-[11px] text-muted-foreground", className)}
      {...props}
    />
  );
}
