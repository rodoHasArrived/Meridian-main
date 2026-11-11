// Meridian Datum — a shared reference line for charts, tables, timelines, and workflows.
// 3.2.0: SVG-native rendering, band/range datums, and lane-based label collision handling.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "datum");
  el.textContent = `
@property --datum-color{syntax:"*";inherits:true;}
@property --datum-lane{syntax:"*";inherits:true;}
.mds-datum{position:absolute;z-index:3;pointer-events:none;color:var(--text-muted,#606870);font-family:var(--font-data,monospace);font-size:10px;line-height:1;}
.mds-datum--v{top:0;bottom:0;width:0;border-left:var(--stroke-hairline,1px) solid var(--datum-color,var(--border-strong,#AAA49A));}
.mds-datum--h{left:0;right:0;height:0;border-top:var(--stroke-hairline,1px) solid var(--datum-color,var(--border-strong,#AAA49A));}
.mds-datum--selection{--datum-color:var(--selected,#496C8F);border-width:var(--stroke-emphasis,2px);}
.mds-datum--current-time{--datum-color:var(--brand-axis,#29323A);border-style:dashed;}
.mds-datum--target{--datum-color:var(--series-target,var(--text-muted,#606870));border-style:dotted;}
.mds-datum--zero{--datum-color:var(--viz-zero,var(--border-strong,#AAA49A));}
.mds-datum--warning{--datum-color:var(--orange,#986717);}
.mds-datum--interactive{pointer-events:auto;cursor:pointer;}
.mds-datum:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px;}
.mds-datum__label{position:absolute;display:inline-flex;align-items:center;gap:5px;padding:3px 5px;border:1px solid var(--border,#D2CDC3);background:var(--surface-overlay,var(--surface-overlay-base,#fff));color:var(--text-secondary,#535C65);white-space:nowrap;box-shadow:var(--shadow-menu,0 2px 8px rgba(17,23,29,.16));}
.mds-datum--v .mds-datum__label{left:5px;top:5px;transform:translateY(calc(var(--datum-lane,0) * 21px));}
.mds-datum--v.mds-datum--before .mds-datum__label{left:auto;right:5px;}
.mds-datum--h .mds-datum__label{left:5px;top:5px;transform:translateX(calc(var(--datum-lane,0) * 8px));}
.mds-datum--h.mds-datum--before .mds-datum__label{top:auto;bottom:5px;}
.mds-datum__value{color:var(--text-primary,#20252B);font-weight:600;}
.mds-datum__marker{position:absolute;width:7px;height:7px;border:2px solid var(--datum-color,var(--selected,#496C8F));background:var(--surface-well,var(--card-surface-raised,#FCFBF8));border-radius:50%;box-sizing:border-box;}
.mds-datum--v .mds-datum__marker{left:-4px;top:50%;transform:translateY(-50%);}
.mds-datum--h .mds-datum__marker{top:-4px;left:50%;transform:translateX(-50%);}
.mds-datum--band{border:0;background:color-mix(in srgb,var(--datum-color,var(--border-strong,#AAA49A)) 10%,transparent);}
.mds-datum--band.mds-datum--v{width:auto;border-left:var(--stroke-hairline,1px) solid var(--datum-color,var(--border-strong,#AAA49A));border-right:var(--stroke-hairline,1px) solid var(--datum-color,var(--border-strong,#AAA49A));}
.mds-datum--band.mds-datum--h{height:auto;border-top:var(--stroke-hairline,1px) solid var(--datum-color,var(--border-strong,#AAA49A));border-bottom:var(--stroke-hairline,1px) solid var(--datum-color,var(--border-strong,#AAA49A));}
.mds-datum-group{position:absolute;inset:0;pointer-events:none;}
.mds-datum-svg{pointer-events:none;--datum-color:var(--border-strong,#AAA49A);}
.mds-datum-svg--selection{--datum-color:var(--selected,#496C8F);}
.mds-datum-svg--current-time{--datum-color:var(--brand-axis,#29323A);}
.mds-datum-svg--target{--datum-color:var(--series-target,var(--text-muted,#606870));}
.mds-datum-svg--zero{--datum-color:var(--viz-zero,var(--border-strong,#AAA49A));}
.mds-datum-svg--warning{--datum-color:var(--orange,#986717);}
.mds-datum-svg--interactive{pointer-events:auto;cursor:pointer;}
.mds-datum-svg__line{stroke:var(--datum-color);stroke-width:var(--viz-stroke-datum,1px);fill:none;vector-effect:non-scaling-stroke;}
.mds-datum-svg--selection .mds-datum-svg__line{stroke-width:var(--stroke-emphasis,2px);}
.mds-datum-svg--current-time .mds-datum-svg__line{stroke-dasharray:4 3;}
.mds-datum-svg--target .mds-datum-svg__line{stroke-dasharray:1 3;stroke-linecap:round;}
.mds-datum-svg__band{fill:var(--datum-color);fill-opacity:.10;stroke:none;}
.mds-datum-svg__label{font-family:var(--font-data,monospace);font-size:10px;fill:var(--text-secondary,#535C65);}
.mds-datum-svg__label-value{font-weight:600;fill:var(--text-primary,#20252B);}
.mds-datum-svg__plate{fill:var(--surface-overlay,var(--surface-overlay-base,#fff));stroke:var(--border,#D2CDC3);stroke-width:1;}
.mds-datum-svg__marker{fill:var(--surface-well,var(--card-surface-raised,#FCFBF8));stroke:var(--datum-color);stroke-width:2;}
@media print{.mds-datum__label{box-shadow:none}}
`;
  document.head.appendChild(el);
}

