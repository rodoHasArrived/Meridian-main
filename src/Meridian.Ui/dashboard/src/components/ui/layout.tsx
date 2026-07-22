import { forwardRef, type HTMLAttributes } from "react";
import { cn } from "@/lib/utils";

/**
 * Concrete layout primitives — `Stack`, `Flex`, and `Grid`.
 *
 * Prefer these over hand-rolled flex/grid so spacing tracks the Meridian spacing
 * scale (`xs sm md lg xl 2xl`). Each maps to a fixed Tailwind gap utility so the
 * rhythm stays consistent (6 tight · 12 standard · 16 generous · 24 section · 32 major).
 */
export type SpacingScale = "none" | "xs" | "sm" | "md" | "lg" | "xl" | "2xl";

const gapClasses: Record<SpacingScale, string> = {
  none: "gap-0",
  xs: "gap-1", // ~3px micro / 4px
  sm: "gap-1.5", // ~6px tight
  md: "gap-3", // 12px standard
  lg: "gap-4", // 16px generous
  xl: "gap-6", // 24px section
  "2xl": "gap-8" // 32px major
};

type FlexAlign = "start" | "center" | "end" | "stretch" | "baseline";
type FlexJustify = "start" | "center" | "end" | "between" | "around" | "evenly";

const alignClasses: Record<FlexAlign, string> = {
  start: "items-start",
  center: "items-center",
  end: "items-end",
  stretch: "items-stretch",
  baseline: "items-baseline"
};

const justifyClasses: Record<FlexJustify, string> = {
  start: "justify-start",
  center: "justify-center",
  end: "justify-end",
  between: "justify-between",
  around: "justify-around",
  evenly: "justify-evenly"
};

export interface StackProps extends HTMLAttributes<HTMLDivElement> {
  direction?: "vertical" | "horizontal";
  spacing?: SpacingScale;
  align?: FlexAlign;
  justify?: FlexJustify;
}

/** Semantic directional flex — reach for it when direction is the point. */
export const Stack = forwardRef<HTMLDivElement, StackProps>(
  ({ className, direction = "vertical", spacing = "md", align = "stretch", justify = "start", ...props }, ref) => (
    <div
      ref={ref}
      className={cn(
        "flex",
        direction === "vertical" ? "flex-col" : "flex-row",
        gapClasses[spacing],
        alignClasses[align],
        justifyClasses[justify],
        className
      )}
      {...props}
    />
  )
);

Stack.displayName = "Stack";

export interface FlexProps extends HTMLAttributes<HTMLDivElement> {
  vertical?: boolean;
  gap?: SpacingScale;
  align?: FlexAlign;
  justify?: FlexJustify;
  wrap?: boolean;
}

/** A row (or `vertical` column) with `align` / `justify` / `gap`. */
export const Flex = forwardRef<HTMLDivElement, FlexProps>(
  ({ className, vertical = false, gap = "md", align = "stretch", justify = "start", wrap = false, ...props }, ref) => (
    <div
      ref={ref}
      className={cn(
        "flex",
        vertical ? "flex-col" : "flex-row",
        wrap && "flex-wrap",
        gapClasses[gap],
        alignClasses[align],
        justifyClasses[justify],
        className
      )}
      {...props}
    />
  )
);

Flex.displayName = "Flex";

export interface GridProps extends HTMLAttributes<HTMLDivElement> {
  cols?: 1 | 2 | 3 | 4 | 5 | 6 | 12;
  gap?: SpacingScale;
}

const colClasses: Record<NonNullable<GridProps["cols"]>, string> = {
  1: "grid-cols-1",
  2: "grid-cols-2",
  3: "grid-cols-3",
  4: "grid-cols-4",
  5: "grid-cols-5",
  6: "grid-cols-6",
  12: "grid-cols-12"
};

/** Equal-column CSS grid: `<Grid cols={3} gap="lg">`. */
export const Grid = forwardRef<HTMLDivElement, GridProps>(
  ({ className, cols = 2, gap = "md", ...props }, ref) => (
    <div ref={ref} className={cn("grid", colClasses[cols], gapClasses[gap], className)} {...props} />
  )
);

Grid.displayName = "Grid";
