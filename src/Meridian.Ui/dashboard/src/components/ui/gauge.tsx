import { type CSSProperties, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface GaugeProps {
  value?: number;
  max?: number;
  label?: ReactNode;
  size?: number;
  thickness?: number;
  color?: string;
  showValue?: boolean;
  className?: string;
  style?: CSSProperties;
}

/**
 * Circular progress indicator for a single headline metric. Concrete: flat plot, hairline
 * track, one accent arc; the number renders as a mono metric, the label as a small-caps
 * eyebrow.
 */
export function Gauge({
  value = 0,
  max = 100,
  label,
  size = 120,
  thickness = 8,
  color = "hsl(var(--primary))",
  showValue = true,
  className,
  style
}: GaugeProps) {
  const percent = Math.min(Math.max(max > 0 ? value / max : 0, 0), 1);
  const radius = (size - thickness) / 2;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference * (1 - percent);
  const center = size / 2;

  return (
    <div className={cn("flex flex-col items-center gap-3", className)} style={style}>
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} style={{ transform: "rotate(-90deg)" }} role="img" aria-label={typeof label === "string" ? label : "gauge"}>
        <circle cx={center} cy={center} r={radius} fill="none" stroke="hsl(var(--border))" strokeWidth={thickness} />
        <circle
          cx={center}
          cy={center}
          r={radius}
          fill="none"
          stroke={color}
          strokeWidth={thickness}
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          strokeLinecap="butt"
          style={{ transition: "stroke-dashoffset 300ms ease-out" }}
        />
      </svg>
      {label || showValue ? (
        <div className="text-center">
          {showValue ? (
            <div className="font-mono text-[28px] font-bold leading-none tabular-nums text-foreground">
              {Math.round(percent * 100)}%
            </div>
          ) : null}
          {label ? (
            <div className="mt-1 font-mono text-[9px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
              {label}
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

export interface LinearGaugeProps {
  value?: number;
  max?: number;
  label?: ReactNode;
  showValue?: boolean;
  color?: string;
  height?: number;
  className?: string;
  style?: CSSProperties;
}

/**
 * Horizontal progress indicator with a small-caps label and mono value readout. Concrete:
 * flat hairline-bordered track, one accent fill, 2px corners.
 */
export function LinearGauge({
  value = 0,
  max = 100,
  label,
  showValue = true,
  color = "hsl(var(--primary))",
  height = 8,
  className,
  style
}: LinearGaugeProps) {
  const percent = Math.min(Math.max(max > 0 ? value / max : 0, 0), 1) * 100;

  return (
    <div className={cn("flex flex-col gap-1.5", className)} style={style}>
      {label || showValue ? (
        <div className="flex items-baseline justify-between">
          {label ? (
            <span className="font-mono text-[9px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{label}</span>
          ) : null}
          {showValue ? (
            <span className="font-mono text-sm font-semibold tabular-nums text-foreground">{Math.round(percent)}%</span>
          ) : null}
        </div>
      ) : null}
      <div
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(percent)}
        className="relative w-full overflow-hidden rounded-[2px] border border-border bg-[#F3F6F9]"
        style={{ height }}
      >
        <div
          className="absolute left-0 top-0 h-full"
          style={{ width: `${percent}%`, backgroundColor: color, transition: "width 300ms ease-out" }}
        />
      </div>
    </div>
  );
}
