// Meridian SessionControls — the session/permission primitives every deployment needs:
// UserMenu (initials chip + popover: identity, role, sign out), RoleBadge (small-caps
// permission chip), ReadOnlyBanner (persistent strip when the operator can't write).
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-usermenu{position:relative;display:inline-block;font-family:var(--font-body);}
.mds-usermenu__chip{appearance:none;cursor:pointer;width:28px;height:28px;border-radius:50%;
  border:1px solid var(--topbar-field-border,#38424B);background:var(--topbar-field-bg,#20272D);
  color:var(--topbar-text,#F1EFEA);font:600 10px var(--font-data);letter-spacing:.02em;
  display:inline-flex;align-items:center;justify-content:center;}
.mds-usermenu__chip:hover{border-color:var(--topbar-field-border-hover,#46515C);}
.mds-usermenu__chip:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-usermenu__chip--light{border-color:var(--border,#D2CDC3);background:var(--bg-medium,#EBE8E1);
  color:var(--text-primary,#20252B);}
.mds-usermenu__panel{position:absolute;right:0;top:calc(100% + 6px);min-width:220px;z-index:60;
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border-strong,#AAA49A);
  box-shadow:var(--shadow-menu);}
.mds-usermenu__id{padding:11px 14px;border-bottom:1px solid var(--border,#D2CDC3);
  display:flex;flex-direction:column;gap:3px;}
.mds-usermenu__name{font-size:13px;font-weight:600;color:var(--text-primary,#20252B);}
.mds-usermenu__sub{font-family:var(--font-data);font-size:11px;color:var(--text-muted,#606870);}
.mds-usermenu__item{display:block;width:100%;text-align:left;appearance:none;border:none;
  background:transparent;cursor:pointer;padding:8px 14px;font-family:var(--font-body);
  font-size:12px;color:var(--text-secondary,#535C65);}
.mds-usermenu__item:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B);}
.mds-usermenu__item:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-usermenu__item--danger{color:var(--red-dim,#883832);}
.mds-usermenu__item--danger:hover{background:var(--red-a10,rgba(181,75,66,.10));color:var(--red-dim,#883832);}
.mds-usermenu__sep{height:1px;background:var(--border-divider,#E2DED5);margin:4px 0;}
.mds-rolebadge{display:inline-flex;align-items:center;gap:5px;padding:2px 8px;
  border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.04em;background:var(--bg-medium,#EBE8E1);color:var(--text-secondary,#535C65);}
.mds-rolebadge--admin{border-color:var(--purple,#2F648A);color:var(--purple-dim,#234B68);
  background:var(--purple-a10,rgba(47,100,138,.10));}
.mds-rolebadge--operator{border-color:var(--brand-signal,#C56343);color:var(--text-secondary,#535C65);
  background:var(--brand-signal-ghost,#F6E6DF);}
.mds-rolebadge--viewer{border-color:var(--border-strong,#AAA49A);}
.mds-readonly{display:flex;align-items:center;gap:10px;padding:7px 14px;
  background:var(--orange-a10,rgba(152,103,23,.10));border:1px solid var(--orange,#986717);
  border-left-width:1px;font-family:var(--font-body);font-size:12px;color:var(--orange-dim,#724D11);}
.mds-readonly__tag{font-family:var(--font-data);font-size:9px;font-weight:700;letter-spacing:.06em;
  text-transform:uppercase;padding:2px 6px;border:1px solid var(--orange,#986717);
  border-radius:var(--radius-chip,2px);flex:none;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "sessioncontrols");
  el.textContent = css;
  document.head.appendChild(el);
}

function initials(name) {
  return String(name || "?").split(/\s+/).map((w) => w[0]).filter(Boolean).slice(0, 2).join("").toUpperCase();
}

export function UserMenu({ name = "", role = "", detail = "", items = [], onSignOut, onChrome = true }) {
  inject();
  const [open, setOpen] = React.useState(false);
  const rootRef = React.useRef(null);
  const buttonRef = React.useRef(null);
  const closeMenu = (refocus = true) => { setOpen(false); if (refocus) buttonRef.current?.focus(); };
  React.useEffect(() => { if (open) rootRef.current?.querySelector('[role="menuitem"]')?.focus(); }, [open]);
  React.useEffect(() => {
    if (!open) return;
    const onDown = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === "Escape" && rootRef.current?.contains(e.target)) { e.stopPropagation(); closeMenu(); } };
    const onFocus = (e) => { if (!rootRef.current?.contains(e.target)) closeMenu(false); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    document.addEventListener("focusin", onFocus);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("focusin", onFocus);
    };
  }, [open]);
  return (
    <div className="mds-usermenu" ref={rootRef}>
      <button ref={buttonRef} type="button" aria-haspopup="menu" aria-expanded={open} aria-label={`${initials(name)} — Account: ${name}`}
        className={`mds-usermenu__chip${onChrome ? "" : " mds-usermenu__chip--light"}`}
        onKeyDown={(e) => { if (e.key === "ArrowDown") { e.preventDefault(); setOpen(true); } }}
        onClick={() => setOpen((o) => !o)}>
        {initials(name)}
      </button>
      {open && (
        <div className="mds-usermenu__panel" role="menu" aria-label="Account actions" onKeyDown={(e) => {
          const items = Array.from(e.currentTarget.querySelectorAll('[role="menuitem"]')).filter((node) => !node.disabled);
          const index = items.indexOf(document.activeElement);
          let next;
          if (e.key === "ArrowDown") next = items[(index + 1) % items.length];
          else if (e.key === "ArrowUp") next = items[(index - 1 + items.length) % items.length];
          else if (e.key === "Home") next = items[0];
          else if (e.key === "End") next = items[items.length - 1];
          if (next) { e.preventDefault(); next.focus(); }
        }}>
          <div className="mds-usermenu__id">
            <span className="mds-usermenu__name">{name}</span>
            {(role || detail) && (
              <span className="mds-usermenu__sub">
                {role && <RoleBadge role={role} />} {detail}
              </span>
            )}
          </div>
          {items.map((it, i) => (
            <button key={i} type="button" role="menuitem"
              className={`mds-usermenu__item${it.danger ? " mds-usermenu__item--danger" : ""}`}
              onClick={() => { closeMenu(); it.onSelect && it.onSelect(); }}>
              {it.label}
            </button>
          ))}
          {onSignOut && (
            <React.Fragment>
              {items.length > 0 && <div className="mds-usermenu__sep" />}
              <button type="button" role="menuitem" className="mds-usermenu__item mds-usermenu__item--danger"
                onClick={() => { closeMenu(); onSignOut(); }}>
                Sign out
              </button>
            </React.Fragment>
          )}
        </div>
      )}
    </div>
  );
}

export function RoleBadge({ role = "viewer", label }) {
  inject();
  const r = String(role).toLowerCase();
  const known = ["admin", "operator", "viewer"].includes(r) ? r : "viewer";
  return (
    <span className={`mds-rolebadge mds-rolebadge--${known}`}>
      {label || role}
    </span>
  );
}

export function ReadOnlyBanner({ message = "You have view-only access. Changes are disabled for your role." }) {
  inject();
  return (
    <div className="mds-readonly" role="status">
      <span className="mds-readonly__tag">Read-only</span>
      <span>{message}</span>
    </div>
  );
}

// Named export matching the file so the bundler registers the module; the three primitives
// are also exported individually above.
export const SessionControls = { UserMenu, RoleBadge, ReadOnlyBanner };
