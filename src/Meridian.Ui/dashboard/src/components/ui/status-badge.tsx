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

const TONE_VARIANT: Record<StatusTone, BadgeProps["variant"]> = {
  neutral: "outline",
  info: "outline",
  success: "success",
  warning: "warning",
  danger: "danger"
};

const INFO_CLASSES = "border-primary/40 text-primary";
const SOLID_INFO_CLASSES = "border-primary bg-primary text-primary-foreground";

// Solid semantic tones are rendered as their own span, not through Badge. The design-system
// semantic variants carry a text-success/warning/danger class that would fight a contrasting
// foreground, and cn() does not resolve class conflicts.
const SOLID_TONE_CLASSES: Partial<Record<StatusTone, string>> = {
  success: "border-success bg-success text-background",
  warning: "border-warning bg-warning text-background",
  danger: "border-danger bg-danger text-background"
};

/**
 * The one status pill for workstation screens. Built on {@link Badge} so the existing
 * design-system styling applies. Screen-specific chips should be re-pointed here.
 *
 * @example
 * <StatusBadge tone="warning">Review required</StatusBadge>
 */
export function StatusBadge({ tone = "neutral", emphasis = "subtle", dot = false, className, children }: StatusBadgeProps) {
  const solid = emphasis === "solid";
  const solidTone = solid ? SOLID_TONE_CLASSES[tone] : undefined;
  if (solidTone) {
    return (
      <span
        className={["inline-flex items-center gap-1.5 rounded-[2px] border px-1.5 py-0.5 text-xs font-semibold leading-none", solidTone, className]
          .filter(Boolean)
          .join(" ")}
      >
        {dot ? <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-current" /> : null}
        {children}
      </span>
    );
  }
  const variant = solid && tone === "neutral" ? "default" : TONE_VARIANT[tone];
  const toneClasses = tone === "info" ? (solid ? SOLID_INFO_CLASSES : INFO_CLASSES) : undefined;
  return (
    <Badge variant={variant} dot={dot} className={[toneClasses, className].filter(Boolean).join(" ") || undefined}>
      {children}
    </Badge>
  );
}
