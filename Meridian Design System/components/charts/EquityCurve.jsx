// Meridian equity curve — line/area chart with benchmark overlay, value axis labels,
// gridlines, legend, crosshair readout, and an optional drawdown subpane. Mirrors the
// performance charts in AdvancedAnalytics / backtest reporting.
import React from "react";

function niceTicks(min, max, count) {
  const span = max - min || 1;
  const raw = span / count;
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const norm = raw / mag;
  const step = (norm >= 5 ? 5 : norm >= 2 ? 2 : 1) * mag;
  const start = Math.ceil(min / step) * step;
  const out = [];
  for (let v = start; v <= max + 1e-9; v += step) out.push(+v.toFixed(6));
  return out;
}

export function EquityCurve({
  series, labels = [], drawdown = null, valueFmt = (v) => v.toFixed(0),
  crosshairIndex = null, valueTicks = 6, timeTicks = 7, showLegend = true, fill = true,
  baseline = null, baselineLabel = null, showLast = true,
}) {
  // series: [{ label, color, points:number[], dashed?, area? }]
  const W = 960, H = 460;
  const padL = 48, padT = 14, axisR = 62, axisB = 28;
  const ddH = drawdown ? 88 : 0, ddGap = drawdown ? 12 : 0;
  const plotL = padL, plotR = W - axisR;
  const eqT = padT, eqB = H - axisB - ddH - ddGap;
  const ddT = eqB + ddGap, ddB = H - axisB;

  const n = series[0].points.length;
  const allV = series.flatMap(s => s.points);
  let max = Math.max(...allV), min = Math.min(...allV);
  const pv = (max - min) * 0.08; max += pv; min -= pv;

  const x = (i) => plotL + i * ((plotR - plotL) / (n - 1));
  const y = (v) => eqT + (max - v) * ((eqB - eqT) / (max - min));
  const line = (pts) => pts.map((v, i) => `${i === 0 ? "M" : "L"}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  const area = (pts) => `${line(pts)} L${x(n - 1).toFixed(1)},${eqB} L${x(0).toFixed(1)},${eqB} Z`;

  const vTicks = niceTicks(min, max, valueTicks);
  const tStep = Math.max(1, Math.round(n / timeTicks));
  const axFont = { fontFamily: "var(--font-data)", fontSize: 12, fontWeight: 500, fontVariantNumeric: "slashed-zero tabular-nums" };
  const labelFont = { fontFamily: "var(--font-data)", fontSize: 11, fontVariantNumeric: "slashed-zero tabular-nums" };

  // drawdown subpane scale (drawdown values are <= 0)
  const ddMin = drawdown ? Math.min(...drawdown, 0) : 0;
  const yD = (v) => ddT + (0 - v) * ((ddB - ddT) / (ddMin || -1));
  const ddArea = drawdown ? `M${x(0).toFixed(1)},${yD(0).toFixed(1)} ${drawdown.map((v, i) => `L${x(i).toFixed(1)},${yD(v).toFixed(1)}`).join(" ")} L${x(n - 1).toFixed(1)},${yD(0).toFixed(1)} Z` : "";

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      {showLegend && (
        <div style={{ display: "flex", gap: 16, padding: "0 8px 8px", flexWrap: "wrap" }}>
          {series.map((s, k) => (
            <span key={k} style={{ display: "inline-flex", alignItems: "center", gap: 6, fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-secondary)" }}>
              <span style={{ width: 14, height: 0, borderTop: `2px ${s.dashed ? "dashed" : "solid"} ${s.color}` }} />
              {s.label}
            </span>
          ))}
          {drawdown && (
            <span style={{ display: "inline-flex", alignItems: "center", gap: 6, fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-secondary)" }}>
              <span style={{ width: 14, height: 8, background: "var(--chart-drawdown)", opacity: 0.25 }} />drawdown
            </span>
          )}
        </div>
      )}
      <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", flex: 1, display: "block" }}>
        <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
        {/* value gridlines + labels */}
        {vTicks.map((v, k) => (
          <g key={k}>
            <line x1={plotL} x2={plotR} y1={y(v)} y2={y(v)} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.65" />
            <text x={plotR + 8} y={y(v) + 5} fill="var(--chart-axis)" textAnchor="start" style={labelFont}>{valueFmt(v)}</text>
          </g>
        ))}
        {/* time labels */}
        {labels.map((lab, i) => (i % tStep === 0 || i === n - 1) ? (
          <g key={"t" + i}>
            <line x1={x(i)} x2={x(i)} y1={eqT} y2={eqB} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.55" />
            <text x={x(i)} y={H - 8} fill="var(--chart-axis)" textAnchor="middle" style={labelFont}>{lab}</text>
          </g>
        ) : null)}
        <line x1={plotR} x2={plotR} y1={eqT} y2={ddB} stroke="var(--chart-border)" strokeWidth="1.2" />
        <line x1={plotL - 1} x2={plotR} y1={eqB} y2={eqB} stroke="var(--chart-border)" strokeWidth="1.2" />
        <line x1={plotL - 1} x2={plotL - 1} y1={eqT} y2={eqB} stroke="var(--chart-border)" strokeWidth="1" opacity="0.4" />

        {/* baseline / cost-basis reference */}
        {baseline != null && baseline >= min && baseline <= max && (
          <g>
            <line x1={plotL} x2={plotR} y1={y(baseline)} y2={y(baseline)} stroke="var(--chart-axis)" strokeWidth="1" strokeDasharray="3 3" opacity="0.6" />
            <text x={plotL + 4} y={y(baseline) - 5} fill="var(--chart-axis)" style={labelFont}>{baselineLabel || valueFmt(baseline)}</text>
          </g>
        )}

        {/* area fill for the first (primary) series */}
        {fill && series[0].area !== false && (
          <path d={area(series[0].points)} fill={series[0].color} opacity="0.10" />
        )}
        {/* series lines */}
        {series.map((s, k) => (
          <path key={k} d={line(s.points)} fill="none" stroke={s.color} strokeWidth={k === 0 ? 2 : 1.5}
            strokeDasharray={s.dashed ? "5 4" : undefined} />
        ))}

        {/* persistent last-value marker + right-axis chip (anchors the curve like the candle chart) */}
        {showLast && series[0].points[n - 1] != null && (
          <g>
            <line x1={plotL} x2={x(n - 1)} y1={y(series[0].points[n - 1])} y2={y(series[0].points[n - 1])} stroke={series[0].color} strokeWidth="0.8" strokeDasharray="2 3" opacity="0.45" />
            <circle cx={x(n - 1)} cy={y(series[0].points[n - 1])} r="3.5" fill={series[0].color} />
            <rect x={plotR + 2} y={y(series[0].points[n - 1]) - 10} width={axisR - 4} height="20" fill={series[0].color} />
            <text x={plotR + axisR / 2} y={y(series[0].points[n - 1]) + 5} fill="#fff" textAnchor="middle" style={{ ...axFont, fontWeight: 600, fontSize: 13 }}>{valueFmt(series[0].points[n - 1])}</text>
          </g>
        )}

        {/* drawdown subpane */}
        {drawdown && (
          <g>
            <path d={ddArea} fill="var(--chart-drawdown)" opacity="0.20" />
            <path d={`M${x(0).toFixed(1)},${yD(drawdown[0]).toFixed(1)} ${drawdown.map((v, i) => `L${x(i).toFixed(1)},${yD(v).toFixed(1)}`).join(" ")}`} fill="none" stroke="var(--chart-drawdown)" strokeWidth="1.4" />
            <line x1={plotL - 1} x2={plotR} y1={yD(0)} y2={yD(0)} stroke="var(--chart-border)" strokeWidth="1" opacity="0.6" />
            <text x={plotR + 8} y={yD(0) + 5} fill="var(--chart-axis)" textAnchor="start" style={labelFont}>0%</text>
            <text x={plotR + 8} y={ddB} fill="var(--chart-axis)" textAnchor="start" style={labelFont}>{ddMin.toFixed(0)}%</text>
          </g>
        )}

        {/* crosshair */}
        {crosshairIndex != null && series[0].points[crosshairIndex] != null && (
          <g>
            <line x1={x(crosshairIndex)} x2={x(crosshairIndex)} y1={eqT} y2={ddB} stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="4 2" opacity="0.8" />
            {series.map((s, k) => <circle key={k} cx={x(crosshairIndex)} cy={y(s.points[crosshairIndex])} r="4" fill={s.color} opacity="0.9" />)}
            <rect x={plotR + 2} y={y(series[0].points[crosshairIndex]) - 10} width={axisR - 4} height="20" fill="var(--chart-crosshair)" rx="4" opacity="0.95" />
            <text x={plotR + axisR / 2} y={y(series[0].points[crosshairIndex]) + 5} fill="#fff" textAnchor="middle" style={{ ...axFont, fontWeight: 600, fontSize: 13 }}>{valueFmt(series[0].points[crosshairIndex])}</text>
          </g>
        )}
      </svg>
    </div>
  );
}
