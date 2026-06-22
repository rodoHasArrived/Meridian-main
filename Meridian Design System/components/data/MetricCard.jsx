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
  border-radius:var(--radius-card,8px);padding:18px;
  box-shadow:var(--shadow-card,0 1px 1px rgba(0,0,0,.08));}
.mds-metric--neutral{border-left-color:var(--border-strong,#AAB4BF);}
.mds-metric--info{border-left-color:var(--accent,#2F6F8F);}
.mds-metric--success{border-left-color:var(--green,#16885F);}
.mds-metric--warning{border-left-color:var(--orange,#B7791F);}
.mds-metric--danger{border-left-color:var(--red,#BA3F55);}
.mds-metric__label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#6E7781);margin:0;}
.mds-metric__value{margin:8px 0 0;font-family:var(--font-data);font-size:24px;font-weight:600;
  line-height:1;font-variant-numeric:tabular-nums;color:var(--text-primary,#22272E);}
.mds-metric__delta{font-family:var(--font-data);font-size:11px;margin-top:6px;
  font-variant-numeric:tabular-nums;}
.mds-delta--up{color:var(--green-dim,#126C4D);}
.mds-delta--down{color:var(--red-dim,#983244);}
.mds-delta--flat{color:var(--text-muted,#6E7781);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "metric");
  el.textContent = css;
  document.head.appendChild(el);
}

export function MetricCard({ label, value, delta, tone = "neutral", trend }) {
  inject();
  // trend: "up" | "down" | "flat" (colors the delta). Defaults from a leading sign.
  const t = trend || (delta && delta.trim().startsWith("-") ? "down"
    : delta && delta.trim().startsWith("+") ? "up" : "flat");
  return (
    <div className={`mds-metric mds-metric--${tone}`} role="group" aria-label={`${label}: ${value}`}>
      <p className="mds-metric__label">{label}</p>
      <p className="mds-metric__value">{value}</p>
      {delta && <div className={`mds-metric__delta mds-delta--${t}`}>{delta}</div>}
    </div>
  );
}
