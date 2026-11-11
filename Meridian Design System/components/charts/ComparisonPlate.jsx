// Meridian comparison plate — the Becher-typology unit. Every plate in a set
// shares plot size, domain, tick positions, label placement, padding, and
// footer geometry, so differences between plates are real differences and not
// artefacts of scaling. PlateGrid computes the shared domain for you; a plate
// may be selected (rail + tint) but is never resized.
import React from "react";
import { roleStyle, scale, niceTicks, domainOf, linePath } from "./viz";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-plates{display:grid;gap:var(--plate-gap,12px);
  grid-template-columns:repeat(auto-fit,minmax(var(--plate-min-width,208px),1fr));width:100%;}
.mds-plate{display:flex;flex-direction:column;background:var(--bg-light);border:1px solid var(--border);
  border-left:3px solid transparent;border-radius:var(--radius-chip,2px);padding:var(--plate-pad,12px);
  font-family:var(--font-body);text-align:left;}
button.mds-plate{cursor:pointer;}
button.mds-plate:hover{border-color:var(--border-hover);}
button.mds-plate:focus-visible{outline:2px solid var(--border-focus);outline-offset:2px;}
.mds-plate--sel{border-left-color:var(--viz-select-rail);background:var(--viz-select-surface);}
.mds-plate__hd{display:flex;align-items:baseline;gap:8px;margin-bottom:8px;}
.mds-plate__t{font-size:13px;font-weight:var(--weight-semibold,600);color:var(--text-primary);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-plate__tag{margin-left:auto;font-family:var(--font-data);font-size:10px;color:var(--text-muted);
  text-transform:uppercase;letter-spacing:.04em;}
.mds-plate__ft{display:flex;align-items:baseline;gap:8px;margin-top:8px;padding-top:7px;
  border-top:1px solid var(--border-divider);}
.mds-plate__fl{font-size:11px;color:var(--text-muted);}
.mds-plate__fv{margin-left:auto;font-family:var(--font-data);font-size:12px;font-weight:var(--weight-semibold,600);
  color:var(--text-primary);font-variant-numeric:tabular-nums;}
.mds-plate__ax{font-family:var(--font-data);font-size:var(--viz-type-micro,10px);fill:var(--viz-axis);}
.mds-plate__end{font-family:var(--font-data);font-size:11px;font-weight:var(--weight-semibold,600);
  paint-order:stroke;stroke:var(--viz-halo);stroke-width:3px;stroke-linejoin:round;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "comparison-plate");
  el.textContent = css;
  document.head.appendChild(el);
}

const PLOT_W = 200;

export function ComparisonPlate({
  title, tag, categories = [], values = [], role = "actual", domain = [0, 1], ticks,
  format = (v) => String(v), footerLabel, footerValue, height,
  selected = false, onSelect, className = "", ...rest
}) {
  inject();
  const h = height || parseFloat(getVar("--plate-plot-h")) || 124;
  const padT = 8, padB = 16, padL = 18;
  const plotH = h - padT - padB;
  const tk = ticks && ticks.length ? ticks : niceTicks(domain[0], domain[1], 4);
  const x = scale([0, Math.max(1, categories.length - 1)], [padL, PLOT_W - 26]);
  const y = scale(domain, [padT + plotH, padT]);
  const r = roleStyle(role);
  let last = values.length - 1;
  while (last >= 0 && (values[last] === null || values[last] === undefined)) last -= 1;
  const Tag = onSelect ? "button" : "div";
  const extra = onSelect ? { type: "button", onClick: onSelect, "aria-pressed": selected } : {};
  return (
    <Tag className={`mds-plate${selected ? " mds-plate--sel" : ""}${className ? " " + className : ""}`} {...extra} {...rest}>
      <div className="mds-plate__hd">
        <span className="mds-plate__t">{title}</span>
        {tag ? <span className="mds-plate__tag">{tag}</span> : null}
      </div>
      <svg viewBox={`0 0 ${PLOT_W} ${h}`} width="100%" height={h} preserveAspectRatio="xMidYMid meet" role="img"
        aria-label={`${title}${footerLabel ? ` — ${footerLabel} ${footerValue}` : ""}`}>
        {tk.map((t) => (
          <g key={t}>
            <line x1={padL} x2={PLOT_W - 4} y1={y(t)} y2={y(t)} style={{ stroke: "var(--viz-grid)", strokeWidth: 1 }} />
            <text className="mds-plate__ax" x={0} y={y(t) + 3}>{format(t)}</text>
          </g>
        ))}
        <path d={linePath(values, x, y)} fill="none"
          style={{ stroke: r.stroke, strokeWidth: "var(--viz-stroke-secondary,1.5px)", strokeDasharray: r.dash, strokeLinejoin: "round" }} />
        {values.map((v, i) => (v === null || v === undefined ? null : (
          <circle key={i} cx={x(i)} cy={y(v)} r={2.2} style={{ fill: r.stroke }} />
        )))}
        {last >= 0 ? (
          <text className="mds-plate__end" x={x(last) + 4} y={y(values[last]) + 4} style={{ fill: r.stroke }}>{format(values[last])}</text>
        ) : null}
        {categories.map((c, i) => (
          <text key={c + i} className="mds-plate__ax" x={x(i)} y={h - 3} textAnchor="middle">{c}</text>
        ))}
      </svg>
      {footerLabel || footerValue ? (
        <div className="mds-plate__ft">
          <span className="mds-plate__fl">{footerLabel}</span>
          <span className="mds-plate__fv">{footerValue}</span>
        </div>
      ) : null}
    </Tag>
  );
}

function getVar(name) {
  if (typeof document === "undefined") return null;
  return getComputedStyle(document.documentElement).getPropertyValue(name);
}

export function PlateGrid({
  plates = [], categories = [], domain, tickCount = 4, format = (v) => String(v), role = "actual",
  selectedId, onSelect, className = "", ...rest
}) {
  inject();
  const dom = domain || domainOf(plates.map((p) => p.values));
  const ticks = niceTicks(dom[0], dom[1], tickCount);
  return (
    <div className={`mds-plates${className ? " " + className : ""}`} {...rest}>
      {plates.map((p) => (
        <ComparisonPlate
          key={p.id}
          title={p.title}
          tag={p.tag}
          categories={p.categories || categories}
          values={p.values}
          role={p.role || role}
          domain={dom}
          ticks={ticks}
          format={format}
          footerLabel={p.footerLabel}
          footerValue={p.footerValue}
          selected={selectedId === p.id}
          onSelect={onSelect ? () => onSelect(p.id) : undefined}
        />
      ))}
    </div>
  );
}
