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

// One identity per annotation, shared by the overlay and the list so they can point at each other.
const annotationId = (type, item, i) => (item && item.id != null ? String(item.id) : `${type}-${i}`);
const KIND_WORD = { positive: "Positive", negative: "Negative", warning: "Warning", information: "Information", scenario: "Scenario" };

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "chart-annotation-list");
  el.textContent = `
.mds-cal{list-style:none;margin:8px 0 0;padding:0;display:flex;flex-direction:column;gap:2px;font-family:var(--font-body);}
.mds-cal__btn{appearance:none;width:100%;display:flex;align-items:baseline;gap:8px;text-align:left;background:none;border:1px solid transparent;border-radius:var(--radius-button,2px);padding:5px 8px;color:var(--text-primary);font:inherit;font-size:var(--viz-type-label,12px);cursor:pointer;}
.mds-cal__btn:hover{background:var(--bg-hover);}
.mds-cal__btn[aria-expanded="true"]{background:var(--interaction-selected-surface,var(--bg-active));}
.mds-cal__btn:focus-visible{outline:2px solid var(--interaction-focus,var(--border-focus,#2D6FB3));outline-offset:1px;}
.mds-cal__tag{flex:none;min-width:16px;padding:0 4px;border:1px solid currentColor;border-radius:var(--radius-chip,3px);font-family:var(--font-data);font-size:var(--viz-type-micro,10px);font-weight:700;text-align:center;}
.mds-cal__at{flex:none;font-family:var(--font-data);font-variant-numeric:slashed-zero tabular-nums;color:var(--text-muted);}
.mds-cal__kind{flex:none;font-size:var(--viz-type-micro,10px);font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-secondary);}
.mds-cal__detail{margin:0 0 4px 34px;padding:4px 8px;border-left:2px solid var(--border-strong);font-size:var(--viz-type-annotation,12px);line-height:1.45;color:var(--text-secondary);max-width:62ch;}
@media (forced-colors:active){.mds-cal__btn[aria-expanded="true"]{border-color:Highlight;}.mds-cal__btn:focus-visible{outline-color:Highlight;}}
`;
  document.head.appendChild(el);
}

// The overlay draws inside ChartFrame's svg, which is an image to assistive technology: nothing
// in it can take focus or be read item by item. This list is the same annotations as HTML, in
// time order, beside the chart — the keyboard and screen-reader path to every one of them.
function ChartAnnotationList({
  bands = [], events = [], callouts = [],
  ariaLabel = "Chart annotations",
  formatIndex = (i) => `#${i}`,   // x-index → the label the axis shows (a date, a period)
  formatValue = (v) => String(v), // callout value → printed figure with its unit
  activeId, onActiveChange,       // controlled highlight, shared with <ChartAnnotations activeId>
  emptyLabel = "No annotations on this chart.",
}) {
  inject();
  const base = React.useId();
  const [open, setOpen] = React.useState(null);
  const refs = React.useRef([]);
  const items = React.useMemo(() => {
    const all = [
      ...bands.map((b, i) => ({
        id: annotationId("band", b, i), at: Math.min(b.from, b.to), tag: "↔", kind: b.kind,
        where: `${formatIndex(b.from)} to ${formatIndex(b.to)}`, text: b.label || "Regime", detail: b.detail, type: "Regime",
      })),
      ...events.map((e, i) => ({
        id: annotationId("event", e, i), at: e.index, tag: e.badge != null ? String(e.badge) : String(i + 1), kind: e.kind,
        where: formatIndex(e.index), text: e.label || "Event", detail: e.detail, type: "Event",
      })),
      ...callouts.map((c, i) => ({
        id: annotationId("callout", c, i), at: c.index, tag: "•", kind: c.kind,
        where: `${formatIndex(c.index)}, ${formatValue(c.value)}`, text: c.text, detail: c.detail, type: "Callout",
      })),
    ];
    // Stable time order: ties keep regime → event → callout, then declaration order.
    return all.map((item, order) => ({ item, order })).sort((a, b) => a.item.at - b.item.at || a.order - b.order).map((x) => x.item);
  }, [bands, events, callouts, formatIndex, formatValue]);

  if (!items.length) return <p style={{ margin: "8px 0 0", fontSize: "var(--viz-type-annotation,12px)", color: "var(--text-muted)" }}>{emptyLabel}</p>;

  const move = (to) => { const el = refs.current[Math.max(0, Math.min(items.length - 1, to))]; if (el) el.focus(); };
  const onKeyDown = (e, i) => {
    if (e.key === "ArrowDown" || e.key === "ArrowRight") { e.preventDefault(); move(i + 1); }
    else if (e.key === "ArrowUp" || e.key === "ArrowLeft") { e.preventDefault(); move(i - 1); }
    else if (e.key === "Home") { e.preventDefault(); move(0); }
    else if (e.key === "End") { e.preventDefault(); move(items.length - 1); }
    else if (e.key === "Escape" && open != null) { e.preventDefault(); e.stopPropagation(); setOpen(null); }
  };
  return (
    <ol className="mds-cal" aria-label={ariaLabel}>
      {items.map((it, i) => {
        const expanded = open === it.id;
        const detailId = `${base}-d${i}`;
        const color = kindColor(it.kind);
        return (
          <li key={it.id}>
            <button type="button" className="mds-cal__btn" ref={(el) => { refs.current[i] = el; }}
              data-annotation-id={it.id} data-active={activeId === it.id ? "true" : undefined}
              aria-expanded={it.detail ? expanded : undefined} aria-controls={it.detail && expanded ? detailId : undefined}
              onFocus={() => onActiveChange && onActiveChange(it.id)}
              onMouseEnter={() => onActiveChange && onActiveChange(it.id)}
              onBlur={() => onActiveChange && onActiveChange(null)}
              onMouseLeave={() => onActiveChange && onActiveChange(null)}
              onClick={() => { if (it.detail) setOpen(expanded ? null : it.id); }}
              onKeyDown={(e) => onKeyDown(e, i)}>
              <span className="mds-cal__tag" style={{ color }} aria-hidden="true">{it.tag}</span>
              <span className="mds-cal__at">{it.where}</span>
              <span style={{ flex: "1 1 auto", minWidth: 0 }}>{it.text}</span>
              <span className="mds-cal__kind">{it.type}{KIND_WORD[it.kind] ? ` · ${KIND_WORD[it.kind]}` : ""}</span>
            </button>
            {it.detail && expanded && <p className="mds-cal__detail" id={detailId}>{it.detail}</p>}
          </li>
        );
      })}
    </ol>
  );
}
ChartAnnotationList.displayName = "ChartAnnotations.List";

