// Meridian EmptyState — consistent zero-data placeholder for tables and panels.
import React from "react";

const ICONS = {
  table:   `<svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><line x1="3" y1="9" x2="21" y2="9"/><line x1="3" y1="15" x2="21" y2="15"/><line x1="9" y1="9" x2="9" y2="21"/></svg>`,
  search:  `<svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><line x1="16.5" y1="16.5" x2="21" y2="21"/></svg>`,
  chart:   `<svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/></svg>`,
  docs:    `<svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>`,
  inbox:   `<svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="22 13 16 13 14 16 10 16 8 13 2 13"/><path d="M5.45 5.11L2 13v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-7.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/></svg>`,
};

export function EmptyState({
  icon = "table",
  title = "No data",
  detail,
  action,
  onAction,
  compact = false,
}) {
  const svg = ICONS[icon] ?? ICONS.table;
  return (
    <div style={{
      display:"flex", flexDirection:"column", alignItems:"center", justifyContent:"center",
      padding: compact ? "24px 16px" : "48px 24px",
      gap: compact ? 8 : 12,
      fontFamily:"var(--font-body)", textAlign:"center",
    }}>
      <span style={{ color:"var(--text-disabled,#889099)" }}
        dangerouslySetInnerHTML={{ __html: svg }} />
      <div style={{ fontSize: compact ? 13 : 14, fontWeight:600, color:"var(--text-secondary,#4D5967)" }}>
        {title}
      </div>
      {detail && (
        <div style={{ fontSize:12, color:"var(--text-muted,#59636F)", maxWidth:280, lineHeight:1.5 }}>
          {detail}
        </div>
      )}
      {action && onAction && (
        <button onClick={onAction} style={{
          marginTop:4, padding:"7px 16px",
          background:"var(--accent,#2F6F8F)", color:"white",
          border:"none", borderRadius:"var(--radius-button,2px)",
          fontFamily:"var(--font-body)", fontSize:12, fontWeight:600, cursor:"pointer",
          transition:"background .12s ease",
        }}>
          {action}
        </button>
      )}
    </div>
  );
}
