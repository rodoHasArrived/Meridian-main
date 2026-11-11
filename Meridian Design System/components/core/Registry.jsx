// Meridian registry — ONE lookup for the system's controlled vocabularies.
// Retires four parallel constant carriers (ROLES, CONDITIONS, TIME_ROLES,
// OBJECT_TYPES) and the density naming drift (compact/spacious vs dense/review):
// aliases resolve to canonical keys here, so a screen never has to know which
// spelling a component wanted.
import React from "react";

export const DENSITY = {
  terminal: { label: "Terminal", row: 26, body: 12, aliases: ["wall", "ops"] },
  dense:    { label: "Dense",    row: 28, body: 13, aliases: ["compact"] },
  standard: { label: "Standard", row: 36, body: 14, aliases: ["default", "normal"] },
  review:   { label: "Review",   row: 44, body: 15, aliases: ["spacious", "comfortable"] },
};

export const SEVERITY = {
  ready:   { label: "Ready",   rank: 0 },
  info:    { label: "Info",    rank: 1 },
  review:  { label: "Review",  rank: 2 },
  action:  { label: "Action",  rank: 3 },
  blocked: { label: "Blocked", rank: 4 },
};

export { TIME_ROLES } from "./TimeContext";
import { OBJECT_TYPES } from "./ObjectModel";
import { TIME_ROLES } from "./TimeContext";

export const Registry = {
  density: DENSITY,
  severity: SEVERITY,
  timeRole: TIME_ROLES.reduce((m, r) => { m[r.id] = { label: r.label, note: r.definition }; return m; }, {}),
  seriesRole: { actual: "Actual", prior: "Prior", benchmark: "Benchmark", scenario: "Scenario", target: "Target", estimated: "Estimated" },
  condition: { confirmed: "Confirmed", provisional: "Provisional", estimated: "Estimated", stale: "Stale",
    overridden: "Overridden", missing: "Missing", restricted: "Restricted", na: "Not applicable", pending: "Pending", exception: "Exception", reconciled: "Reconciled" },
  environment: { live: "Live", paper: "Paper", fixture: "Fixture" },
  objectType: OBJECT_TYPES.reduce((m, o) => { m[o.id] = { label: o.label, note: o.plural }; return m; }, {}),
  authority: { source: "Source of record", derived: "Derived", desk: "Desk mark", override: "Override", estimate: "Estimate", unattributed: "Unattributed" },
};

const ALIAS = (() => {
  const m = { density: {}, severity: {} };
  Object.keys(DENSITY).forEach((k) => { m.density[k] = k; (DENSITY[k].aliases || []).forEach((a) => { m.density[a] = k; }); });
  Object.keys(SEVERITY).forEach((k) => { m.severity[k] = k; });
  m.severity.ok = "ready"; m.severity.warning = "review"; m.severity.error = "action"; m.severity.critical = "blocked";
  // Series-role and condition spellings seen in older call sites.
  m.seriesRole = { actual: "actual", current: "actual", prior: "prior", previous: "prior", comparison: "prior",
    benchmark: "benchmark", index: "benchmark", scenario: "scenario", shocked: "scenario",
    target: "target", budget: "target", estimated: "estimated", modelled: "estimated", forecast: "estimated" };
  m.condition = { confirmed: "confirmed", verified: "confirmed", provisional: "provisional", unaudited: "provisional",
    estimated: "estimated", modelled: "estimated", stale: "stale", late: "stale", overridden: "overridden",
    override: "overridden", missing: "missing", absent: "missing", na: "na", "n/a": "na",
    pending: "pending", awaiting: "pending", exception: "exception", broken: "exception", reconciled: "reconciled", tied: "reconciled" };
  return m;
})();

/** Resolve any spelling to the canonical key of a vocabulary. */
export function resolve(vocabulary, value) {
  const v = String(value ?? "").trim().toLowerCase();
  const map = ALIAS[vocabulary];
  if (map && map[v]) return map[v];
  const vocab = Registry[vocabulary] || {};
  if (vocab[v]) return v;
  const hit = Object.keys(vocab).find((k) => k.toLowerCase() === v);
  return hit || null;
}

/** The registry rendered — one card documents every vocabulary at once. */
export function RegistryTable({ vocabulary, className = "", ...rest }) {
  const keys = vocabulary ? [vocabulary] : Object.keys(Registry);
  return (
    <div className={className} style={{ display: "grid", gap: 12 }} {...rest}>
      {keys.map((v) => {
        const vocab = Registry[v] || {};
        return (
          <div key={v} style={{ border: "1px solid var(--border,#D2CDC3)", borderRadius: 2, background: "var(--bg-light,#F8F7F3)" }}>
            <div style={{ padding: "6px 10px", borderBottom: "1px solid var(--border-strong,#AAA49A)", font: "500 10px var(--font-body,system-ui)", fontVariant: "all-small-caps", letterSpacing: ".06em", color: "var(--text-muted,#606870)" }}>{v}</div>
            <div style={{ padding: "4px 10px" }}>
              {Object.keys(vocab).map((k) => {
                const meta = vocab[k];
                const label = typeof meta === "string" ? meta : meta.label;
                const aliases = typeof meta === "object" && meta.aliases ? meta.aliases : [];
                return (
                  <div key={k} style={{ display: "flex", justifyContent: "space-between", gap: 12, padding: "3px 0", borderBottom: "1px solid var(--border-divider,#E2DED5)", fontSize: 11, fontFamily: "var(--font-body,system-ui)", color: "var(--text-secondary,#535C65)" }}>
                    <span style={{ fontFamily: "var(--font-data,ui-monospace)", color: "var(--text-primary,#20252B)" }}>{k}</span>
                    <span>{label}{aliases.length ? ` \u00B7 ${aliases.join(", ")}` : ""}</span>
                  </div>
                );
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}

// Non-enumerable so RegistryTable and resolve() keep iterating vocabularies only.
Object.defineProperty(Registry, "resolve", { value: resolve, enumerable: false });
