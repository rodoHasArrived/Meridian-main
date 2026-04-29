// ============================================================
// Meridian Dashboard UI Kit
// Load AFTER React + Babel, as <script type="text/babel" src=".../components.jsx">
// Exposes: PanelSurface, Eyebrow, Button, Badge, MetricCard, Input,
//          StatusBanner, NavItem, DataTable → window
// ============================================================

function PanelSurface({ children, strong, className = "", style = {}, ...rest }) {
  const base = {
    borderRadius: 10,
    border: "1px solid hsl(var(--border))",
    background: strong ? "hsl(var(--panel-strong))" : "hsl(var(--card))",
    boxShadow: "0 1px 0 rgba(255,255,255,0.02) inset, 0 1px 2px rgba(0,0,0,0.30)",
    ...style,
  };
  return <div style={base} className={className} {...rest}>{children}</div>;
}

function Eyebrow({ children, style = {} }) {
  return (
    <div style={{
      fontSize: 10, fontWeight: 600, textTransform: "uppercase",
      letterSpacing: "0.24em", color: "hsl(var(--muted-foreground))", ...style
    }}>{children}</div>
  );
}

function Button({ variant = "primary", size = "md", icon, children, style = {}, ...rest }) {
  const pad = size === "sm" ? "4px 10px" : "6px 12px";
  const fs = size === "sm" ? 11 : 12;
  const rad = 6;
  const variants = {
    primary:     { background: "#2AB2D4", color: "#05101B", border: "1px solid transparent" },
    secondary:   { background: "#162334", color: "hsl(var(--foreground))", border: "1px solid #1F344C" },
    outline:     { background: "transparent", color: "hsl(var(--foreground))", border: "1px solid #1F344C" },
    ghost:       { background: "transparent", color: "hsl(var(--muted-foreground))", border: "1px solid transparent" },
    destructive: { background: "rgba(222,88,120,0.08)", color: "#DE5878", border: "1px solid rgba(222,88,120,0.30)" },
  };
  return (
    <button style={{
      display: "inline-flex", alignItems: "center", gap: 6, padding: pad, fontSize: fs,
      fontWeight: 500, fontFamily: "inherit", borderRadius: rad, cursor: "pointer",
      ...variants[variant], ...style,
    }} {...rest}>
      {icon && <span style={{ display: "inline-flex" }}>{icon}</span>}
      {children}
    </button>
  );
}

function Badge({ tone = "outline", children, dot = false, style = {} }) {
  const tones = {
    outline: { bg: "transparent", fg: "hsl(var(--muted-foreground))", bd: "hsl(var(--border))" },
    live:    { bg: "rgba(42,178,212,.12)",  fg: "#2AB2D4", bd: "rgba(42,178,212,.35)" },
    paper:   { bg: "rgba(96,165,250,.12)",  fg: "#60A5FA", bd: "rgba(96,165,250,.35)" },
    research:{ bg: "rgba(128,162,200,.12)", fg: "#93B4DA", bd: "rgba(128,162,200,.35)" },
    success: { bg: "rgba(38,191,134,.10)",  fg: "#26BF86", bd: "rgba(38,191,134,.30)" },
    warning: { bg: "rgba(214,158,56,.10)",  fg: "#D69E38", bd: "rgba(214,158,56,.30)" },
    danger:  { bg: "rgba(222,88,120,.10)",  fg: "#DE5878", bd: "rgba(222,88,120,.30)" },
  }[tone] || {};
  return (
    <span style={{
      display: "inline-flex", alignItems: "center", gap: 5, padding: "2px 8px",
      borderRadius: 3, fontSize: 10, fontWeight: 500, letterSpacing: "0.12em",
      textTransform: "uppercase", fontFamily: "var(--font-mono)",
      background: tones.bg, color: tones.fg, border: `1px solid ${tones.bd}`, ...style
    }}>
      {dot && <span style={{ width: 6, height: 6, borderRadius: 999, background: "currentColor" }} />}
      {children}
    </span>
  );
}

