// Meridian WaterfallChart — additive bridge from a starting level through signed contributions
// to a net (P&L attribution: gross → carry → funding → fees → net; NAV bridges). Items are
// `start` (absolute), `delta` (floats from the running level), or `total` (drops to zero);
// a computed Net total is appended unless one is provided. Renders through ChartFrame.
import React from "react";
import { ChartFrame, AXIS_FONT } from "./ChartFrame.jsx";

export function WaterfallChart({ ariaLabel, ariaDescription, accessibleData,
  items = [],                    // [{ label, value, kind?: "start" | "delta" | "total", color? }]
  showTotal = true,
  totalLabel = "Net",
  valueFmt = (v) => v.toFixed(1),
  deltaFmt = null,               // default: explicit-sign valueFmt
  valueTicks = 5,
  showConnectors = true,
  height = 300,
}) {
  const dFmt = deltaFmt || ((v) => (v < 0 ? "\u2212" : "+") + valueFmt(Math.abs(v)));
  let cum = 0;
  const steps = items.map((it) => {
    const kind = it.kind || "delta";
    if (kind === "start") { cum = it.value; return { ...it, kind, from: 0, to: it.value, level: cum }; }
    if (kind === "total") { return { ...it, kind, from: 0, to: cum, level: cum, value: cum }; }
    const from = cum; cum += it.value;
    return { ...it, kind, from, to: cum, level: cum };
  });
  if (showTotal && !items.some((it) => it.kind === "total")) {
    steps.push({ label: totalLabel, kind: "total", from: 0, to: cum, level: cum, value: cum });
  }
  const n = steps.length || 1;
  const edges = steps.flatMap((s) => [s.from, s.to]);
  const lo0 = Math.min(0, ...edges), hi0 = Math.max(0, ...edges);
  const p = (hi0 - lo0 || 1) * 0.09;
  const lo = lo0 < 0 ? lo0 - p : lo0, hi = hi0 + p;
  const color = (s) => {
    if (s.color) return s.color;
    if (s.kind !== "delta") return "var(--chart-primary)";
    return s.value < 0 ? "var(--chart-drawdown)" : "var(--chart-equity)";
  };
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={n} xMode="band" xLabels={steps.map((s) => s.label)} xTicks={99}
      railSide="left" rail={50} padL={10} padT={22} leftRule
      panes={[{ key: "v", domain: [lo, hi], ticks: valueTicks, fmt: valueFmt, zero: 0 }]}>
      {(g) => {
        const P = g.pane("v");
        const barW = Math.min(g.band * 0.58, 84);
        return (
          <g>
            {showConnectors && steps.slice(0, -1).map((s, i) => {
              const x0 = g.x(i) + barW / 2, x1 = g.x(i + 1) - barW / 2;
              const yy = P.y(steps[i + 1].kind === "total" ? steps[i + 1].to : s.to);
              return <line key={"c" + i} x1={x0} x2={x1} y1={yy} y2={yy}
                stroke="var(--chart-axis)" strokeWidth="0.9" strokeDasharray="3 3" opacity="0.6" />;
            })}
            {steps.map((s, i) => {
              const cx = g.x(i);
              const top = Math.min(P.y(s.from), P.y(s.to)), bot = Math.max(P.y(s.from), P.y(s.to));
              const labelUp = s.kind === "delta" ? s.value >= 0 : s.to >= 0;
              return (
                <g key={i}>
                  <rect x={cx - barW / 2} y={top} width={barW} height={Math.max(0.75, bot - top)}
                    fill={color(s)} opacity="0.78" />
                  <text x={cx} y={labelUp ? top - 5 : bot + 13} fill="var(--text-secondary)" textAnchor="middle"
                    style={{ ...AXIS_FONT, fontWeight: 600 }}>
                    {s.kind === "delta" ? dFmt(s.value) : valueFmt(s.to)}
                  </text>
                </g>
              );
            })}
          </g>
        );
      }}
    </ChartFrame>
  );
}

WaterfallChart.displayName = "WaterfallChart";
