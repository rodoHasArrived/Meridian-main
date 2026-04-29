// ============================================================
// Meridian Dashboard UI Kit
// Load AFTER React + Babel, as <script type="text/babel" src=".../components.jsx">
// Exposes: PanelSurface, Eyebrow, Button, Badge, MetricCard, Input,
//          StatusBanner, NavItem, DataTable, WorkstationShell,
//          ToolbarStrip, DenseDataTable, EntitySummary → window
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
      border: "1px solid hsl(var(--border) / .7)", borderRadius: 8, overflow: "hidden",
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

function WorkstationShell({ brand = "Meridian", subtitle = "Workstation", brandMark = "assets/brand/meridian-mark.svg", search = "Search securities, reports, orders…", actions, nav = [], activeNav, children }) {
  return (
    <div style={{
      minHeight: "100vh", display: "grid", gridTemplateRows: "52px 1fr",
      background: "hsl(var(--background))", backgroundImage: "var(--bg-ambient)",
      color: "var(--fg)", fontFamily: "var(--font-sans)"
    }}>
      <div style={{
        display: "grid", gridTemplateColumns: "minmax(220px,280px) minmax(240px,1fr) auto",
        alignItems: "center", gap: 16, padding: "0 18px",
        background: "#05101B", borderBottom: "1px solid var(--border-color)",
        boxShadow: "var(--shadow-panel)"
      }}>
        <div style={{ display: "inline-flex", alignItems: "center", gap: 10, minWidth: 0 }}>
          <img src={brandMark} alt="" style={{ width: 28, height: 28 }} />
          <div>
            <div style={{ fontFamily: "var(--font-display)", fontSize: 14, fontWeight: 600, color: "#FFF" }}>{brand}</div>
            <div style={{ fontFamily: "var(--font-mono)", fontSize: 9.5, letterSpacing: ".14em", textTransform: "uppercase", color: "var(--cyan-primary)" }}>{subtitle}</div>
          </div>
        </div>
        <div style={{
          height: 30, maxWidth: 680, display: "flex", alignItems: "center", gap: 10,
          padding: "0 12px", border: "1px solid var(--border-color)", borderRadius: 4,
          background: "#0B1520", color: "var(--fg-muted)", fontFamily: "var(--font-mono)", fontSize: 11
        }}>
          <span>⌕</span><span>{search}</span>
        </div>
        <div style={{ display: "inline-flex", alignItems: "center", gap: 8, justifyContent: "flex-end", fontFamily: "var(--font-mono)", fontSize: 11, color: "var(--fg-muted)" }}>
          {actions || <><span>Alerts <b style={{ color: "var(--cyan-primary)" }}>0</b></span><span>Tasks <b style={{ color: "var(--cyan-primary)" }}>0</b></span></>}
        </div>
      </div>
      <div style={{ minHeight: 0, display: "grid", gridTemplateColumns: "248px minmax(0,1fr)" }}>
        <nav style={{ minHeight: 0, overflow: "auto", padding: "12px 10px", background: "#08131F", borderRight: "1px solid var(--border-color)" }}>
          {nav.map((item) => {
            const active = activeNav ? item.label === activeNav : item.active;
            return (
              <div key={item.label} style={{
                display: "grid", gridTemplateColumns: "16px 1fr auto", gap: 9, alignItems: "center",
                minHeight: 32, padding: "6px 10px", border: `1px solid ${active ? "rgba(42,178,212,.28)" : "transparent"}`,
                borderRadius: 4, background: active ? "rgba(42,178,212,.10)" : "transparent",
                boxShadow: active ? "inset 3px 0 0 var(--cyan-primary)" : "none",
                color: active ? "#FFF" : "var(--fg-muted)", fontSize: 12
              }}>
                <span style={{ color: active ? "var(--cyan-primary)" : "var(--fg-muted)" }}>{item.icon || "·"}</span>
                <span>{item.label}</span>
                <span style={{ fontFamily: "var(--font-mono)", fontSize: 9.5, color: active ? "var(--cyan-primary)" : "var(--fg-muted)" }}>{item.status}</span>
              </div>
            );
          })}
        </nav>
        <main style={{ minWidth: 0, minHeight: 0 }}>{children}</main>
      </div>
    </div>
  );
}