function MetricCard({ label, value, delta, tone = "muted" }) {
  const valColor = { success: "#26BF86", danger: "#DE5878", primary: "#2AB2D4", muted: "hsl(var(--foreground))" }[tone];
  return (
    <div style={{
      background: "hsl(var(--card))", border: "1px solid #1F344C",
      borderRadius: 8, padding: "12px 14px 14px", display: "flex", flexDirection: "column",
      gap: 10, boxShadow: "0 1px 0 rgba(255,255,255,0.02) inset, 0 1px 1px rgba(0,0,0,.25)"
    }}>
      <Eyebrow>{label}</Eyebrow>
      <div style={{
        fontFamily: "var(--font-mono)", fontWeight: 500, fontSize: 22, lineHeight: 1,
        fontVariantNumeric: "tabular-nums", color: valColor, letterSpacing: "-0.01em"
      }}>{value}</div>
      {delta && (
        <div style={{ fontFamily: "var(--font-mono)", fontSize: 11, color: "hsl(var(--muted-foreground))" }}>{delta}</div>
      )}
    </div>
  );
}

function Input({ label, style = {}, ...rest }) {
  return (
    <label style={{ display: "block" }}>
      {label && <div style={{
        fontSize: 10, fontWeight: 600, letterSpacing: "0.14em", textTransform: "uppercase",
        color: "hsl(var(--muted-foreground))", marginBottom: 4
      }}>{label}</div>}
      <input style={{
        width: "100%", borderRadius: 10, border: "1px solid hsl(var(--border))",
        background: "hsl(var(--background))", color: "hsl(var(--foreground))",
        padding: "8px 12px", fontSize: 13, fontFamily: "var(--font-mono)", boxSizing: "border-box", ...style
      }} {...rest} />
    </label>
  );
}

function StatusBanner({ tone = "success", title, detail }) {
  const c = {
    success: { bg: "rgba(38,191,134,.05)",  bd: "rgba(38,191,134,.30)",  fg: "#26BF86" },
    warning: { bg: "rgba(214,158,56,.05)",  bd: "rgba(214,158,56,.30)",  fg: "#D69E38" },
    danger:  { bg: "rgba(222,88,120,.05)",  bd: "rgba(222,88,120,.30)",  fg: "#DE5878" },
  }[tone];
  return (
    <div style={{
      display: "flex", gap: 10, alignItems: "center", padding: "11px 14px",
      borderRadius: 10, border: `1px solid ${c.bd}`, background: c.bg, color: c.fg, fontSize: 13
    }}>
      <div>
        <div style={{ fontWeight: 500 }}>{title}</div>
        {detail && <div style={{ fontSize: 12, color: "hsl(var(--muted-foreground))", marginTop: 2 }}>{detail}</div>}
      </div>
    </div>
  );
}

function NavItem({ icon, label, status, active = false }) {
  return (
    <div style={{
      display: "flex", gap: 10, padding: "10px 12px", borderRadius: 14,
      border: `1px solid ${active ? "rgba(42,178,212,.30)" : "transparent"}`,
      background: active ? "rgba(42,178,212,.10)" : "transparent",
      color: active ? "hsl(var(--foreground))" : "hsl(var(--muted-foreground))",
      fontSize: 13, alignItems: "flex-start"
    }}>
      {icon && <span style={{ width: 16, height: 16, marginTop: 1, flexShrink: 0 }}>{icon}</span>}
      <div>
        <div style={{ fontWeight: 600 }}>{label}</div>
        {status && <div style={{ fontSize: 11, color: "hsl(var(--muted-foreground))", marginTop: 2 }}>{status}</div>}
      </div>
    </div>
  );
}

function DataTable({ columns, rows }) {
  return (
    <div style={{
      border: "1px solid hsl(var(--border) / .7)", borderRadius: 14, overflow: "hidden",
      background: "hsl(var(--card))"
    }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead style={{ background: "hsl(213 32% 16% / .5)" }}>
          <tr>{columns.map((col, i) => (
            <th key={i} style={{
              textAlign: "left", padding: "9px 14px", fontSize: 10, fontWeight: 600,
              textTransform: "uppercase", letterSpacing: "0.14em",
              color: "hsl(var(--muted-foreground))",
              borderBottom: "1px solid hsl(var(--border) / .6)"
            }}>{col}</th>
          ))}</tr>
        </thead>
        <tbody>
          {rows.map((row, ri) => (
            <tr key={ri}>{row.map((cell, ci) => (
              <td key={ci} style={{
                padding: "10px 14px", fontFamily: "var(--font-mono)", fontSize: 13,
                color: (cell && cell.color) || "hsl(var(--foreground))",
                borderBottom: ri === rows.length - 1 ? "none" : "1px solid hsl(var(--border) / .4)",
                fontVariantNumeric: "tabular-nums"
              }}>{cell && cell.value !== undefined ? cell.value : cell}</td>
            ))}</tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

Object.assign(window, {
  PanelSurface, Eyebrow, Button, Badge, MetricCard, Input,
  StatusBanner, NavItem, DataTable,
});