const clamp = (n) => Math.max(0, Math.min(1, Number(n) || 0));
const CHAR_PX = 6.05; // IBM Plex Mono at 10px
function labelWidth(label, value) {
  const chars = `${label ?? ""}${value ?? ""}`.length;
  if (!chars) return 0;
  return 12 + chars * CHAR_PX + (label && value !== undefined ? 5 : 0);
}

function DatumSvg({
  role, orientation, position, to, label, value, marker, labelPlacement,
  interactive, onActivate, labelLane = 0, className = "", ...rest
}) {
  const vertical = orientation === "vertical";
  const a = clamp(position), b = to == null ? null : clamp(to);
  const lo = b == null ? a : Math.min(a, b), hi = b == null ? a : Math.max(a, b);
  const pc = (n) => `${n * 100}%`;
  const activation = interactive || Boolean(onActivate);
  const text = [label, value].filter((part) => part !== undefined && part !== null && part !== "").join(" ");
  const laneShift = vertical ? labelLane * 15 : 0;
  const tx = vertical ? (labelPlacement === "before" ? -6 : 6) : 6 + labelLane * 8;
  const ty = vertical ? 12 + laneShift : (labelPlacement === "before" ? -5 : 13);
  const handleKeyDown = (event) => {
    if ((event.key === "Enter" || event.key === " ") && onActivate) { event.preventDefault(); onActivate(event); }
  };
  return (
    <g
      className={`mds-datum-svg mds-datum-svg--${role}${activation ? " mds-datum-svg--interactive" : ""}${className ? " " + className : ""}`}
      role={activation ? "button" : "presentation"}
      tabIndex={activation ? 0 : undefined}
      aria-label={rest["aria-label"] || text || `${role} datum`}
      onClick={onActivate}
      onKeyDown={handleKeyDown}
      {...rest}
    >
      {b != null ? (
        vertical
          ? <rect className="mds-datum-svg__band" x={pc(lo)} y="0" width={pc(hi - lo)} height="100%" />
          : <rect className="mds-datum-svg__band" x="0" y={pc(lo)} width="100%" height={pc(hi - lo)} />
      ) : null}
      {(b == null ? [lo] : [lo, hi]).map((at, i) => (
        vertical
          ? <line key={i} className="mds-datum-svg__line" x1={pc(at)} x2={pc(at)} y1="0" y2="100%" />
          : <line key={i} className="mds-datum-svg__line" x1="0" x2="100%" y1={pc(at)} y2={pc(at)} />
      ))}
      {marker ? (
        vertical
          ? <circle className="mds-datum-svg__marker" cx={pc(lo)} cy="50%" r="3.5" />
          : <circle className="mds-datum-svg__marker" cx="50%" cy={pc(lo)} r="3.5" />
      ) : null}
      {text ? (
        <text
          className="mds-datum-svg__label"
          x={vertical ? pc(lo) : 0}
          y={vertical ? 0 : pc(lo)}
          dx={tx}
          dy={ty}
          textAnchor={vertical && labelPlacement === "before" ? "end" : "start"}
        >
          {label ? <tspan>{label}</tspan> : null}
          {value !== undefined && value !== null ? <tspan className="mds-datum-svg__label-value" dx={label ? 5 : 0}>{value}</tspan> : null}
        </text>
      ) : null}
    </g>
  );
}

