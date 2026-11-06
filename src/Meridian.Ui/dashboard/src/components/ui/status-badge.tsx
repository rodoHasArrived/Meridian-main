import type { ReactNode } from "react";
import { Badge, type BadgeProps } from "@/components/ui/badge";

/**
 * Meaning of a status, independent of how strong it looks. Colour carries meaning only
 * through `tone`: `danger` is reserved for blocked or failed states.
 */
export type StatusTone = "neutral" | "info" | "success" | "warning" | "danger";

/** `subtle` for ordinary status text; `solid` for the single most important state in an area. */
export type StatusEmphasis = "subtle" | "solid";

export interface StatusBadgeProps {
  tone?: StatusTone;
  emphasis?: StatusEmphasis;
  dot?: boolean;
  className?: string;
  children: ReactNode;
}

// Subtle tones use the design-system variant as-is. Adding colour classes to a variant
// produces conflicting utilities (for example bg-secondary/35 against bg-primary), and cn()
// does not resolve them, so the tone is chosen by variant alone.
const SUBTLE_VARIANT: Record<StatusTone, BadgeProps["variant"]> = {
  neutral: "outline",
  info: "default",
  success: "success",
  warning: "warning",
  danger: "danger"
};

// Solid tones are rendered as their own span with a fill and a single contrasting foreground.
const SOLID_TONE_CLASSES: Record<StatusTone, string> = {
  neutral: "",
  info: "border-primary bg-primary text-primary-foreground",
  success: "border-success bg-success text-background",
  warning: "border-warning bg-warning text-background",
  danger: "border-danger bg-danger text-background"
};

/**
 * The one status pill for workstation screens. Subtle tones are design-system badges; solid
 * tones are filled spans so the fill and label colours never fight each other. Screen-specific
 * chips should be re-pointed here.
 *
 * @example
 * <StatusBadge tone="warning">Review required</StatusBadge>
 */
export function StatusBadge({ tone = "neutral", emphasis = "subtle", dot = false, className, children }: StatusBadgeProps) {
  if (emphasis === "solid" && tone !== "neutral") {
    return (
      <span
        className={["inline-flex items-center gap-1.5 rounded-[2px] border px-1.5 py-0.5 text-xs font-semibold leading-none", SOLID_TONE_CLASSES[tone], className]
          .filter(Boolean)
          .join(" ")}
      >
        {dot ? <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-current" /> : null}
        {children}
      </span>
    );
  }
  // Solid neutral has no semantic meaning to fill with, so it uses the design-system default.
  const variant = emphasis === "solid" ? "default" : SUBTLE_VARIANT[tone];
  return (
    <Badge variant={variant} dot={dot} className={className}>
      {children}
    </Badge>
  );
}
