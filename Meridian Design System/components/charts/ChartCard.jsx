// Meridian chart card — the ChartCardStyle wrapper: header row (title + OHLC/price readout
// or legend) over a framed plot area. Use to host CandleChart / EquityCurve panes.
import React from "react";

export function ChartCard({ title, subtitle, readout, actions, height = 320, children, style = {} }) {
  return (
    <div style={{
      background: "var(--card-surface)", border: "1px solid var(--border)",
      borderRadius: "var(--radius-card, 8px)", boxShadow: "var(--shadow-card)",
      display: "flex", flexDirection: "column", overflow: "hidden", ...style
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 12, padding: "12px 14px", borderBottom: "1px solid var(--border)" }}>
        <div style={{ minWidth: 0 }}>
          <div style={{ fontFamily: "var(--font-display)", fontSize: 14, fontWeight: 600, color: "var(--text-primary)" }}>{title}</div>
          {subtitle && <div style={{ fontSize: 11, color: "var(--text-muted)", marginTop: 1 }}>{subtitle}</div>}
        </div>
        {readout && <div style={{ display: "flex", gap: 16, marginLeft: 8, flexWrap: "wrap" }}>
          {readout.map((r, i) => (
            <div key={i} style={{ display: "flex", flexDirection: "column" }}>
              <span style={{ fontFamily: "var(--font-body)", fontSize: 9, fontVariant: "all-small-caps", letterSpacing: ".03em", color: "var(--text-muted)" }}>{r.label}</span>
              <span style={{ fontFamily: "var(--font-data)", fontSize: 12, fontVariantNumeric: "tabular-nums", color: r.color || "var(--text-primary)" }}>{r.value}</span>
            </div>
          ))}
        </div>}
        <div style={{ flex: 1 }} />
        {actions}
      </div>
      <div style={{ height: height ?? undefined, flex: height == null ? 1 : undefined, minHeight: 0, padding: 8, background: "var(--chart-plot)" }}>{children}</div>
    </div>
  );
}
