// Meridian status banner — light institutional notice. Alpha-10 fill, solid semantic
// left-accent border, dim semantic title. For run results, data health, session notices.
import React from "react";

export function StatusBanner({ tone = "info", title, detail }) {
  const c = {
    success: { bg: "var(--green-a10, rgba(22,136,95,.10))",  bd: "var(--green, #16885F)",  fg: "var(--green-dim, #10663F)" },
    warning: { bg: "var(--orange-a10, rgba(183,121,31,.10))", bd: "var(--orange, #8A520E)", fg: "var(--orange-dim, #683E0B)" },
    danger:  { bg: "var(--red-a10, rgba(186,63,85,.10))",     bd: "var(--red, #BA3F55)",    fg: "var(--red-dim, #8C2F40)" },
    info:    {
      bg: "var(--severity-info-bg, var(--bg-medium, #F5F7FA))",
      bd: "var(--severity-info-bd, var(--border-strong, #AAB4BF))",
      fg: "var(--severity-info-fg, var(--text-secondary, #4D5967))"
    },
  }[tone];
  return (
    <div style={{
      display: "flex", gap: 10, alignItems: "baseline", padding: "11px 14px",
      borderRadius: "var(--radius-button,2px)",
      border: "1px solid var(--border, #D7DCE2)",
      borderLeft: `4px solid ${c.bd}`, background: c.bg,
      fontFamily: "var(--font-body)", fontSize: 13
    }}>
      <div>
        <div style={{ fontWeight: 600, color: c.fg }}>{title}</div>
        {detail && <div style={{
          fontSize: 12, color: "var(--text-secondary, #4D5967)", marginTop: 2,
          fontFamily: "var(--font-body)"
        }}>{detail}</div>}
      </div>
    </div>
  );
}
