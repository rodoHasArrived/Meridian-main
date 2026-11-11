// Meridian equity curve — line/area chart with benchmark overlay, value axis labels,
// gridlines, legend, crosshair readout, and an optional drawdown subpane that hangs
// below its own waterline. Renders through ChartFrame: real-pixel, shared ticks.
import React from "react";
import { Fmt, domainOf, linePath } from "./viz.js";
import { ChartFrame, AxisChip, AXIS_FONT } from "./ChartFrame.jsx";

export function EquityCurve({ ariaLabel, ariaDescription, accessibleData,
  series,                      // [{ label, color, points:number[], dashed?, area? }]
  labels = [],
  drawdown = null,             // number[] ≤ 0 (percent), rendered as a subpane
  valueFmt = Fmt.price,
  crosshairIndex = null,
  valueTicks = 6,
  timeTicks = 7,
  showLegend = true,
  fill = true,
  baseline = null,
  baselineLabel = null,
  showLast = true,
  height = 380,
  rail = 62,
}) {
  const n = series[0].points.length;
  const domain = domainOf(series.map((s) => s.points), 0.08);
  const ddMin = drawdown ? Math.min(...drawdown, 0) || -1 : -1;
  const panes = [
    { key: "eq", flex: 3, domain, ticks: valueTicks, fmt: valueFmt },
    ...(drawdown ? [{ key: "dd", flex: 0.9, domain: [ddMin, 0], ticks: 0, zero: 0 }] : []),
  ];
  const legend = showLegend ? [
    ...series.map((s) => ({ label: s.label, color: s.color, dashed: s.dashed })),
    ...(drawdown ? [{ label: "drawdown", color: "var(--chart-drawdown)", swatch: true }] : []),
  ] : null;
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={n} xLabels={labels} xTicks={timeTicks} rail={rail}
      panes={panes} legend={legend} crosshairIndex={crosshairIndex} leftRule>
      {(g) => {
        const E = g.pane("eq"), D = drawdown ? g.pane("dd") : null;
        const line = (pts) => linePath(pts, g.x, E.y);
        const last = series[0].points[n - 1];
        return (
          <g>
            {baseline != null && baseline >= E.domain[0] && baseline <= E.domain[1] && (
              <g>
                <line x1={g.plotL} x2={g.plotR} y1={E.y(baseline)} y2={E.y(baseline)} stroke="var(--chart-axis)" strokeWidth="1" strokeDasharray="3 3" opacity="0.6" />
                <text x={g.plotL + 4} y={E.y(baseline) - 5} fill="var(--chart-axis)" style={AXIS_FONT}>{baselineLabel || valueFmt(baseline)}</text>
              </g>
            )}
            {fill && series[0].area !== false && (
              <path d={`${line(series[0].points)}L${g.x(n - 1).toFixed(1)} ${E.b}L${g.x(0).toFixed(1)} ${E.b}Z`} fill={series[0].color} opacity="0.10" />
            )}
            {series.map((s, k) => (
              <path key={k} d={line(s.points)} fill="none" stroke={s.color} strokeWidth={k === 0 ? 2 : 1.5}
                strokeDasharray={s.dashed ? "5 4" : undefined} />
            ))}
            {showLast && last != null && (
              <g>
                <line x1={g.plotL} x2={g.x(n - 1)} y1={E.y(last)} y2={E.y(last)} stroke={series[0].color} strokeWidth="0.8" strokeDasharray="2 3" opacity="0.45" />
                <circle cx={g.x(n - 1)} cy={E.y(last)} r="3.5" fill={series[0].color} />
                <AxisChip x={g.plotR + 2} y={E.y(last)} width={rail - 4} text={valueFmt(last)} fill={series[0].color} rx={0} />
              </g>
            )}
            {D && (
              <g>
                {/* dd pane scale: 0 at its own waterline (pane top), min at pane bottom */}
                <path d={`M${g.x(0).toFixed(1)} ${D.y(0).toFixed(1)}${drawdown.map((v, i) => `L${g.x(i).toFixed(1)} ${D.y(v).toFixed(1)}`).join("")}L${g.x(n - 1).toFixed(1)} ${D.y(0).toFixed(1)}Z`} fill="var(--chart-drawdown)" opacity="0.20" />
                <path d={linePath(drawdown, g.x, D.y)} fill="none" stroke="var(--chart-drawdown)" strokeWidth="1.4" />
                <text x={g.plotR + 8} y={D.y(0) + 5} fill="var(--chart-axis)" textAnchor="start" style={AXIS_FONT}>0%</text>
                <text x={g.plotR + 8} y={D.b} fill="var(--chart-axis)" textAnchor="start" style={AXIS_FONT}>{ddMin.toFixed(0)}%</text>
              </g>
            )}
            {crosshairIndex != null && series[0].points[crosshairIndex] != null && (
              <g>
                {series.map((s, k) => <circle key={k} cx={g.x(crosshairIndex)} cy={E.y(s.points[crosshairIndex])} r="4" fill={s.color} opacity="0.9" />)}
                <AxisChip x={g.plotR + 2} y={E.y(series[0].points[crosshairIndex])} width={rail - 4} text={valueFmt(series[0].points[crosshairIndex])} />
              </g>
            )}
          </g>
        );
      }}
    </ChartFrame>
  );
}

EquityCurve.displayName = "EquityCurve";
