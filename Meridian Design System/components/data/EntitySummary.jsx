// Meridian entity summary — identity fact band with per-item mono/color control. Light theme.
// 4.1: items carry an optional depth `tier`. Identity facets stay untiered — an object's
// name, id and version never withdraw (guidelines/PROGRESSIVE_DISCLOSURE.md).
import React from "react";

export function EntitySummary({ items, columns = 3, detail = "full", onReveal }) {
  // items: [{ label, value, mono?, color?, tier? }]
  const all = items || [];
  const shown = detail === "summary" ? all.filter((it) => it.tier !== "full") : all;
  const withheld = all.length - shown.length;
  return (
    <div>
      <div style={{
        display: "grid", gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
        gap: "14px 20px"
      }}>
        {shown.map((it, i) => (
          <div key={i} style={{ minWidth: 0 }}>
            <div style={{
              fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 600,
              fontVariant: "all-small-caps", letterSpacing: "0.03em",
              color: "var(--text-muted, #606870)", marginBottom: 3
            }}>{it.label}</div>
            <div style={{
              fontSize: 13,
              fontFamily: it.mono === false ? "var(--font-body)" : "var(--font-data)",
              fontVariantNumeric: "tabular-nums",
              color: it.color || "var(--text-primary, #20252B)",
              overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap"
            }}>{it.value}</div>
          </div>
        ))}
      </div>
      {withheld > 0 ? (
        <div style={{
          display: "flex", justifyContent: "space-between", gap: 12, alignItems: "baseline",
          marginTop: 12, paddingTop: 8, borderTop: "1px solid var(--border-divider, #E2DED5)",
          fontFamily: "var(--font-body)", fontSize: 11, color: "var(--text-muted, #606870)"
        }}>
          <span>{withheld} facet{withheld === 1 ? "" : "s"} withheld at summary depth</span>
          {onReveal ? (
            <button type="button" onClick={() => onReveal("full")} style={{
              padding: 0, background: "transparent", border: 0, font: "inherit", fontWeight: 500,
              color: "var(--link, #2A5F96)", cursor: "pointer", textDecoration: "underline", textUnderlineOffset: 2
            }}>Show full detail</button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
