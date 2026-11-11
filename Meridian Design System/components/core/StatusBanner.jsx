// Meridian status banner — light institutional notice. Alpha-10 fill, solid semantic
// left-accent border, dim semantic title. For run results, data health, session notices.
// A11y (3.5): configurable announcements — `announce`: "polite" (role=status) | "assertive"
// (role=alert) | "off". Default: assertive for danger, polite otherwise.
import React from "react";

export function StatusBanner({ tone = "success", title, detail, announce, ...rest }) {
  const c = {
    success: { bg: "var(--green-a10, rgba(74,124,94,.10))",  bd: "var(--green, #2E7655)",  fg: "var(--green-dim, #375D46)" },
    warning: { bg: "var(--orange-a10, rgba(183,121,31,.10))", bd: "var(--orange, #986717)", fg: "var(--orange-dim, #735414)" },
    danger:  { bg: "var(--red-a10, rgba(166,67,63,.10))",     bd: "var(--red, #B54B42)",    fg: "var(--red-dim, #7D322F)" },
    info:    { bg: "var(--blue-a10, rgba(47,111,143,.10))",   bd: "var(--semantic-info,#2F648A)", fg: "var(--semantic-info,#2F648A)" },
  }[tone];
  const mode = announce || (tone === "danger" ? "assertive" : "polite");
  const live = mode === "off" ? {} : mode === "assertive" ? { role: "alert" } : { role: "status" };
  return (
    <div {...live} style={{
      display: "flex", gap: 10, alignItems: "baseline", padding: "11px 14px",
      borderRadius: "var(--radius-button,2px)",
      border: "1px solid var(--border, #D2CDC3)",
      borderLeft: `4px solid ${c.bd}`, background: c.bg,
      fontFamily: "var(--font-body)", fontSize: 13
    }} {...rest}>
      <div>
        <div style={{ fontWeight: 600, color: c.fg }}>{title}</div>
        {detail && <div style={{
          fontSize: 12, color: "var(--text-secondary, #535C65)", marginTop: 2,
          fontFamily: "var(--font-data)", fontVariantNumeric: "tabular-nums"
        }}>{detail}</div>}
      </div>
    </div>
  );
}
