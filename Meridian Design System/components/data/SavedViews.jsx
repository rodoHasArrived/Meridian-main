// Meridian SavedViews — named, persisted table views ("My breaks", "Live fills only"): a
// snapshot of query + sort stack + filters that operators save, re-apply, and share a vocabulary
// around. Pairs with useTableState (getViewState/applyViewState) or any getState/applyState
// pair. Presets are locked; saved views persist to localStorage under `storageKey`.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-savedviews{position:relative;display:inline-block;font-family:var(--font-body);}
.mds-savedviews__trigger{display:inline-flex;align-items:center;gap:7px;height:28px;padding:0 10px;box-sizing:border-box;
  background:var(--bg-light,#fff);border:1px solid var(--border,#CBD3DC);cursor:pointer;font:inherit;font-size:12px;color:var(--text-muted,#59636F);}
.mds-savedviews__trigger:hover{border-color:var(--border-hover,#ADB8C4);}
.mds-savedviews__trigger:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-savedviews__eyebrow{font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.05em;}
.mds-savedviews__name{font-weight:600;color:var(--text-primary,#22272E);max-width:180px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-savedviews__dirty{font-family:var(--font-data);font-size:10.5px;color:var(--orange-dim,#8A520E);}
.mds-savedviews__caret{font-size:9px;color:var(--text-muted,#59636F);}
.mds-savedviews__menu{position:absolute;top:calc(100% + 4px);left:0;z-index:1000;min-width:252px;
  background:var(--bg-light,#fff);border:1px solid var(--border-strong,#99A5B2);box-shadow:var(--shadow-menu,0 2px 6px rgba(0,0,0,.18));padding:4px 0;}
.mds-savedviews__label{padding:6px 12px 3px;font-size:9.5px;font-weight:600;font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-muted,#59636F);}
.mds-savedviews__item{display:flex;align-items:center;gap:8px;}
.mds-savedviews__row{display:flex;align-items:center;gap:8px;flex:1;min-width:0;padding:6px 12px;border:0;background:none;
  font:inherit;font-size:12.5px;color:var(--text-primary,#22272E);cursor:pointer;text-align:left;}
.mds-savedviews__item:hover{background:var(--bg-medium,#EBEFF4);}
.mds-savedviews__item[data-active="true"]{background:var(--blue-a10);box-shadow:inset 2px 0 0 var(--accent,#2F6F8F);}
.mds-savedviews__meta{margin-left:auto;flex:none;font-family:var(--font-data);font-size:10px;color:var(--text-muted,#59636F);}
.mds-savedviews__x{flex:none;border:0;background:none;cursor:pointer;color:var(--text-muted,#59636F);font-size:13px;line-height:1;padding:4px 10px 4px 2px;}
.mds-savedviews__x:hover{color:var(--red-dim,#8C2F40);}
.mds-savedviews__save{display:flex;gap:6px;padding:8px 12px 6px;border-top:1px solid var(--border,#CBD3DC);margin-top:4px;}
.mds-savedviews__input{flex:1;min-width:0;height:26px;box-sizing:border-box;padding:0 8px;border:1px solid var(--border,#CBD3DC);
  background:var(--bg-light,#fff);color:var(--text-primary,#22272E);font-family:var(--font-data);font-size:12px;}
.mds-savedviews__input:focus{border-color:var(--border-focus,#2F6F8F);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-savedviews__btn{flex:none;height:26px;padding:0 10px;border:1px solid var(--border,#CBD3DC);background:var(--bg-medium,#EBEFF4);
  font:inherit;font-size:12px;font-weight:600;color:var(--text-primary,#22272E);cursor:pointer;}
.mds-savedviews__btn:hover:not(:disabled){border-color:var(--border-hover,#ADB8C4);}
.mds-savedviews__btn:disabled{opacity:.5;cursor:not-allowed;}
.mds-savedviews__update{display:block;width:calc(100% - 24px);margin:0 12px 6px;height:26px;border:1px solid var(--state-warn-bd);
  background:var(--orange-a10);font:inherit;font-size:12px;font-weight:600;color:var(--orange-dim,#8A520E);cursor:pointer;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "savedviews");
  el.textContent = css;
  document.head.appendChild(el);
}

const readJSON = (k, fallback) => {
  try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : fallback; } catch (e) { return fallback; }
};

export function SavedViews({
  storageKey = null,
  table = null,
  getState = null,
  applyState = null,
  presets = [],
  defaultLabel = "All rows",
  label = "View",
  onApply = null,
}) {
  inject();
  const [open, setOpen] = React.useState(false);
  const [views, setViews] = React.useState(() => (storageKey ? readJSON(storageKey + ".views", []) : []));
  const [active, setActive] = React.useState(() => (storageKey ? localStorage.getItem(storageKey + ".active") : null));
  const [draft, setDraft] = React.useState("");
  const rootRef = React.useRef(null);

  const capture = () => {
    if (getState) return getState();
    if (table && table.getViewState) return table.getViewState();
    if (table) return { query: table.query || "", sortStack: table.sortStack || [], filters: table.filters || {} };
    return {};
  };
  const applyTo = (state) => {
    if (applyState) applyState(state || {});
    else if (table && table.applyViewState) table.applyViewState(state || {});
  };
  const persist = (nextViews, nextActive) => {
    setViews(nextViews);
    setActive(nextActive);
    if (!storageKey) return;
    localStorage.setItem(storageKey + ".views", JSON.stringify(nextViews));
    if (nextActive) localStorage.setItem(storageKey + ".active", nextActive);
    else localStorage.removeItem(storageKey + ".active");
  };

  React.useEffect(() => {
    if (!open) return;
    const onDoc = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("mousedown", onDoc); document.removeEventListener("keydown", onKey); };
  }, [open]);

  const activeEntry = active
    ? presets.find((p) => p.name === active) || views.find((v) => v.name === active)
    : null;
  const dirty = !!activeEntry && JSON.stringify(capture()) !== JSON.stringify(activeEntry.state);
  const isPresetActive = !!active && presets.some((p) => p.name === active);

  const apply = (name, state) => {
    applyTo(state);
    persist(views, name);
    setOpen(false);
    if (onApply) onApply(name, state);
  };
  const reset = () => apply(null, {});
  const saveDraft = () => {
    const name = draft.trim();
    if (!name || presets.some((p) => p.name === name)) return;
    const state = capture();
    const next = views.some((v) => v.name === name)
      ? views.map((v) => (v.name === name ? { ...v, state } : v))
      : [...views, { name, state }];
    persist(next, name);
    setDraft("");
    setOpen(false);
    if (onApply) onApply(name, state);
  };
  const updateActive = () => {
    const state = capture();
    persist(views.map((v) => (v.name === active ? { ...v, state } : v)), active);
    setOpen(false);
  };
  const remove = (name) => {
    persist(views.filter((v) => v.name !== name), active === name ? null : active);
  };

  const row = (v, preset) => (
    <div key={(preset ? "p:" : "v:") + v.name} className="mds-savedviews__item" data-active={active === v.name ? "true" : "false"}>
      <button type="button" className="mds-savedviews__row" onClick={() => apply(v.name, v.state)}>
        <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{v.name}</span>
        {preset && <span className="mds-savedviews__meta">preset</span>}
      </button>
      {!preset && (
        <button type="button" className="mds-savedviews__x" aria-label={"Delete view " + v.name}
          onClick={(e) => { e.stopPropagation(); remove(v.name); }}>{"\u00d7"}</button>
      )}
    </div>
  );

  return (
    <div ref={rootRef} className="mds-savedviews">
      <button type="button" className="mds-savedviews__trigger" aria-haspopup="menu" aria-expanded={open}
        onClick={() => setOpen((o) => !o)}>
        <span className="mds-savedviews__eyebrow">{label}</span>
        <span className="mds-savedviews__name">{active || defaultLabel}</span>
        {dirty && <span className="mds-savedviews__dirty">{"\u00b7 edited"}</span>}
        <span className="mds-savedviews__caret">{"\u25be"}</span>
      </button>

      {open && (
        <div className="mds-savedviews__menu" role="menu">
          <div className="mds-savedviews__item" data-active={active ? "false" : "true"}>
            <button type="button" className="mds-savedviews__row" onClick={reset}>{defaultLabel}</button>
          </div>
          {presets.length > 0 && <div className="mds-savedviews__label">Presets</div>}
          {presets.map((p) => row(p, true))}
          {views.length > 0 && <div className="mds-savedviews__label">Saved</div>}
          {views.map((v) => row(v, false))}
          {dirty && active && !isPresetActive && (
            <React.Fragment>
              <div style={{ height: 6 }}></div>
              <button type="button" className="mds-savedviews__update" onClick={updateActive}>
                {"Update \u201c" + active + "\u201d"}
              </button>
            </React.Fragment>
          )}
          <div className="mds-savedviews__save">
            <input className="mds-savedviews__input" placeholder={"Save current as\u2026"} value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") saveDraft(); }} />
            <button type="button" className="mds-savedviews__btn" disabled={!draft.trim()} onClick={saveDraft}>Save</button>
          </div>
        </div>
      )}
    </div>
  );
}

SavedViews.displayName = "SavedViews";
