import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export type StatusBannerTone = "success" | "warning" | "danger" | "info";

export interface StatusBannerProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title"> {
  tone?: StatusBannerTone;
  title: ReactNode;
  detail?: ReactNode;
}

const toneClasses: Record<StatusBannerTone, string> = {
  success: "border-l-success bg-success/10 text-success",
  warning: "border-l-warning bg-warning/10 text-warning",
  danger: "border-l-danger bg-danger/10 text-danger",
  info: "border-l-primary bg-primary/10 text-primary"
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
      {detail ? <div className="mt-0.5 font-mono text-xs leading-5 text-muted-foreground">{detail}</div> : null}
    </div>
  );
}
