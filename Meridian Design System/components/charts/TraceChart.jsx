// Meridian trace chart — the annotated line/step primitive the visualization
// grammar is built on. Series carry ROLES (actual · prior · benchmark ·
// scenario · target · estimated), values are direct-labelled at their
// endpoints, model intervals draw as bands that never look like observed data,
// selection is committed (hover only previews), and every chart ships a
// keyboard path plus a table fallback.
import React from "react";
import { roleStyle, scale, niceTicks, domainOf, linePath, bandPath, observeWidth } from "./viz";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-trc{position:relative;width:100%;font-family:var(--font-body);}
.mds-trc__svg{display:block;width:100%;overflow:visible;}
.mds-trc__svg:focus-visible{outline:2px solid var(--border-focus);outline-offset:2px;}
.mds-trc__axis{font-family:var(--font-data);font-size:var(--viz-type-axis,11px);fill:var(--viz-axis);
  font-variant-numeric:tabular-nums;}
.mds-trc__unit{font-family:var(--font-body);font-size:var(--viz-type-micro,10px);fill:var(--text-muted);
  font-variant:all-small-caps;letter-spacing:.06em;}
.mds-trc__label{font-family:var(--font-body);font-size:var(--viz-type-label,12px);
  font-weight:var(--weight-semibold,600);paint-order:stroke;stroke:var(--viz-halo);stroke-width:3px;
  stroke-linejoin:round;}
.mds-trc__anno{font-family:var(--font-body);font-size:var(--viz-type-annotation,12px);fill:var(--viz-annotation);
  paint-order:stroke;stroke:var(--viz-halo);stroke-width:3px;stroke-linejoin:round;}
.mds-trc__hit{fill:transparent;cursor:pointer;}
.mds-trc__hit:hover + .mds-trc__prev,.mds-trc__hit:focus-visible + .mds-trc__prev{opacity:1;}
.mds-trc__prev{opacity:0;pointer-events:none;}
.mds-trc__sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "trace-chart");
  el.textContent = css;
  document.head.appendChild(el);
}

const fmtDefault = (v) => (v === null || v === undefined ? "—" : String(v));

