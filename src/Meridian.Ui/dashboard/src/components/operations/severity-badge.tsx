import type { HTMLAttributes, ReactNode } from "react";
import { DesignSystemStatus, normalizeDesignSystemSeverity } from "@/design-system/status";
import { cn } from "@/lib/utils";

export interface SeverityBadgeProps extends HTMLAttributes<HTMLSpanElement> {
  status?: string;
  label?: ReactNode;
  dot?: boolean;
}

/** Compatibility adapter: all operator severity chips use the same presentation and vocabulary. */
export function SeverityBadge({ status = "info", label, dot = true, className, ...rest }: SeverityBadgeProps) {
  const severity = normalizeDesignSystemSeverity(status);
  return <DesignSystemStatus status={status} dot={dot} className={cn(`mds-sev--${severity}`, className)} {...rest}>
    {label}
  </DesignSystemStatus>;
}
