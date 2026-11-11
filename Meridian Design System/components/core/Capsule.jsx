// Meridian Capsule — a configurable workspace module with fixed anatomy and standard heights.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "capsule");
  el.textContent = `
.mds-capsule{--capsule-height:240px;display:flex;flex-direction:column;min-width:0;height:var(--capsule-height);background:var(--surface-plate,var(--bg-light,#F8F7F3));border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-card,4px);font-family:var(--font-body);color:var(--text-secondary,#535C65);overflow:hidden;position:relative;}
.mds-capsule--xs{--capsule-height:144px}.mds-capsule--sm{--capsule-height:240px}.mds-capsule--md{--capsule-height:336px}.mds-capsule--lg{--capsule-height:432px}
.mds-capsule--selected{box-shadow:inset 2px 0 0 var(--selected,#496C8F);background:var(--selected-surface,#E7EDF2);}
.mds-capsule--stale,.mds-capsule--partial,.mds-capsule--offline{border-style:dashed;}
.mds-capsule--error{border-color:var(--red,#B54B42);}
.mds-capsule__header{min-height:42px;display:flex;align-items:center;gap:10px;padding:0 12px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--surface-recessed,var(--bg-medium,#EBE8E1));}
.mds-capsule__identity{min-width:0;flex:1;display:grid;gap:1px;}
.mds-capsule__title{font-size:13px;font-weight:600;color:var(--text-primary,#20252B);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-capsule__context{font:10px var(--font-data,monospace);color:var(--text-muted,#606870);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-capsule__state{display:inline-flex;align-items:center;gap:5px;font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870);}
.mds-capsule__state::before{content:"";width:6px;height:6px;border-radius:50%;background:var(--neutral,#6D757D);}
.mds-capsule--ready .mds-capsule__state::before{background:var(--green,#2E7655)}.mds-capsule--partial .mds-capsule__state::before{background:var(--orange,#986717)}.mds-capsule--stale .mds-capsule__state::before,.mds-capsule--offline .mds-capsule__state::before{background:var(--pending,#7A6B52)}.mds-capsule--error .mds-capsule__state::before{background:var(--red,#B54B42)}
.mds-capsule__tools{display:flex;align-items:center;gap:4px;}
.mds-capsule__tool{width:26px;height:26px;border:1px solid transparent;background:transparent;color:var(--text-muted,#606870);font:600 11px var(--font-data,monospace);cursor:pointer;}
.mds-capsule__tool:hover{background:var(--bg-hover,#EDEFF0);border-color:var(--border,#D2CDC3);color:var(--text-primary,#20252B)}.mds-capsule__tool:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-capsule__body{flex:1;min-height:0;overflow:auto;padding:12px;}
.mds-capsule__footer{min-height:28px;display:flex;align-items:center;gap:8px;padding:0 12px;border-top:1px solid var(--border-divider,#E2DED5);font:10px var(--font-data,monospace);color:var(--text-muted,#606870);}
.mds-capsule.mds-capsule--collapsed{height:42px;min-height:42px}.mds-capsule--collapsed .mds-capsule__body,.mds-capsule--collapsed .mds-capsule__footer{display:none}
.mds-capsule__resize{position:absolute;right:3px;bottom:3px;width:12px;height:12px;color:var(--text-disabled,#A4ABB2);font-size:10px;line-height:12px;cursor:nwse-resize;}
`;
  document.head.appendChild(el);
}

export function CapsuleHeader({ title, context, state = "ready", tools, collapsible, collapsed, onToggle, movable }) {
  inject();
  return <header className="mds-capsule__header">
    {movable ? <span aria-hidden="true" title="Move module" style={{cursor:"grab",fontFamily:"var(--font-data)",color:"var(--text-disabled)"}}>⠿</span> : null}
    <div className="mds-capsule__identity"><span className="mds-capsule__title">{title}</span>{context ? <span className="mds-capsule__context">{context}</span> : null}</div>
    <span className="mds-capsule__state">{state}</span>
    {tools ? <div className="mds-capsule__tools">{tools}</div> : null}
    {collapsible ? <button type="button" className="mds-capsule__tool" onClick={onToggle} aria-expanded={!collapsed} aria-label={collapsed ? "Expand module" : "Collapse module"}>{collapsed ? "+" : "−"}</button> : null}
  </header>;
}
export function CapsuleBody({ className = "", children, ...rest }) { inject(); return <div className={`mds-capsule__body${className ? " "+className : ""}`} {...rest}>{children}</div>; }
export function CapsuleFooter({ className = "", children, ...rest }) { inject(); return <footer className={`mds-capsule__footer${className ? " "+className : ""}`} {...rest}>{children}</footer>; }

export function Capsule({
  title, context, state = "ready", size = "sm", height, selected = false,
  collapsible = false, defaultCollapsed = false, storageKey, movable = false,
  resizable = false, tools, footer, className = "", children, style, ...rest
}) {
  inject();
  const generatedId = React.useId();
  const key = storageKey ? `mds-capsule:${storageKey}` : null;
  const [collapsed, setCollapsed] = React.useState(() => {
    if (!key || typeof localStorage === "undefined") return defaultCollapsed;
    try { const saved = localStorage.getItem(key); return saved == null ? defaultCollapsed : saved === "1"; } catch { return defaultCollapsed; }
  });
  const toggle = () => setCollapsed((current) => {
    const next = !current;
    if (key && typeof localStorage !== "undefined") try { localStorage.setItem(key, next ? "1" : "0"); } catch {}
    return next;
  });
  const inline = height ? { "--capsule-height": typeof height === "number" ? `${height}px` : height, ...style } : style;
  return <section id={rest.id || generatedId} className={`mds-capsule mds-capsule--${size} mds-capsule--${state}${selected ? " mds-capsule--selected" : ""}${collapsed ? " mds-capsule--collapsed" : ""}${className ? " "+className : ""}`} style={inline} {...rest}>
    <CapsuleHeader title={title} context={context} state={state} tools={tools} collapsible={collapsible} collapsed={collapsed} onToggle={toggle} movable={movable}/>
    <CapsuleBody>{children}</CapsuleBody>
    {footer ? <CapsuleFooter>{footer}</CapsuleFooter> : null}
    {resizable && !collapsed ? <span className="mds-capsule__resize" aria-hidden="true">⌟</span> : null}
  </section>;
}
