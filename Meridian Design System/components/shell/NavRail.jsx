// Meridian operator rail — mirrors the light sidebar in ThemeTokens/ThemeControls:
// 14rem paper rail (#F4F6F8), small-caps section labels, nav items with a 3px teal-blue
// left indicator on the active item.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.op-rail{width:14rem;flex-shrink:0;box-sizing:border-box;height:100%;overflow:auto;
  background:var(--sidebar-bg,#F4F6F8);border-right:1px solid var(--sidebar-border,#D7DCE2);
  padding:10px 8px;font-family:var(--font-body);}
.op-rail__nav{display:flex;flex-direction:column;gap:2px;}
.op-rail__section{padding:10px 10px 5px;font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.04em;color:var(--nav-section-label,#6E7781);}
.op-nav-item{display:grid;grid-template-columns:16px minmax(0,1fr) max-content;gap:8px;
  align-items:center;min-height:34px;padding:0 9px;border:none;
  border-left:3px solid transparent;border-radius:0 6px 6px 0;background:transparent;
  color:var(--nav-item,#4D5967);font-size:13px;text-align:left;cursor:pointer;width:100%;
  transition:background-color .1s ease,color .1s ease;}
.op-nav-item:hover{background:var(--sidebar-hover,#E9EEF3);color:var(--nav-item-hover,#22272E);}
.op-nav-item.active{background:var(--sidebar-active,#E1EAF2);
  border-left-color:var(--sidebar-active-ind,#2F6F8F);
  color:var(--nav-item-active,#1F2933);font-weight:600;}
.op-nav-item__icon{width:16px;height:16px;opacity:.7;}
.op-nav-item.active .op-nav-item__icon{opacity:1;}
.op-nav-item__shortcut{font-family:var(--font-data);font-size:10px;
  color:var(--text-muted,#6E7781);border:1px solid var(--border,#D7DCE2);
  border-radius:3px;padding:1px 5px;background:var(--bg-light,#fff);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "navrail");
  el.textContent = css;
  document.head.appendChild(el);
}

export function NavRail({ sections, activeId, onSelect }) {
  inject();
  return (
    <nav className="op-rail">
      {sections.map((sec, si) => (
        <div key={si} style={{ marginBottom: 10 }}>
          <div className="op-rail__section">{sec.label}</div>
          <div className="op-rail__nav">
            {sec.items.map((it) => (
              <button key={it.id} className={`op-nav-item${it.id === activeId ? " active" : ""}`}
                      onClick={onSelect ? () => onSelect(it.id) : undefined}>
                {it.icon
                  ? <img className="op-nav-item__icon" src={it.icon} width="16" height="16" alt="" />
                  : <span className="op-nav-item__icon" />}
                <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{it.label}</span>
                {it.shortcut ? <span className="op-nav-item__shortcut">{it.shortcut}</span> : <span />}
              </button>
            ))}
          </div>
        </div>
      ))}
    </nav>
  );
}
