// Meridian disclosure — 4.1.  One detail dial per screen, per-region overrides, and regions
// that state their own residue while collapsed. Depth is never concealment: a collapsed region
// still reports count, worst severity and freshness, and a summary view always states how much
// it withheld and how to get it back (guidelines/PROGRESSIVE_DISCLOSURE.md).
import React from "react";
import { SegmentedControl } from "./SegmentedControl";
import { Badge } from "./Badge";

/** Two levels, always the same two. A third level is a second screen. */
export const DETAIL_LEVELS = ["summary", "full"];
const SEVERITY_RANK = { none: 0, info: 1, warning: 2, critical: 3 };
const SEVERITY_BADGE = { info: "info", warning: "warning", critical: "danger" };
const SEVERITY_GLYPH = { info: "\u25CB", warning: "\u25B2", critical: "\u25B2" };

const Ctx = React.createContext(null);
const storeKey = (screenId) => `mds.disclosure.${screenId}`;

function readStore(screenId, storage) {
  try {
    const store = storage || (typeof window !== "undefined" ? window.localStorage : null);
    const raw = store?.getItem(storeKey(screenId));
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}
function writeStore(screenId, value, storage) {
  try {
    const store = storage || (typeof window !== "undefined" ? window.localStorage : null);
    store?.setItem(storeKey(screenId), JSON.stringify(value));
  } catch { /* reading depth is never load-bearing */ }
}
function readUrl(param) {
  if (!param || typeof window === "undefined") return null;
  try {
    const v = new URLSearchParams(window.location.search).get(param);
    return DETAIL_LEVELS.includes(v) ? v : null;
  } catch { return null; }
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-disc{border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);}
.mds-disc + .mds-disc{border-top:0;}
.mds-disc__hd{display:flex;align-items:center;gap:10px;width:100%;box-sizing:border-box;
  padding:8px 10px;background:var(--bg-light,#F8F7F3);border:0;border-bottom:1px solid transparent;
  font-family:var(--font-body,system-ui);text-align:left;}
.mds-disc--open > .mds-disc__hd{border-bottom-color:var(--border-divider,#E2DED5);}
.mds-disc__btn{display:flex;align-items:center;gap:8px;flex:1;min-width:0;padding:0;margin:0;
  background:transparent;border:0;font:inherit;color:inherit;cursor:pointer;text-align:left;}
.mds-disc__btn:hover .mds-disc__title{color:var(--interaction-selected,#496C8F);}
.mds-disc__btn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px;}
.mds-disc__caret{flex:0 0 auto;width:10px;font-size:10px;line-height:1;color:var(--text-muted,#6B737B);}
.mds-disc__title{font-size:11px;font-weight:var(--weight-semibold,600);letter-spacing:.06em;
  text-transform:uppercase;color:var(--text-secondary,#535C65);white-space:nowrap;}
.mds-disc__kicker{font-family:var(--font-data,ui-monospace);font-size:10px;letter-spacing:.04em;
  color:var(--text-muted,#6B737B);}
.mds-disc__residue{display:flex;align-items:center;gap:8px;flex:1;min-width:0;
  font-family:var(--font-data,ui-monospace);font-size:10.5px;color:var(--text-muted,#6B737B);
  font-variant-numeric:tabular-nums;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-disc__sev{display:inline-flex;align-items:center;gap:4px;font-family:var(--font-body,system-ui);
  font-size:10px;font-weight:var(--weight-medium,500);}
.mds-disc__sev--warning{color:var(--amber,#946200);}
.mds-disc__sev--critical{color:var(--red,#B54B42);}
.mds-disc__sev--info{color:var(--text-secondary,#535C65);}
.mds-disc__tools{display:flex;align-items:center;gap:8px;flex:0 0 auto;}
.mds-disc__body{padding:12px 12px 14px;background:var(--bg-surface,#FCFBF8);
  border-top:1px solid var(--border-divider,#E2DED5);}
.mds-disc__body--bare{padding:0;}
.mds-disc__withheld{display:flex;align-items:baseline;justify-content:space-between;gap:12px;
  padding:6px 10px;background:var(--bg-light,#F8F7F3);border-top:1px solid var(--border-divider,#E2DED5);
  font-family:var(--font-body,system-ui);font-size:11px;color:var(--text-muted,#6B737B);}
.mds-disc__reveal{padding:0;background:transparent;border:0;font:inherit;font-weight:var(--weight-medium,500);
  color:var(--link,#2D6FB3);cursor:pointer;text-decoration:underline;text-underline-offset:2px;}
.mds-disc__reveal:hover{color:var(--link-hover,#1F5285);}
.mds-disc__reveal:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px;}
.mds-disc__dial{display:inline-flex;align-items:center;gap:6px;}
.mds-disc__inherit{padding:0;background:transparent;border:0;font-family:var(--font-body,system-ui);
  font-size:10px;color:var(--text-muted,#6B737B);cursor:pointer;text-decoration:underline;
  text-underline-offset:2px;white-space:nowrap;}
.mds-disc__inherit:hover{color:var(--text-secondary,#535C65);}
.mds-disc__inherit:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "disclosure");
  el.textContent = css;
  document.head.appendChild(el);
}

/**
 * Screen-scoped reading depth. Persists the dial, region overrides and operator-pinned
 * collapse under `mds.disclosure.<screenId>`; mirrors the screen dial into `?detail=` so a
 * link carries how the sender was reading. Never persists filters, period or as-of —
 * arrangement only (guidelines/SESSION_CONTINUITY.md).
 */
export function DisclosureProvider({
  screenId = "global", detail: controlled, defaultDetail = "full", regions: defaultRegions,
  persist = true, urlParam = "detail", storage, onChange, children,
}) {
  const [state, setState] = React.useState(() => {
    const saved = persist ? readStore(screenId, storage) : null;
    const fromUrl = readUrl(urlParam);
    return {
      detail: fromUrl || saved?.detail || defaultDetail,
      regions: { ...(defaultRegions || null), ...(saved?.regions || null) },
      open: saved?.open || {},
    };
  });
  const detail = controlled || state.detail;

  const commit = React.useCallback((updater) => {
    setState((prev) => {
      const next = typeof updater === "function" ? updater(prev) : updater;
      if (persist) writeStore(screenId, next, storage);
      return next;
    });
  }, [persist, screenId, storage]);

  React.useEffect(() => {
    if (!urlParam || typeof window === "undefined") return;
    try {
      const u = new URL(window.location.href);
      if (u.searchParams.get(urlParam) !== detail) {
        u.searchParams.set(urlParam, detail);
        window.history.replaceState(null, "", u);
      }
    } catch { /* file:// and sandboxed frames */ }
  }, [detail, urlParam]);

  const api = React.useMemo(() => {
    const detailFor = (id) => (id && state.regions[id]) || detail;
    return {
      screenId, detail, summary: detail === "summary", levels: DETAIL_LEVELS,
      regions: state.regions,
      setDetail: (level) => {
        if (!DETAIL_LEVELS.includes(level)) return;
        commit((prev) => ({ ...prev, detail: level }));
        onChange?.({ screenId, detail: level, scope: "screen" });
      },
      detailFor,
      /** null clears the override — the region follows the screen dial again. */
      setDetailFor: (id, level) => {
        if (!id) return;
        commit((prev) => {
          const next = { ...prev.regions };
          if (level == null) delete next[id]; else next[id] = level;
          return { ...prev, regions: next };
        });
        onChange?.({ screenId, detail: level, scope: "region", region: id });
      },
      overridden: (id) => !!(id && state.regions[id]),
      /** Operator collapse wins; otherwise the region follows the dial. */
      isOpen: (id, fallback) => (id && id in state.open ? state.open[id] : fallback !== false),
      pinned: (id) => !!(id && id in state.open),
      setOpen: (id, open) => id && commit((prev) => ({ ...prev, open: { ...prev.open, [id]: !!open } })),
      toggle: (id, fallback) => id && commit((prev) => {
        const current = id in prev.open ? prev.open[id] : fallback !== false;
        return { ...prev, open: { ...prev.open, [id]: !current } };
      }),
      /** Forget operator pins so every region follows the dial again. */
      reset: () => commit((prev) => ({ ...prev, regions: {}, open: {} })),
    };
  }, [screenId, detail, state.regions, state.open, commit, onChange]);

  return <Ctx.Provider value={api}>{children}</Ctx.Provider>;
}

/** Null outside a provider — every consumer degrades to full detail rather than hiding. */
export function useDisclosure() { return React.useContext(Ctx); }

const FALLBACK = {
  detail: "full", summary: false, levels: DETAIL_LEVELS, regions: {},
  setDetail: () => {}, detailFor: () => "full", setDetailFor: () => {}, overridden: () => false,
  isOpen: (id, fallback) => fallback !== false, pinned: () => false, setOpen: () => {},
  toggle: () => {}, reset: () => {},
};
function useDisc() { return React.useContext(Ctx) || FALLBACK; }

/**
 * The dial. No `region` → the screen's depth, and it belongs in the chrome beside density,
 * not inside the work plane. With `region` → that region's override, with a reset back to
 * the screen once one is set.
 */
export function DetailDial({
  region, labels = { summary: "Summary", full: "Full" }, size = "sm", fullWidth = false,
  showReset = true, resetLabel = "follows screen", className = "", ...rest
}) {
  inject();
  const d = useDisc();
  const value = region ? d.detailFor(region) : d.detail;
  const overridden = region ? d.overridden(region) : false;
  return (
    <span className={`mds-disc__dial${className ? " " + className : ""}`} {...rest}>
      <SegmentedControl size={size} fullWidth={fullWidth} value={value}
        aria-label={region ? `Detail level — ${region}` : "Detail level"}
        options={DETAIL_LEVELS.map((l) => ({ value: l, label: labels[l] || l }))}
        onChange={(v) => (region ? d.setDetailFor(region, v) : d.setDetail(v))} />
      {region && showReset ? (
        overridden
          ? <button type="button" className="mds-disc__inherit" onClick={() => d.setDetailFor(region, null)}>{"\u21BA"} screen</button>
          : <span className="mds-disc__kicker">{resetLabel}</span>
      ) : null}
    </span>
  );
}

export function severityOf(list = []) {
  return list.reduce((worst, s) => ((SEVERITY_RANK[s] || 0) > (SEVERITY_RANK[worst] || 0) ? s : worst), "none");
}

/** The residue a collapsed region still owes: how much, how bad, how old. */
export function DisclosureResidue({ count, countLabel, severity, severityLabel, freshness, children }) {
  inject();
  const sev = severity && severity !== "none" ? severity : null;
  return (
    <span className="mds-disc__residue">
      {count != null ? <span>{count}{countLabel ? " " + countLabel : ""}</span> : null}
      {sev ? (
        <span className={`mds-disc__sev mds-disc__sev--${sev}`}>
          <span aria-hidden="true">{SEVERITY_GLYPH[sev] || "\u25B2"}</span>
          {severityLabel || sev}
        </span>
      ) : null}
      {freshness ? <span title="Last observed">{"\u25F7 "}{freshness}</span> : null}
      {children}
    </span>
  );
}

/**
 * A collapsible region of a workstation. `secondary` regions start collapsed at summary depth
 * and open at full depth; an operator's own toggle outranks the dial from then on. The header
 * is the region's standing report — it renders identically collapsed or open.
 */
export function DisclosureRegion({
  id, title, kicker, secondary = false, defaultOpen, dial = false, count, countLabel,
  severity, severityLabel, freshness, residue, actions, withheld, withheldLabel = "withheld at summary depth",
  revealLabel, bare = false, className = "", children, ...rest
}) {
  inject();
  const d = useDisc();
  const rid = id || (typeof title === "string" ? title : null);
  const level = d.detailFor(rid);
  const fallback = defaultOpen !== undefined ? defaultOpen : !secondary || level === "full";
  const open = d.isOpen(rid, fallback);
  const bodyId = rid ? `mds-disc-${String(rid).replace(/\W+/g, "-")}` : undefined;
  const hasResidue = residue !== undefined || count != null || (severity && severity !== "none") || freshness;
  const showWithheld = withheld > 0 && level === "summary" && open;
  return (
    <section className={`mds-disc${open ? " mds-disc--open" : ""}${className ? " " + className : ""}`} {...rest}>
      <div className="mds-disc__hd">
        <button type="button" className="mds-disc__btn" aria-expanded={open} aria-controls={bodyId}
          onClick={() => d.toggle(rid, fallback)}>
          <span className="mds-disc__caret" aria-hidden="true">{open ? "\u2304" : "\u203A"}</span>
          <span className="mds-disc__title">{title}</span>
          {kicker ? <span className="mds-disc__kicker">{kicker}</span> : null}
        </button>
        {hasResidue ? (
          residue !== undefined && residue !== null && typeof residue !== "string"
            ? <span className="mds-disc__residue">{residue}</span>
            : <DisclosureResidue count={count} countLabel={countLabel} severity={severity}
                severityLabel={severityLabel} freshness={freshness}>{residue}</DisclosureResidue>
        ) : <span className="mds-disc__residue" />}
        {(dial || actions) ? (
          <span className="mds-disc__tools">
            {actions}
            {dial ? <DetailDial region={rid} showReset={false} /> : null}
          </span>
        ) : null}
      </div>
      {open ? (
        <div id={bodyId} className={`mds-disc__body${bare ? " mds-disc__body--bare" : ""}`}>{children}</div>
      ) : null}
      {showWithheld ? (
        <div className="mds-disc__withheld">
          <span>{withheld} {withheldLabel}</span>
          <button type="button" className="mds-disc__reveal" onClick={() => d.setDetailFor(rid, "full")}>
            {revealLabel || "Show full detail"}
          </button>
        </div>
      ) : null}
    </section>
  );
}

/** Renders children only at full depth, and states what it is holding back. */
export function DetailOnly({ region, label, children, notice = true }) {
  inject();
  const d = useDisc();
  if (d.detailFor(region) === "full") return <>{children}</>;
  if (!notice) return null;
  return (
    <div className="mds-disc__withheld">
      <span>{label || "Detail withheld at summary depth"}</span>
      <button type="button" className="mds-disc__reveal"
        onClick={() => (region ? d.setDetailFor(region, "full") : d.setDetail("full"))}>Show</button>
    </div>
  );
}

/** Level of a tier against a depth: `tier: "full"` rows/fields/columns hide at summary. */
export function atDepth(tier, detail) { return tier !== "full" || detail === "full"; }

DisclosureProvider.displayName = "DisclosureProvider";
DetailDial.displayName = "DetailDial";
DisclosureRegion.displayName = "DisclosureRegion";
DetailDial.levels = DETAIL_LEVELS;
DisclosureRegion.atDepth = atDepth;
DisclosureRegion.severityOf = severityOf;
// Lowercase exports never reach the namespace, so the hook rides a capitalized carrier.
export const DisclosureHooks = { useDisclosure, atDepth, severityOf };
/** The file's carrier — every disclosure primitive reachable as one object. */
export const Disclosure = {
  Provider: DisclosureProvider, Dial: DetailDial, Region: DisclosureRegion,
  Residue: DisclosureResidue, DetailOnly, useDisclosure, atDepth, severityOf,
  LEVELS: DETAIL_LEVELS,
};
