// Meridian BarChart — categorical comparison bars (P&L by sector, fees by venue, fills per
// venue). Vertical SVG bars by default; `horizontal` renders ranked rows (label · bar · value)
// for exposure-style views. `signed` tints bars red/green by sign. Family data shape:
// plain `values` array + parallel `labels` (+ optional per-bar `colors` overrides).
import React from "react";
import { ChartFrame, AXIS_FONT } from "./ChartFrame.jsx";

function barColor(v, override, signed) {
  if (override) return override;
  if (signed) return v < 0 ? "var(--chart-drawdown)" : "var(--chart-equity)";
  return "var(--chart-primary)";
}

function HorizontalBars({ values, labels, colors, signed, valueFmt, showValues }) {
  const lo = Math.min(0, ...values), hi = Math.max(0, ...values);
  const span = hi - lo || 1;
  const pct = (v) => ((v - lo) / span) * 100;
  const zero = pct(0);
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 6, width: "100%" }}>
      {values.map((v, i) => {
        const p0 = Math.min(zero, pct(v)), p1 = Math.max(zero, pct(v));
        return (
          <div key={i} style={{ display: "grid", gridTemplateColumns: "minmax(72px,140px) 1fr 86px", gap: 10, alignItems: "center" }}>
            <span style={{ fontSize: 11, fontWeight: 600, fontVariant: "all-small-caps", letterSpacing: ".04em",
              color: "var(--text-secondary)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{labels[i] ?? ""}</span>
            <div style={{ position: "relative", height: 16, background: "var(--chart-plot)", border: "1px solid var(--border)" }}>
              <div style={{ position: "absolute", top: 0, bottom: 0, left: p0 + "%", width: Math.max(0.4, p1 - p0) + "%",
                background: barColor(v, colors && colors[i], signed), opacity: 0.75 }} />
              {lo < 0 && <div style={{ position: "absolute", top: 0, bottom: 0, left: zero + "%", width: 1, background: "var(--chart-border)" }} />}
            </div>
            {showValues
              ? <span style={{ fontFamily: "var(--font-data)", fontVariantNumeric: "tabular-nums", fontSize: 12, textAlign: "right",
                  color: signed && v < 0 ? "var(--red-dim)" : "var(--text-primary)" }}>{valueFmt(v)}</span>
              : <span />}
          </div>
        );
      })}
    </div>
  );
}

export function BarChart({ ariaLabel, ariaDescription, accessibleData,
  values = [],                 // number[]
  labels = [],                 // string[] parallel to values
  colors = null,               // (string|null)[] per-bar overrides
  horizontal = false,
  signed = false,
  valueFmt = (v) => String(v),
  showValues = true,
  valueTicks = 5,
  sort = null,                 // "asc" | "desc" | null (keep given order)
  height = 300,
}) {
  let order = values.map((_, i) => i);
  if (sort === "desc") order.sort((a, b) => values[b] - values[a]);
  if (sort === "asc") order.sort((a, b) => values[a] - values[b]);
  const vals = order.map((i) => values[i]);
  const labs = order.map((i) => labels[i] ?? "");
  const cols = colors ? order.map((i) => colors[i]) : null;

  if (horizontal) return <HorizontalBars values={vals} labels={labs} colors={cols} signed={signed} valueFmt={valueFmt} showValues={showValues} />;

  const n = vals.length || 1;
  const lo0 = Math.min(0, ...vals), hi0 = Math.max(0, ...vals);
  const p = (hi0 - lo0 || 1) * 0.08;
  const lo = lo0 < 0 ? lo0 - p : lo0, hi = hi0 > 0 ? hi0 + p : hi0;
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={n} xMode="band" xLabels={labs} xTicks={14}
      rail={50} railSide="left" padL={10} padT={22} leftRule
      panes={[{ key: "v", domain: [lo, hi], ticks: valueTicks, fmt: valueFmt, zero: 0 }]}>
      {(g) => {
        const P = g.pane("v");
        const barW = Math.min(g.band * 0.62, 88);
        return (
          <g>
            {vals.map((v, i) => {
              const cx = g.x(i);
              const top = Math.min(P.y(0), P.y(v)), bot = Math.max(P.y(0), P.y(v));
              return (
                <g key={i}>
                  <rect x={cx - barW / 2} y={top} width={barW} height={Math.max(0.75, bot - top)}
                    fill={barColor(v, cols && cols[i], signed)} opacity="0.75" />
                  {showValues && (
                    <text x={cx} y={v >= 0 ? top - 5 : bot + 13} fill="var(--text-secondary)" textAnchor="middle"
                      style={{ ...AXIS_FONT, fontWeight: 600 }}>{valueFmt(v)}</text>
                  )}
                </g>
              );
            })}
          </g>
        );
      }}
    </ChartFrame>
  );
}

BarChart.displayName = "BarChart";
