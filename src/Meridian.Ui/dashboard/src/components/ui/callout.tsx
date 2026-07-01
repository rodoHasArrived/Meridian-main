import { type ReactNode } from "react";
import { cn } from "@/lib/utils";

export type CalloutTone = "info" | "success" | "warning" | "danger";

export interface CalloutProps {
  tone?: CalloutTone;
  title?: ReactNode;
  icon?: ReactNode;
  children?: ReactNode;
  className?: string;
}

const toneClasses: Record<CalloutTone, string> = {
  info: "border-l-primary bg-primary/10",
  success: "border-l-success bg-success/10",
  warning: "border-l-warning bg-warning/10",
  danger: "border-l-danger bg-danger/10"
};

const iconToneClasses: Record<CalloutTone, string> = {
  info: "text-primary",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

const defaultIcons: Record<CalloutTone, string> = {
  info: "ℹ",
  success: "✓",
  warning: "▲",
  danger: "✕"
};

/**
 * Inline prose guidance inside content — distinct from `StatusBanner` (run results). Concrete:
 * desaturated alpha-10 wash, solid 3px left rule, hairline border, 2px corners, no fill.
 */
export function Callout({ tone = "info", title, icon, children, className }: CalloutProps) {
  return (
    <div
      role="note"
      className={cn(
        "flex gap-2.5 rounded-[2px] border border-border border-l-[3px] px-3.5 py-3 text-sm",
        toneClasses[tone],
        className
      )}
    >
      <span aria-hidden="true" className={cn("shrink-0 leading-5", iconToneClasses[tone])}>
        {icon ?? defaultIcons[tone]}
      </span>
      <div className="min-w-0 flex-1">
        {title ? <p className="font-semibold leading-5 text-foreground">{title}</p> : null}
        {children ? <p className="leading-5 text-muted-foreground">{children}</p> : null}
      </div>
    </div>
  );
}