export function TraceChart({
  categories = [],
  series = [],
  band,
  annotations = [],
  yDomain,
  tickCount = 5,
  yFormat = fmtDefault,
  yUnit,
  xUnit,
  height = 320,
  directLabels = true,
  selected,
  onSelect,
  description,
  tableFallback = true,
  className = "",
  ...rest
}) {
  inject();
  const hostRef = React.useRef(null);
  const [w, setW] = React.useState(720);
  React.useEffect(() => observeWidth(hostRef.current, setW), []);

  const visible = series.filter((s) => !s.hidden);
  const n = categories.length;
  const padT = 18, padB = 32, padL = 52;
  const padR = directLabels ? Math.min(180, Math.max(96, w * 0.18)) : 20;
  const plotW = Math.max(80, w - padL - padR);
  const plotH = Math.max(80, height - padT - padB);

  const dom = yDomain || domainOf([...visible.map((s) => s.values), band?.upper, band?.lower]);
  const ticks = niceTicks(dom[0], dom[1], tickCount);
  const x = scale([0, Math.max(1, n - 1)], [padL, padL + plotW]);
  const y = scale(dom, [padT + plotH, padT]);

  const sel = selected || null;
  const selIndex = sel && typeof sel.index === "number" ? sel.index : null;

  // Direct labels, de-collided deterministically (never layout-dependent).
  const labels = [];
  if (directLabels) {
    visible.forEach((s) => {
      let i = s.values.length - 1;
      while (i >= 0 && (s.values[i] === null || s.values[i] === undefined)) i -= 1;
      if (i < 0) return;
      labels.push({ id: s.id, role: s.role, text: `${s.label} ${yFormat(s.values[i])}`, y: y(s.values[i]), x: x(i) + 8 });
    });
    labels.sort((a, b) => a.y - b.y);
    for (let i = 1; i < labels.length; i += 1) {
      if (labels[i].y - labels[i - 1].y < 15) labels[i].y = labels[i - 1].y + 15;
    }
  }

  function commit(i) {
    if (!onSelect) return;
    const primary = visible.find((s) => s.role === "actual") || visible[0];
    onSelect({ seriesId: (sel && sel.seriesId) || (primary && primary.id), index: i });
  }
  function onKey(e) {
    if (!onSelect || !n) return;
    const cur = selIndex === null ? 0 : selIndex;
    if (e.key === "ArrowRight") { e.preventDefault(); commit(Math.min(n - 1, cur + 1)); }
    else if (e.key === "ArrowLeft") { e.preventDefault(); commit(Math.max(0, cur - 1)); }
    else if (e.key === "Home") { e.preventDefault(); commit(0); }
    else if (e.key === "End") { e.preventDefault(); commit(n - 1); }
  }

  return (
    <div className={`mds-trc${className ? " " + className : ""}`} ref={hostRef} {...rest}>
      <svg
        className="mds-trc__svg"
        width={w}
        height={height}
        viewBox={`0 0 ${w} ${height}`}
        role="img"
        tabIndex={onSelect ? 0 : -1}
        aria-label={description}
        onKeyDown={onKey}
      >
        {yUnit ? <text className="mds-trc__unit" x={4} y={11}>{yUnit}</text> : null}
        {ticks.map((t) => (
          <g key={`t${t}`}>
            <line x1={padL} x2={padL + plotW} y1={y(t)} y2={y(t)}
              style={{ stroke: "var(--viz-grid)", strokeWidth: "var(--viz-stroke-grid,1px)" }} />
            <text className="mds-trc__axis" x={padL - 8} y={y(t) + 3.5} textAnchor="end">{yFormat(t)}</text>
          </g>
        ))}
        {annotations.filter((a) => a.type === "range").map((a, i) => (
          <g key={`ar${i}`}>
            <rect x={x(a.from)} y={padT} width={Math.max(0, x(a.to) - x(a.from))} height={plotH}
              style={{ fill: "var(--surface-recessed)", opacity: 0.55 }} />
            {a.label ? <text className="mds-trc__anno" x={x(a.from) + 6} y={padT + 12} style={{ fontSize: "var(--viz-type-micro,10px)" }}>{a.label}</text> : null}
          </g>
        ))}
        {annotations.filter((a) => a.type === "line").map((a, i) => (
          <g key={`al${i}`}>
            <line x1={padL} x2={padL + plotW} y1={y(a.at)} y2={y(a.at)}
              style={{ stroke: "var(--series-target)", strokeWidth: "var(--viz-stroke-zero,1.25px)", strokeDasharray: "var(--viz-dash-target,2 4)" }} />
            {a.label ? <text className="mds-trc__anno" x={padL + plotW} y={y(a.at) - 6} textAnchor="end">{a.label}</text> : null}
          </g>
        ))}
        {band && band.upper && band.lower ? (
          <g aria-hidden="true">
            <path d={bandPath(band.upper, band.lower, x, y)} style={{ fill: "var(--viz-band-outer)" }} />
            {band.innerUpper && band.innerLower ? (
              <path d={bandPath(band.innerUpper, band.innerLower, x, y)} style={{ fill: "var(--viz-band-inner)" }} />
            ) : null}
          </g>
        ) : null}
        {selIndex !== null ? (
          <line x1={x(selIndex)} x2={x(selIndex)} y1={padT} y2={padT + plotH}
            style={{ stroke: "var(--viz-select-rail)", strokeWidth: "var(--viz-stroke-datum,2px)", opacity: 0.35 }} />
        ) : null}
        {visible.map((s) => {
          const r = roleStyle(s.role);
          return (
            <path key={s.id} d={linePath(s.values, x, y)} fill="none"
              style={{ stroke: r.stroke, strokeWidth: r.width, strokeDasharray: r.dash, strokeLinejoin: "round", strokeLinecap: "round" }} />
          );
        })}
        {visible.map((s) => {
          const r = roleStyle(s.role);
          if (r.marker === "none") return null;
          return s.values.map((v, i) => {
            if (v === null || v === undefined) return null;
            const isSel = selIndex === i;
            const rad = isSel ? 5 : 3.5;
            return (
              <circle key={`${s.id}-${i}`} cx={x(i)} cy={y(v)} r={rad}
                style={{
                  fill: r.marker === "open" ? "var(--viz-plot-bg)" : r.stroke,
                  stroke: r.stroke, strokeWidth: r.marker === "open" ? 1.5 : isSel ? 2 : 0,
                }} />
            );
          });
        })}
        {annotations.filter((a) => a.type === "bracket").map((a, i) => {
          const yy = y(a.at);
          return (
            <g key={`ab${i}`} style={{ stroke: "var(--series-scenario)", fill: "none", strokeWidth: 1 }}>
              <path d={`M${x(a.from)} ${yy - 5}L${x(a.from)} ${yy}L${x(a.to)} ${yy}L${x(a.to)} ${yy - 5}`} />
              <text className="mds-trc__anno" x={(x(a.from) + x(a.to)) / 2} y={yy - 9} textAnchor="middle" style={{ stroke: "var(--viz-halo)", fill: "var(--viz-annotation)" }}>{a.label}</text>
            </g>
          );
        })}
        {labels.map((l) => (
          <text key={l.id} className="mds-trc__label" x={l.x} y={l.y + 4}
            style={{ fill: roleStyle(l.role).stroke }}>{l.text}</text>
        ))}
        {categories.map((c, i) => (
          <text key={`x${i}`} className="mds-trc__axis" x={x(i)} y={padT + plotH + 18} textAnchor="middle">{c}</text>
        ))}
        {xUnit ? <text className="mds-trc__unit" x={padL + plotW / 2} y={height - 1} textAnchor="middle">{xUnit}</text> : null}
        {onSelect ? categories.map((c, i) => (
          <rect key={`h${i}`} className="mds-trc__hit" x={x(i) - Math.max(9, plotW / (n * 2))} y={padT}
            width={Math.max(18, plotW / n)} height={plotH} onClick={() => commit(i)}>
            <title>{`${c} — select`}</title>
          </rect>
        )) : null}
      </svg>
      {tableFallback ? (
        <table className="mds-trc__sr">
          <caption>{description}</caption>
          <thead>
            <tr><th scope="col">{xUnit || "Category"}</th>{visible.map((s) => <th key={s.id} scope="col">{s.label}</th>)}</tr>
          </thead>
          <tbody>
            {categories.map((c, i) => (
              <tr key={c}>
                <th scope="row">{c}</th>
                {visible.map((s) => <td key={s.id}>{s.values[i] === null || s.values[i] === undefined ? "Missing" : yFormat(s.values[i])}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}
