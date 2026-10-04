// Meridian status banner — light institutional notice. Alpha-10 fill, solid semantic
// left-accent border, dim semantic title. For run results, data health, session notices.
import React from "react";

export function StatusBanner({ tone = "success", title, detail }) {
  const c = {
    success: { bg: "var(--green-a10, rgba(58,122,86,.10))",  bd: "var(--green, #3A7A56)",  fg: "var(--green-dim, #2C5C40)" },
    warning: { bg: "var(--orange-a10, rgba(138,92,18,.10))", bd: "var(--orange, #8A5C12)", fg: "var(--orange-dim, #68450E)" },
    danger:  { bg: "var(--red-a10, rgba(168,68,60,.10))",     bd: "var(--red, #A8443C)",    fg: "var(--red-dim, #7E332D)" },
    info:    { bg: "var(--blue-a10, rgba(168,84,54,.10))",   bd: "var(--accent, #A85436)", fg: "var(--accent, #A85436)" },
  }[tone];
  return (
    <div style={{
      display: "flex", gap: 10, alignItems: "baseline", padding: "11px 14px",
      borderRadius: "var(--radius-button,2px)",
      border: "1px solid var(--border, #E4E3DE)",
      borderLeft: `4px solid ${c.bd}`, background: c.bg,
      fontFamily: "var(--font-body)", fontSize: 13
    }}>
      <div>
        <div style={{ fontWeight: 600, color: c.fg }}>{title}</div>
        {detail && <div style={{
          fontSize: 12, color: "var(--text-secondary, #4E5258)", marginTop: 2,
          fontFamily: "var(--font-data)", fontVariantNumeric: "tabular-nums"
        }}>{detail}</div>}
      </div>
    </div>
  );
}
