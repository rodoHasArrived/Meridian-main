// Meridian key/value fact grid — small-caps label over mono value. Light theme.
// 4.1: items carry an optional depth `tier` — `tier: "full"` fields withdraw at summary
// depth and the grid states how many it withheld (guidelines/PROGRESSIVE_DISCLOSURE.md).
import React from "react";

export function KeyValueGrid({ items, columns = 2, detail = "full", onReveal }) {
  const all = items || [];
  const shown = detail === "summary" ? all.filter((it) => it.tier !== "full") : all;
  const withheld = all.length - shown.length;
  return (
    <div>
      <div style={{
        display: "grid", gridTemplateColumns: `repeat(${columns}, minmax(0,1fr))`,
        gap: "12px 24px"
      }}>
        {shown.map((it, i) => (
          <div key={i} style={{ display: "flex", flexDirection: "column", gap: 3, minWidth: 0 }}>
            <div style={{
              fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 600,
              fontVariant: "all-small-caps", letterSpacing: "0.03em",
              color: "var(--text-muted, #6B737B)"
            }}>{it.label}</div>
            <div style={{
              fontFamily: "var(--font-data)", fontSize: 13,
              fontVariantNumeric: "tabular-nums", color: "var(--text-primary, #20252B)"
            }}>{it.value}</div>
          </div>
        ))}
      </div>
      {withheld > 0 ? (
        <div style={{
          display: "flex", justifyContent: "space-between", gap: 12, alignItems: "baseline",
          marginTop: 12, paddingTop: 8, borderTop: "1px solid var(--border-divider, #E2DED5)",
          fontFamily: "var(--font-body)", fontSize: 11, color: "var(--text-muted, #6B737B)"
        }}>
          <span>{withheld} field{withheld === 1 ? "" : "s"} withheld at summary depth</span>
          {onReveal ? (
            <button type="button" onClick={() => onReveal("full")} style={{
              padding: 0, background: "transparent", border: 0, font: "inherit", fontWeight: 500,
              color: "var(--link, #2D6FB3)", cursor: "pointer", textDecoration: "underline", textUnderlineOffset: 2
            }}>Show full detail</button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