export function Datum({
  role = "baseline", orientation = "vertical", position = 0.5, to,
  label, value, marker = false, labelPlacement = "after", interactive = false,
  svg = false, labelLane = 0, onActivate, className = "", style, ...rest
}) {
  inject();
  if (svg) {
    return (
      <DatumSvg
        role={role} orientation={orientation} position={position} to={to} label={label} value={value}
        marker={marker} labelPlacement={labelPlacement} interactive={interactive} onActivate={onActivate}
        labelLane={labelLane} className={className} style={style} {...rest}
      />
    );
  }
  const vertical = orientation === "vertical";
  const a = clamp(position), b = to == null ? null : clamp(to);
  const lo = b == null ? a : Math.min(a, b), span = b == null ? null : Math.abs(b - a);
  const pct = `${lo * 100}%`;
  const placement = vertical
    ? { left: pct, ...(span != null ? { width: `${span * 100}%` } : null) }
    : { top: pct, ...(span != null ? { height: `${span * 100}%` } : null) };
  const activation = interactive || Boolean(onActivate);
  const handleKeyDown = (event) => {
    if ((event.key === "Enter" || event.key === " ") && onActivate) {
      event.preventDefault();
      onActivate(event);
    }
    rest.onKeyDown?.(event);
  };
  return (
    <div
      className={`mds-datum mds-datum--${vertical ? "v" : "h"} mds-datum--${role} mds-datum--${labelPlacement}${span != null ? " mds-datum--band" : ""}${activation ? " mds-datum--interactive" : ""}${className ? " " + className : ""}`}
      style={{ ...placement, ...(labelLane ? { "--datum-lane": labelLane } : null), ...style }}
      role={activation ? "button" : undefined}
      tabIndex={activation ? 0 : undefined}
      aria-label={rest["aria-label"] || [label, value].filter(Boolean).join(" · ") || `${role} datum`}
      onClick={onActivate}
      onKeyDown={handleKeyDown}
      {...rest}
    >
      {marker ? <span className="mds-datum__marker" aria-hidden="true" /> : null}
      {(label || value !== undefined) ? (
        <span className="mds-datum__label"><span>{label}</span>{value !== undefined ? <span className="mds-datum__value">{value}</span> : null}</span>
      ) : null}
    </div>
  );
}

/**
 * Wraps a set of Datums and assigns each a label lane so crowded labels stack
 * instead of overprinting. Measures its own box; with no width it is a no-op.
 */
export function DatumGroup({ children, width, laneHeight = 21, gap = 4, as: Tag = "div", className = "", style, ...rest }) {
  inject();
  const ref = React.useRef(null);
  const [measured, setMeasured] = React.useState(width || 0);
  React.useEffect(() => {
    if (width || !ref.current || typeof ResizeObserver === "undefined") return undefined;
    const node = ref.current;
    const observer = new ResizeObserver((entries) => {
      const next = Math.round(entries[0].contentRect.width);
      setMeasured((current) => (Math.abs(current - next) > 1 ? next : current));
    });
    observer.observe(node);
    setMeasured(Math.round(node.getBoundingClientRect().width));
    return () => observer.disconnect();
  }, [width]);

  const box = width || measured;
  const laned = React.useMemo(() => {
    const list = React.Children.toArray(children).filter(React.isValidElement);
    if (!box) return list;
    const lanes = [];
    const order = list
      .map((child, i) => ({ child, i, at: clamp(child.props.position) }))
      .sort((a, b) => a.at - b.at);
    const assigned = new Map();
    order.forEach(({ child, i, at }) => {
      const w = labelWidth(child.props.label, child.props.value);
      if (!w) { assigned.set(i, 0); return; }
      const start = at * box + 5;
      const end = start + w;
      let lane = 0;
      while (lanes[lane] != null && lanes[lane] > start - gap) lane += 1;
      lanes[lane] = end;
      assigned.set(i, lane);
    });
    return list.map((child, i) =>
      assigned.get(i) ? React.cloneElement(child, { labelLane: assigned.get(i) }) : child
    );
  }, [box, children, gap]);

  return (
    <Tag
      ref={ref}
      className={`mds-datum-group${className ? " " + className : ""}`}
      style={{ ...(laneHeight !== 21 ? { "--datum-lane-height": `${laneHeight}px` } : null), ...style }}
      {...rest}
    >{laned}</Tag>
  );
}
