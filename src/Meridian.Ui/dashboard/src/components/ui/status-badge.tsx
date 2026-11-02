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

/**
 * The one status pill for workstation screens. Built on {@link Badge} so the existing
 * design-system styling applies. Screen-specific chips should be re-pointed here.
 *
 * @example
 * <StatusBadge tone="warning">Review required</StatusBadge>
 */
export function StatusBadge({ tone = "neutral", emphasis = "subtle", dot = false, className, children }: StatusBadgeProps) {
  const variant = emphasis === "solid" && tone === "neutral" ? "default" : TONE_VARIANT[tone];
  const infoClasses = tone === "info" ? (emphasis === "solid" ? SOLID_INFO_CLASSES : INFO_CLASSES) : undefined;
  return (
    <Badge variant={variant} dot={dot} className={[infoClasses, className].filter(Boolean).join(" ") || undefined}>
      {children}
    </Badge>
  );
}
