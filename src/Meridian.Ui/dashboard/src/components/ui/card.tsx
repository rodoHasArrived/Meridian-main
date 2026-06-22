import { forwardRef } from "react";
import { cn } from "@/lib/utils";

/**
 * Surface container with a rounded border and card background. Compose with
 * `CardHeader`, `CardTitle`, `CardDescription`, and `CardContent` for consistent
 * internal spacing and typography.
 *
 * **Variants:**
 * - `"default"` — static surface, no interaction affordance (default).
 * - `"interactive"` — adds hover border/background shift and a `focus-visible` ring.
 *   Use when the entire card is a clickable or keyboard-navigable target (e.g. wrapped
 *   in a `<Link>` or used as a `<button>`), instead of duplicating hover classes inline.
 *
 * @example
 * // Static information card
 * <Card>
 *   <CardHeader><CardTitle>Risk state</CardTitle></CardHeader>
 *   <CardContent>…</CardContent>
 * </Card>
 *
 * // Navigable card
 * <Card variant="interactive" onClick={handleSelect} tabIndex={0} role="button">
 *   <CardContent>…</CardContent>
 * </Card>
 */
interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  /**
   * `"default"` — standard card surface (default).
   * `"interactive"` — adds hover/focus-ring affordances for clickable or
   *   keyboard-navigable cards. Apply when the whole card is a link or button
   *   target, rather than adding inline hover classes at every usage site.
   */
  variant?: "default" | "interactive";
}

const cardVariants: Record<NonNullable<CardProps["variant"]>, string> = {
  default: "",
  interactive:
    "cursor-pointer transition-[background-color,border-color,box-shadow] hover:border-border hover:bg-muted/45 hover:shadow-[var(--shadow-float)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
};

export const Card = forwardRef<HTMLDivElement, CardProps>(
  ({ className, variant = "default", ...props }, ref) => (
    <div
      ref={ref}
      className={cn(
        "rounded-[var(--radius-card,0.5rem)] border border-border bg-card text-card-foreground shadow-panel",
        cardVariants[variant],
        className
      )}
      {...props}
    />
  )
);

Card.displayName = "Card";

export const CardHeader = forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div ref={ref} className={cn("space-y-1.5 border-b border-border/70 p-4", className)} {...props} />
  )
);

CardHeader.displayName = "CardHeader";

export const CardTitle = forwardRef<HTMLHeadingElement, React.HTMLAttributes<HTMLHeadingElement>>(
  ({ className, ...props }, ref) => (
    <h3 ref={ref} className={cn("text-[0.875rem] font-semibold leading-snug tracking-normal", className)} {...props} />
  )
);

CardTitle.displayName = "CardTitle";

export const CardDescription = forwardRef<HTMLParagraphElement, React.HTMLAttributes<HTMLParagraphElement>>(
  ({ className, ...props }, ref) => (
    <p ref={ref} className={cn("text-xs leading-5 text-muted-foreground", className)} {...props} />
  )
);

CardDescription.displayName = "CardDescription";

export const CardContent = forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div ref={ref} className={cn("p-4", className)} {...props} />
  )
);

CardContent.displayName = "CardContent";
