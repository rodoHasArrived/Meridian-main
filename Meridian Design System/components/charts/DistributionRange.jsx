// Meridian DistributionRange — uncertainty made visible, in the two shapes it actually takes.
//   mode="fan"  a projection over TIME: nested probability bands around a model median, with
//               the observed path overlaid so forecast and fact never share a treatment.
//   mode="box"  a five-number summary per CATEGORY: whisker, box, median rule, outliers.
// Boundary: IntervalBar covers quantile ROWS for one metric on a shared domain (no time axis,
// no categories). Reach here when the x-axis carries meaning.
// Model uncertainty uses the --viz-band-* tokens and is never drawn in a condition colour —
// a widening band is not a warning state.
import React from "react";
import { domainOf, linePath, bandPath, roleStyle } from "./viz.js";
import { ChartFrame, AXIS_FONT } from "./ChartFrame.jsx";

export function DistributionRange({
  mode = "fan",
  median = [],                 // fan · model central path
  bands = [],                  // fan · [{ lower:[], upper:[], label }] ordered OUTER → inner
  actual = null,               // fan · observed path (nulls where the future starts)
  labels = [],                 // fan · x labels per index
  boxes = [],                  // box · [{ label, min, q1, median, q3, max, outliers?, kind? }]
  domain: domainProp = null,
  fmt = (v) => v.toFixed(1),
  height = 300,
  zero = null,
  rail = 62,
  note = null,
  crosshairIndex = null,
  ariaLabel, ariaDescription, accessibleData,
}) {
  const isFan = mode === "fan";
  const pools = isFan
    ? [median, actual || [], ...bands.flatMap((b) => [b.lower, b.upper])]
    : [boxes.map((b) => b.min), boxes.map((b) => b.max), boxes.flatMap((b) => b.outliers || [])];
  const domain = domainProp || domainOf(pools);
  const n = isFan ? Math.max(median.length, ...bands.map((b) => (b.upper || []).length), 1) : Math.max(1, boxes.length);
  const xLabels = isFan ? labels : boxes.map((b) => b.label);
  const legend = isFan
    ? [
      ...bands.map((b, i) => ({ label: b.label || `band ${i + 1}`, color: "var(--viz-band-outline)", swatch: true })),
      { label: "Model median", color: roleStyle("estimated").stroke, dashed: true },
      ...(actual ? [{ label: "Observed", color: roleStyle("actual").stroke }] : []),
    ]
    : null;

  return (
    <div>
      <ChartFrame height={height} n={n} xMode={isFan ? "point" : "band"} xLabels={xLabels} rail={rail}
        legend={legend} crosshairIndex={crosshairIndex}
        ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData}
        panes={[{ key: "d", domain, ticks: 5, fmt, zero }]}>
        {(g) => {
          const D = g.pane("d");
          if (isFan) {
            const est = roleStyle("estimated"), act = roleStyle("actual");
            return (
              <g>
                {bands.map((b, i) => {
                  const strength = bands.length > 1 ? i / (bands.length - 1) : 1;
                  return (
                    <g key={i}>
                      <path d={bandPath(b.upper, b.lower, g.x, D.y)}
                        fill={strength > 0.5 ? "var(--viz-band-inner)" : "var(--viz-band-outer)"} />
                      <path d={linePath(b.upper, g.x, D.y)} fill="none" stroke="var(--viz-band-outline)" strokeWidth="1" strokeDasharray="3 3" opacity="0.8" />
                      <path d={linePath(b.lower, g.x, D.y)} fill="none" stroke="var(--viz-band-outline)" strokeWidth="1" strokeDasharray="3 3" opacity="0.8" />
                    </g>
                  );
                })}
                {median.length > 0 && (
                  <path d={linePath(median, g.x, D.y)} fill="none" stroke={est.stroke} strokeWidth={est.width} strokeDasharray={est.dash} strokeLinejoin="round" />
                )}
                {actual && (
                  <path d={linePath(actual, g.x, D.y)} fill="none" stroke={act.stroke} strokeWidth={act.width} strokeLinejoin="round" strokeLinecap="round" />
                )}
              </g>
            );
          }
          const bw = Math.min(46, g.band * 0.54);
          return (
            <g>
              {boxes.map((b, i) => {
                const cx = g.x(i);
                const col = b.kind === "positive" ? "var(--series-positive)" : b.kind === "negative" ? "var(--series-negative)" : "var(--chart-primary)";
                const q1 = D.y(b.q1), q3 = D.y(b.q3);
                return (
                  <g key={b.label || i}>
                    <line x1={cx} x2={cx} y1={D.y(b.max)} y2={D.y(b.min)} stroke={col} strokeWidth="1" opacity="0.75" />
                    <line x1={cx - bw / 4} x2={cx + bw / 4} y1={D.y(b.max)} y2={D.y(b.max)} stroke={col} strokeWidth="1.25" />
                    <line x1={cx - bw / 4} x2={cx + bw / 4} y1={D.y(b.min)} y2={D.y(b.min)} stroke={col} strokeWidth="1.25" />
                    <rect x={cx - bw / 2} y={Math.min(q1, q3)} width={bw} height={Math.max(1, Math.abs(q1 - q3))}
                      fill={col} fillOpacity="0.16" stroke={col} strokeWidth="1.25" />
                    <line x1={cx - bw / 2} x2={cx + bw / 2} y1={D.y(b.median)} y2={D.y(b.median)} stroke={col} strokeWidth="var(--viz-stroke-datum,2px)" />
                    {(b.outliers || []).map((o, k) => (
                      <circle key={k} cx={cx} cy={D.y(o)} r="var(--viz-marker-dense,3px)" fill="none" stroke={col} strokeWidth="1" opacity="0.8" />
                    ))}
                    <text x={cx} y={Math.min(q1, q3) - 6} textAnchor="middle" fill="var(--viz-annotation,var(--text-secondary))"
                      style={{ ...AXIS_FONT, fontSize: "var(--viz-type-micro,10px)", paintOrder: "stroke", stroke: "var(--viz-halo,var(--bg-light))", strokeWidth: 3 }}>{fmt(b.median)}</text>
                  </g>
                );
              })}
            </g>
          );
        }}
      </ChartFrame>
      {note && <p style={{ margin: "8px 0 0", fontSize: "var(--viz-type-annotation,12px)", color: "var(--text-secondary)", maxWidth: "68ch" }}>{note}</p>}
    </div>
  );
}

DistributionRange.displayName = "DistributionRange";
