// Meridian KPI tile (Concrete) — mirrors the desktop MetricCardStyle + its
// success/danger/warning/info variants: a raised paper surface with a 3px LEFT-accent
// border in the tone color, a small-caps label, a 24px mono value, and a signed delta
// (green up / red down). This is the design-system MetricCard; the pre-existing
// read-model tile lives at `components/meridian/metric-card.tsx` and is left untouched.
import type { ReactNode } from "react";
import { injectStyle } from "../operations/inject-style";

const CSS = `
.mds-metric{background:var(--card-surface-raised,#F3F6F9);
  border:1px solid var(--border,#CBD3DC);border-left-width:3px;
  border-radius:var(--radius-card,2px);padding:18px;}
.mds-metric--neutral{border-left-color:var(--border-strong,#99A5B2);}
.mds-metric--info{border-left-color:var(--accent,#2F6F8F);}
.mds-metric--success{border-left-color:var(--severity-ready-fg,#16885F);}
.mds-metric--warning{border-left-color:var(--severity-action-fg,#8A520E);}
.mds-metric--danger{border-left-color:var(--severity-blocked-fg,#BA3F55);}
.mds-metric__label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);margin:0;}
.mds-metric__value{margin:8px 0 0;font-family:var(--font-data,monospace);font-size:24px;font-weight:600;
  line-height:1;font-variant-numeric:tabular-nums;color:var(--text-primary,#22272E);}
.mds-metric__delta{font-family:var(--font-data,monospace);font-size:11px;margin-top:6px;
  font-variant-numeric:tabular-nums;}
.mds-delta--up{color:var(--severity-ready-fg,#16885F);}
.mds-delta--down{color:var(--severity-blocked-fg,#BA3F55);}
.mds-delta--flat{color:var(--text-muted,#59636F);}
`;

export type MetricCardTone = "neutral" | "info" | "success" | "warning" | "danger";

export interface MetricCardProps {
  /** Small-caps label, e.g. "Net liquidation". */
  label: ReactNode;
  /** Mono tabular value, e.g. "$1,284,002.18". */
  value: ReactNode;
  /** Signed delta/context line, e.g. "+1.84% today". */
  delta?: string;
  /** Left-accent color. @default "neutral" */
  tone?: MetricCardTone;
  /** Force delta color; otherwise inferred from a leading +/− sign. */
  trend?: "up" | "down" | "flat";
  /** Override the composed group accessible name (label, value, change, status). */
  ariaLabel?: string;
}

const TONE_STATUS: Record<MetricCardTone, string> = {
  neutral: "",
  info: "",
  success: "positive",
  warning: "attention",
  danger: "critical",
};

/**
 * MetricCard — the KPI tile with a 3px left-accent border. Lay out 4–5 in a row.
 *
 * @example
 * <MetricCard label="Net liquidation" value="$1,284,002.18" delta="+1.84% today" tone="success" />
 * <MetricCard label="Day P&L" value="-$4,118.22" delta="-0.32%" tone="danger" />
 */
export function MetricCard({ label, value, delta, tone = "neutral", trend, ariaLabel }: MetricCardProps) {
  injectStyle("metric", CSS);
  const t =
    trend ??
    (delta?.trim().startsWith("-") ? "down" : delta?.trim().startsWith("+") ? "up" : "flat");
  // Compose the group name so screen-reader users keep the change + status context
  // that label/value alone would drop.
  const composedLabel =
    ariaLabel ??
    [
      typeof label === "string" ? label : undefined,
      typeof value === "string" ? value : undefined,
      delta ? `change ${delta}` : undefined,
      TONE_STATUS[tone] ? `status ${TONE_STATUS[tone]}` : undefined,
    ]
      .filter(Boolean)
      .join(", ");
  return (
    <div
      className={`mds-metric mds-metric--${tone}`}
      role="group"
      aria-label={composedLabel}
    >
      <p className="mds-metric__label">{label}</p>
      <p className="mds-metric__value">{value}</p>
      {delta && <div className={`mds-metric__delta mds-delta--${t}`}>{delta}</div>}
    </div>
  );
}
