// Meridian BarChart — categorical comparison bars (P&L by sector, fees by venue, fills per
// venue). Vertical SVG bars by default; `horizontal` renders ranked rows (label · bar · value)
// for exposure-style views. `signed` tints bars red/green by sign. Flat, token-driven.
import React from "react";

function niceTicks(lo, hi, n) {
  const span = hi - lo || 1;
  const raw = span / Math.max(1, n);
  const p = Math.pow(10, Math.floor(Math.log10(raw)));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * p).find((c) => c >= raw) || 10 * p;
  const out = [];
  for (let v = Math.ceil(lo / step) * step; v <= hi + step * 1e-6; v += step) out.push(+v.toFixed(10));
  return out;
}

function barColor(d, signed) {
  if (d.color) return d.color;
  if (signed) return d.value < 0 ? "var(--chart-drawdown)" : "var(--chart-equity)";
  return "var(--chart-primary)";
}

function HorizontalBars({ data, signed, valueFmt, showValues }) {
  const vals = data.map((d) => d.value);
  const lo = Math.min(0, ...vals), hi = Math.max(0, ...vals);
  const span = (hi - lo) || 1;
  const pct = (v) => ((v - lo) / span) * 100;
  const zero = pct(0);
  const mono = { fontFamily: "var(--font-data)", fontVariantNumeric: "tabular-nums" };
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 6, width: "100%" }}>
      {data.map((d, i) => {
        const p0 = Math.min(zero, pct(d.value)), p1 = Math.max(zero, pct(d.value));
        return (
          <div key={i} style={{ display: "grid", gridTemplateColumns: "minmax(72px,140px) 1fr 86px", gap: 10, alignItems: "center" }}>
            <span style={{ fontSize: 11, fontWeight: 600, fontVariant: "all-small-caps", letterSpacing: ".04em",
              color: "var(--text-secondary)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{d.label}</span>
            <div style={{ position: "relative", height: 16, background: "var(--chart-plot)", border: "1px solid var(--border)" }}>
              <div style={{ position: "absolute", top: 0, bottom: 0, left: p0 + "%", width: Math.max(0.4, p1 - p0) + "%",
                background: barColor(d, signed), opacity: 0.75 }} />
              {lo < 0 && <div style={{ position: "absolute", top: 0, bottom: 0, left: zero + "%", width: 1, background: "var(--chart-border)" }} />}
            </div>
            {showValues
              ? <span style={{ ...mono, fontSize: 12, textAlign: "right", color: signed && d.value < 0 ? "var(--red-dim)" : "var(--text-primary)" }}>{valueFmt(d.value)}</span>
              : <span />}
          </div>
        );
      })}
    </div>
  );
}

export function BarChart({
  data = [],
  horizontal = false,
  signed = false,
  valueFmt = (v) => String(v),
  showValues = true,
  valueTicks = 5,
  sort = null,                   // "asc" | "desc" | null (keep given order)
}) {
  let rows = data.slice();
  if (sort === "desc") rows.sort((a, b) => b.value - a.value);
  if (sort === "asc") rows.sort((a, b) => a.value - b.value);

  if (horizontal) return <HorizontalBars data={rows} signed={signed} valueFmt={valueFmt} showValues={showValues} />;

  const W = 960, H = 340;
  const padT = 22, axisB = 26, axisL = 50, axisR = 10;
  const plotL = axisL, plotR = W - axisR, plotT = padT, plotB = H - axisB;
  const n = rows.length || 1;
  const vals = rows.map((d) => d.value);
  const lo0 = Math.min(0, ...vals), hi0 = Math.max(0, ...vals);
  const pad = (hi0 - lo0 || 1) * 0.08;
  const lo = lo0 < 0 ? lo0 - pad : lo0, hi = hi0 > 0 ? hi0 + pad : hi0;
  const span = (hi - lo) || 1;
  const y = (v) => plotB - ((v - lo) / span) * (plotB - plotT);
  const band = (plotR - plotL) / n;
  const barW = Math.min(band * 0.62, 88);
  const ticks = niceTicks(lo, hi, valueTicks);
  const labelFont = { fontFamily: "var(--font-data)", fontSize: 11 };
  const step = Math.max(1, Math.ceil(n / 14));

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", height: "100%", display: "block" }}>
      <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
      {ticks.map((t, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={y(t)} y2={y(t)} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.55" />
          <text x={plotL - 6} y={y(t) + 4} fill="var(--chart-axis)" textAnchor="end" style={labelFont}>{valueFmt(t)}</text>
        </g>
      ))}

      {rows.map((d, i) => {
        const cx = plotL + band * i + band / 2;
        const top = Math.min(y(0), y(d.value)), bot = Math.max(y(0), y(d.value));
        return (
          <g key={i}>
            <rect x={cx - barW / 2} y={top} width={barW} height={Math.max(0.75, bot - top)}
              fill={barColor(d, signed)} opacity="0.75" />
            {showValues && (
              <text x={cx} y={d.value >= 0 ? top - 5 : bot + 13} fill="var(--text-secondary)" textAnchor="middle"
                style={{ ...labelFont, fontWeight: 600 }}>{valueFmt(d.value)}</text>
            )}
          </g>
        );
      })}

      {/* category labels */}
      {rows.map((d, i) => (i % step === 0) ? (
        <text key={"x" + i} x={plotL + band * i + band / 2} y={H - 9} fill="var(--chart-axis)" textAnchor="middle" style={labelFont}>{d.label}</text>
      ) : null)}

      {/* zero / base line */}
      <line x1={plotL} x2={plotR} y1={y(Math.max(lo, Math.min(hi, 0)))} y2={y(Math.max(lo, Math.min(hi, 0)))}
        stroke="var(--chart-border)" strokeWidth="1.2" />
      <line x1={plotL} x2={plotL} y1={plotT} y2={plotB} stroke="var(--chart-border)" strokeWidth="1" opacity="0.5" />
    </svg>
  );
}

BarChart.displayName = "BarChart";