export function ChartAnnotations({
  g,                           // the geometry object from ChartFrame's children render prop
  pane = null,                 // pane key; defaults to the full plot area
  bands = [],                  // [{ from, to, label?, kind? }] — x-index regime shading
  events = [],                 // [{ index, label?, badge?, kind?, detail? }] — vertical rules
  callouts = [],               // [{ index, value, text, dx?, dy?, anchor? }] — leader + text
  showRules = true,
  badgeY = null,               // override badge baseline (defaults to pane top)
  activeId = null,             // id from ChartAnnotations.List's onActiveChange — emphasises that mark
}) {
  if (!g) return null;
  const p = pane ? g.pane(pane) : null;
  const t = p ? p.t : g.innerT;
  const b = p ? p.b : g.innerB;
  const y = p ? p.y : null;
  const by = badgeY != null ? badgeY : t;
  const clampX = (x) => Math.max(g.plotL, Math.min(g.plotR, x));
  // Emphasis is weight, not hue: the active mark keeps its semantic colour in every theme.
  const on = (kind, item, i) => activeId != null && annotationId(kind, item, i) === activeId;

  return (
    <g>
      {bands.map((bd, i) => {
        const x0 = clampX(g.x(bd.from)), x1 = clampX(g.x(bd.to));
        const c = kindColor(bd.kind);
        return (
          <g key={"b" + i}>
            <rect x={Math.min(x0, x1)} y={t} width={Math.abs(x1 - x0)} height={b - t}
              fill={c} opacity="0.07" />
            <line x1={x0} x2={x0} y1={t} y2={b} stroke={c} strokeWidth={on("band", bd, i) ? 2 : 1} opacity={on("band", bd, i) ? 1 : 0.35} />
            <line x1={x1} x2={x1} y1={t} y2={b} stroke={c} strokeWidth={on("band", bd, i) ? 2 : 1} opacity={on("band", bd, i) ? 1 : 0.35} />
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
            {showRules && (
              <line x1={x} x2={x} y1={by + 8} y2={b} stroke={c} strokeWidth={on("event", e, i) ? 2 : 1}
                strokeDasharray={on("event", e, i) ? undefined : "var(--viz-dash-flow,3 4)"} opacity={on("event", e, i) ? 1 : 0.7} />
            )}
            <rect x={x - w / 2} y={by - 3} width={w} height={16} rx="2" fill={c} opacity="0.92"
              stroke={on("event", e, i) ? "var(--interaction-focus,var(--border-focus,#2D6FB3))" : "none"} strokeWidth="2" />
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
            <circle cx={x} cy={py} r="var(--viz-marker,4px)" fill="none" stroke={col} strokeWidth={on("callout", c, i) ? 3 : 1.5} />
            <text x={tx} y={ty} textAnchor={anchor} fill={col} style={{ ...AXIS, ...HALO }}>{c.text}</text>
          </g>
        );
      })}
    </g>
  );
}

ChartAnnotations.displayName = "ChartAnnotations";
// Compound part rather than a new top-level export: it has no meaning without the overlay's data.
ChartAnnotations.List = ChartAnnotationList;
ChartAnnotations.idOf = annotationId;