function ToolbarStrip({ items = [], right }) {
  return (
    <div style={{
      display: "flex", alignItems: "center", gap: 8, minWidth: 0, padding: "0 18px",
      height: 44, background: "#0B1520", borderBottom: "1px solid var(--border-color)",
      fontFamily: "var(--font-mono)", fontSize: 11
    }}>
      {items.map((item) => (
        <span key={item.label || item} style={{
          display: "inline-flex", alignItems: "center", gap: 6, height: 26, padding: "0 10px",
          border: `1px solid ${item.active ? "rgba(42,178,212,.42)" : "var(--border-color)"}`,
          borderRadius: 4, background: item.active ? "rgba(42,178,212,.10)" : "#08101A",
          color: item.active ? "var(--cyan-primary)" : "var(--fg)", whiteSpace: "nowrap"
        }}>{item.label || item}</span>
      ))}
      <span style={{ flex: "1 1 auto" }} />
      {right}
    </div>
  );
}

function DenseDataTable({ columns, rows, selectedIndex = -1 }) {
  return (
    <div style={{ maxWidth: "100%", overflow: "auto" }}>
      <table style={{
        width: "100%", minWidth: 780, borderCollapse: "collapse", fontFamily: "var(--font-mono)",
        fontSize: 11.5, fontVariantNumeric: "tabular-nums"
      }}>
        <thead><tr>{columns.map((col, i) => (
          <th key={i} style={{
            position: "sticky", top: 0, zIndex: 1, padding: "8px 10px",
            borderBottom: "1px solid var(--border-color)", background: "#08101A",
            color: "var(--fg-muted)", fontSize: 9.5, fontWeight: 500, letterSpacing: ".12em",
            textAlign: col.align === "right" ? "right" : "left", textTransform: "uppercase"
          }}>{col.label || col}</th>
        ))}</tr></thead>
        <tbody>{rows.map((row, ri) => (
          <tr key={ri}>{row.map((cell, ci) => {
            const col = columns[ci] || {};
            return (
              <td key={ci} style={{
                padding: "7px 10px", borderBottom: "1px solid #142036", whiteSpace: "nowrap",
                background: ri === selectedIndex ? "rgba(42,178,212,.14)" : (ri % 2 === 1 ? "rgba(255,255,255,.018)" : "transparent"),
                color: (cell && cell.color) || (ri === selectedIndex ? "#FFF" : "var(--fg)"),
                textAlign: col.align === "right" ? "right" : "left"
              }}>{cell && cell.value !== undefined ? cell.value : cell}</td>
            );
          })}</tr>
        ))}</tbody>
      </table>
    </div>
  );
}

function EntitySummary({ fields = [] }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(2,minmax(0,1fr))" }}>
      {fields.map((field, i) => (
        <div key={field.label} style={{
          minWidth: 0, padding: "11px 14px",
          borderRight: i % 2 === 0 ? "1px solid #142036" : 0,
          borderBottom: "1px solid #142036"
        }}>
          <div style={{ fontFamily: "var(--font-mono)", fontSize: 9.5, letterSpacing: ".12em", textTransform: "uppercase", color: "var(--fg-muted)" }}>{field.label}</div>
          <div style={{ marginTop: 5, fontFamily: "var(--font-mono)", fontSize: 12, color: "#FFF", wordBreak: "break-word" }}>{field.value}</div>
        </div>
      ))}
    </div>
  );
}

Object.assign(window, {
  PanelSurface, Eyebrow, Button, Badge, MetricCard, Input,
  StatusBanner, NavItem, DataTable, WorkstationShell,
  ToolbarStrip, DenseDataTable, EntitySummary,
});
