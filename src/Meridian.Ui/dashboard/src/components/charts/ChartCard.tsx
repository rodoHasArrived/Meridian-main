// Meridian chart card — the ChartCardStyle wrapper: header row (title + optional small-caps
// OHLC/stat readout + actions) over a framed plot area. Hosts CandleChart / EquityCurve panes.
// Flat Concrete surface: white panel, hairline border, no shadow.
import type { CSSProperties, ReactNode } from "react";

export interface ChartReadoutItem {
  label: string;
  value: ReactNode;
  /** Override the value color (e.g. green / red). */
  color?: string;
}

export interface ChartCardProps {
  title: string;
  subtitle?: string;
  /** Inline OHLC / stat readout shown beside the title. */
  readout?: ChartReadoutItem[];
  /** Right-aligned controls (timeframe buttons, etc.). */
  actions?: ReactNode;
  /**
   * Plot-area height in px. Pass `null` to let the plot flex-fill a definite-height parent.
   * @default 320
   */
  height?: number | null;
  children?: ReactNode;
  style?: CSSProperties;
}

export function ChartCard({ title, subtitle, readout, actions, height = 320, children, style = {} }: ChartCardProps) {
  return (
    <div
      style={{
        background: "var(--card-surface, var(--panel, #FFFFFF))",
        border: "1px solid var(--border, #CBD3DC)",
        borderRadius: "var(--radius-card, 2px)",
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
        ...style
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 12,
          padding: "12px 14px",
          background: "var(--ws-surface-subtle, #EBEFF4)",
          borderBottom: "1px solid var(--chart-border, var(--border-strong, #99A5B2))"
        }}
      >
        <div style={{ minWidth: 0 }}>
          <div
            style={{
              fontFamily: "var(--font-display, inherit)",
              fontSize: 14,
              fontWeight: 600,
              color: "var(--text-primary, #22272E)"
            }}
          >
            {title}
          </div>
          {subtitle && <div style={{ fontSize: 11, color: "var(--text-muted, #59636F)", marginTop: 1 }}>{subtitle}</div>}
        </div>
        {readout && (
          <div style={{ display: "flex", gap: 16, marginLeft: 8, flexWrap: "wrap" }}>
            {readout.map((r, i) => (
              <div key={i} style={{ display: "flex", flexDirection: "column" }}>
                <span
                  style={{
                    fontFamily: "var(--font-body, inherit)",
                    fontSize: 9,
                    fontVariant: "all-small-caps",
                    letterSpacing: ".03em",
                    color: "var(--text-muted, #59636F)"
                  }}
                >
                  {r.label}
                </span>
                <span
                  style={{
                    fontFamily: "var(--font-data, monospace)",
                    fontSize: 12,
                    fontVariantNumeric: "slashed-zero tabular-nums",
                    color: r.color || "var(--text-primary, #22272E)"
                  }}
                >
                  {r.value}
                </span>
              </div>
            ))}
          </div>
        )}
        <div style={{ flex: 1 }} />
        {actions}
      </div>
      <div
        style={{
          height: height ?? undefined,
          flex: height == null ? 1 : undefined,
          minHeight: 0,
          padding: 8,
          background: "var(--chart-plot, #FFFFFF)"
        }}
      >
        {children}
      </div>
    </div>
  );
}

ChartCard.displayName = "ChartCard";
