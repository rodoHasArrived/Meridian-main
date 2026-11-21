// Meridian ChartAnnotations — the overlay that turns a chart into an argument: regime bands,
// event rules with badges, and leader-line callouts. It draws INSIDE a ChartFrame children
// render prop, using the frame's own geometry, so annotations land on real pixel positions and
// survive resize without a second scale implementation.
//
//   <ChartFrame …>{(g) => (<>
//     <MyMarks g={g} />
//     <ChartAnnotations g={g} pane="p" events={events} callouts={callouts} />
//   </>)}</ChartFrame>
//
// Annotation ink is deliberately NOT series ink: callout text takes --viz-annotation with a
// halo, so a label crossing a trace stays readable without hiding the data under a filled box.
import React from "react";

const AXIS = { fontFamily: "var(--font-data)", fontSize: "var(--viz-type-annotation,12px)", fontVariantNumeric: "slashed-zero tabular-nums" };
const HALO = { paintOrder: "stroke", stroke: "var(--viz-halo,var(--bg-light))", strokeWidth: 3.5, strokeLinejoin: "round" };
const KIND = {
  event: "var(--viz-annotation,var(--text-secondary))",
  positive: "var(--series-positive)",
  negative: "var(--series-negative)",
  warning: "var(--series-warning)",
  information: "var(--series-information)",
  scenario: "var(--series-scenario)",
};
const kindColor = (k) => KIND[k] || KIND.event;

export function ChartAnnotations({
  g,                           // the geometry object from ChartFrame's children render prop
  pane = null,                 // pane key; defaults to the full plot area
  bands = [],                  // [{ from, to, label?, kind? }] — x-index regime shading
  events = [],                 // [{ index, label?, badge?, kind?, detail? }] — vertical rules
  callouts = [],               // [{ index, value, text, dx?, dy?, anchor? }] — leader + text
  showRules = true,
  badgeY = null,               // override badge baseline (defaults to pane top)
}) {
  const uid = React.useId();
  const ref = React.useRef(null);
  const accessiblePane = g && pane ? g.pane(pane) : null;
  const description = [
    ...bands.map((band,index) => `${band.label || `Band ${index+1}`} (${band.kind || "event"}), positions ${band.from} to ${band.to}`),
    ...events.map((event,index) => `Event ${event.badge ?? index+1}${event.label ? `: ${event.label}` : ""} (${event.kind || "event"}), position ${event.index}${event.detail ? `: ${event.detail}` : ""}`),
    ...(accessiblePane?.y ? callouts.map(callout => `${callout.text}${callout.kind ? ` (${callout.kind})` : ""}, position ${callout.index}, value ${callout.value}`) : []),
  ].join(". ");
  React.useLayoutEffect(() => {
    const svg = ref.current?.ownerSVGElement;
    if (!svg || !description) return;
    const include = () => {
      const ids = (svg.getAttribute("aria-describedby") || "").split(/\s+/).filter(Boolean);
      if (!ids.includes(uid)) svg.setAttribute("aria-describedby", [...ids, uid].join(" "));
    };
    include();
    // ChartFrame owns this attribute and may update its own description. Keep
    // each overlay's description registered without replacing the frame's ids.
    const observer = new MutationObserver(include);
    observer.observe(svg, { attributes: true, attributeFilter: ["aria-describedby"] });
    return () => {
      observer.disconnect();
      const ids = (svg.getAttribute("aria-describedby") || "").split(/\s+/).filter(id => id && id !== uid);
      if (ids.length) svg.setAttribute("aria-describedby", ids.join(" "));
      else svg.removeAttribute("aria-describedby");
    };
  }, [uid, description, g]);
  if (!g) return null;
  const p = pane ? g.pane(pane) : null;
  const t = p ? p.t : g.innerT;
  const b = p ? p.b : g.innerB;
  const y = p ? p.y : null;
  const by = badgeY != null ? badgeY : t;
  const clampX = (x) => Math.max(g.plotL, Math.min(g.plotR, x));

  return (
    <g ref={ref}>
      {description ? <desc id={uid}>{description}</desc> : null}
      {bands.map((bd, i) => {
        const x0 = clampX(g.x(bd.from)), x1 = clampX(g.x(bd.to));
        const c = kindColor(bd.kind);
        return (
          <g key={"b" + i}>
            <rect x={Math.min(x0, x1)} y={t} width={Math.abs(x1 - x0)} height={b - t}
              fill={c} opacity="0.07" />
            <line x1={x0} x2={x0} y1={t} y2={b} stroke={c} strokeWidth="1" opacity="0.35" />
            <line x1={x1} x2={x1} y1={t} y2={b} stroke={c} strokeWidth="1" opacity="0.35" />
            {bd.label && (
              <text x={(x0 + x1) / 2} y={t + 13} textAnchor="middle" fill={c}
                style={{ ...AXIS, fontSize: "var(--viz-type-micro,10px)", fontVariant: "all-small-caps", letterSpacing: ".05em", ...HALO }}>{bd.label}</text>
            )}
          </g>
        );
      })}
      {events.map((e, i) => {
        const x = clampX(g.x(e.index));
        const c = kindColor(e.kind);
        const badge = e.badge != null ? String(e.badge) : String(i + 1);
        const w = Math.max(16, badge.length * 7 + 9);
        return (
          <g key={"e" + i}>
            <title>{[e.label || `Event ${badge}`, e.detail].filter((part) => part != null && part !== "").join(": ")}</title>
            {showRules && (
              <line x1={x} x2={x} y1={by + 8} y2={b} stroke={c} strokeWidth="1" strokeDasharray="var(--viz-dash-flow,3 4)" opacity="0.7" />
            )}
            <rect x={x - w / 2} y={by - 3} width={w} height={16} rx="2" fill={c} opacity="0.92" />
            <text x={x} y={by + 9} textAnchor="middle" fill="var(--text-on-fill,#fff)"
              style={{ ...AXIS, fontSize: "var(--viz-type-micro,10px)", fontWeight: 700 }}>{badge}</text>
            {e.label && (
              <text x={x + w / 2 + 5} y={by + 9} textAnchor="start" fill={c} style={{ ...AXIS, fontSize: "var(--viz-type-micro,10px)", ...HALO }}>{e.label}</text>
            )}
          </g>
        );
      })}
      {callouts.map((c, i) => {
        if (!y) return null;
        const x = clampX(g.x(c.index));
        const py = y(c.value);
        const dx = c.dx != null ? c.dx : 26;
        const dy = c.dy != null ? c.dy : -26;
        const tx = Math.max(g.plotL + 4, Math.min(g.plotR - 4, x + dx));
        const ty = Math.max(t + 11, Math.min(b - 4, py + dy));
        const anchor = c.anchor || (dx < 0 ? "end" : "start");
        const col = c.kind ? kindColor(c.kind) : "var(--viz-annotation,var(--text-secondary))";
        return (
          <g key={"c" + i}>
            <path d={`M${x} ${py}L${tx - (anchor === "end" ? -5 : 5)} ${ty + 3}`} fill="none" stroke={col} strokeWidth="1" opacity="0.7" />
            <circle cx={x} cy={py} r="var(--viz-marker,4px)" fill="none" stroke={col} strokeWidth="1.5" />
            <text x={tx} y={ty} textAnchor={anchor} fill={col} style={{ ...AXIS, ...HALO }}>{c.text}</text>
          </g>
        );
      })}
    </g>
  );
}

ChartAnnotations.displayName = "ChartAnnotations";
