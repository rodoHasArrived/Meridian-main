// Meridian KPI tile — mirrors MetricCardStyle + the success/danger/warning/info variants
// in ThemeSurfaces.xaml: raised paper surface, padding 18, 3px LEFT accent border, small-caps
// label, 24px mono SemiBold value.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-metric{background:var(--card-surface-raised,#FAFBFC);
  border:1px solid var(--border,#D7DCE2);border-left-width:3px;
  border-radius:var(--radius-card,2px);padding:18px;
  box-shadow:var(--shadow-card,none);display:flex;flex-direction:column;min-width:0;}
.mds-metric--neutral{border-left-color:var(--border-strong,#AAB4BF);}
.mds-metric--info{border-left-color:var(--accent,#2F6F8F);}
.mds-metric--success{border-left-color:var(--green,#16885F);}
.mds-metric--warning{border-left-color:var(--orange,#8A520E);}
.mds-metric--danger{border-left-color:var(--red,#BA3F55);}
.mds-metric__head{display:flex;align-items:center;justify-content:space-between;gap:10px;min-height:14px;}
.mds-metric__label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);margin:0;}
.mds-metric__spark{flex:0 0 auto;display:block;opacity:.9;}
.mds-metric__value{margin:10px 0 0;font-family:var(--font-data);font-size:24px;font-weight:600;
  line-height:1;white-space:nowrap;font-variant-numeric:slashed-zero tabular-nums;color:var(--text-primary,#22272E);}
.mds-metric--hero .mds-metric__value{font-size:30px;}
.mds-metric--hero .mds-metric__label{font-size:11px;}
.mds-metric__foot{display:flex;align-items:baseline;gap:8px;margin-top:7px;flex-wrap:wrap;}
.mds-metric__delta{font-family:var(--font-data);font-size:11px;white-space:nowrap;
  font-variant-numeric:slashed-zero tabular-nums;}
.mds-metric__ctx{font-family:var(--font-body);font-size:11px;color:var(--text-muted,#59636F);white-space:nowrap;}
.mds-delta--up{color:var(--green-dim,#10663F);}
.mds-delta--down{color:var(--red-dim,#8C2F40);}
.mds-delta--flat{color:var(--text-muted,#59636F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "metric");
  el.textContent = css;
  document.head.appendChild(el);
}

// Inline trend sparkline — hairline, muted by default (no new color); tone-tints on request.
function Sparkline({ points, tone }) {
  if (!points || points.length < 2) return null;
  const w = 76, h = 22, pad = 2;
  const max = Math.max(...points), min = Math.min(...points), span = max - min || 1;
  const step = (w - pad * 2) / (points.length - 1);
  const y = (v) => pad + (max - v) * ((h - pad * 2) / span);
  const d = points.map((v, i) => `${(pad + i * step).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  const stroke = tone === "success" ? "var(--green)" : tone === "danger" ? "var(--red)"
    : tone === "info" ? "var(--accent)" : tone === "warning" ? "var(--orange)" : "var(--text-disabled,#889099)";
  const lx = pad + (points.length - 1) * step, ly = y(points[points.length - 1]);
  return (
    <svg className="mds-metric__spark" width={w} height={h} viewBox={`0 0 ${w} ${h}`} aria-hidden="true">
      <polyline points={d} fill="none" stroke={stroke} strokeWidth="1.25" strokeLinejoin="round" strokeLinecap="round" />
      <circle cx={lx.toFixed(1)} cy={ly.toFixed(1)} r="1.6" fill={stroke} />
    </svg>
  );
}

export function MetricCard({ label, value, delta, context, tone = "neutral", trend, hero = false, sparkline, sparklineTone = "muted", style, ...rest }) {
  inject();
  // trend: "up" | "down" | "flat" (colors the delta). Defaults from a leading sign.
  const t = trend || (delta && String(delta).trim().startsWith("-") ? "down"
    : delta && String(delta).trim().startsWith("+") ? "up" : "flat");
  const arrow = t === "up" ? "\u25B2" : t === "down" ? "\u25BC" : "";
  return (
    <div className={`mds-metric mds-metric--${tone}${hero ? " mds-metric--hero" : ""}`} style={style} role="group" aria-label={`${label}: ${value}`} {...rest}>
      <div className="mds-metric__head">
        <p className="mds-metric__label">{label}</p>
        {sparkline && <Sparkline points={sparkline} tone={sparklineTone} />}
      </div>
      <p className="mds-metric__value">{value}</p>
      {(delta || context) && (
        <div className="mds-metric__foot">
          {delta && <span className={`mds-metric__delta mds-delta--${t}`}>{arrow && <span aria-hidden="true">{arrow} </span>}{delta}</span>}
          {context && <span className="mds-metric__ctx">{context}</span>}
        </div>
      )}
    </div>
  );
}
