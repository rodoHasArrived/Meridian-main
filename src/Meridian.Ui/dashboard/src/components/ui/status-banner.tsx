import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export type StatusBannerTone = "success" | "warning" | "danger" | "info";

export interface StatusBannerProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title"> {
  tone?: StatusBannerTone;
  title: ReactNode;
  detail?: ReactNode;
}

const toneClasses: Record<StatusBannerTone, string> = {
  success: "border-l-success bg-[var(--state-healthy-bg)] text-[var(--state-healthy-fg)]",
  warning: "border-l-warning bg-[var(--state-warn-bg)] text-[var(--state-warn-fg)]",
  danger: "border-l-danger bg-[var(--state-danger-bg)] text-[var(--state-danger-fg)]",
  info: "border-l-primary bg-[var(--state-paper-bg)] text-[var(--state-paper-fg)]"
};

export function StatusBanner({ className, detail, title, tone = "info", ...props }: StatusBannerProps) {
  return (
    <div
      className={cn(
        "rounded-[2px] border border-border border-l-[3px] px-3.5 py-3 text-sm",
        toneClasses[tone],
        className
      )}
      {...props}
    >
      <div className="font-semibold">{title}</div>
      {detail ? <div className="mt-0.5 font-mono text-xs leading-5 text-[var(--ws-text-secondary)]">{detail}</div> : null}
    </div>
  );
}
