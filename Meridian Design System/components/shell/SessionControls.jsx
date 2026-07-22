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
  border:1px solid var(--topbar-field-border,#2C323A);background:var(--topbar-field-bg,#0F1216);
  color:var(--topbar-text,#F4F6F8);font:600 10px var(--font-data);letter-spacing:.02em;
  display:inline-flex;align-items:center;justify-content:center;}
.mds-usermenu__chip:hover{border-color:var(--topbar-field-border-hover,#3A424B);}
.mds-usermenu__chip:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
.mds-usermenu__chip--light{border-color:var(--border,#CBD3DC);background:var(--bg-medium,#EBEFF4);
  color:var(--text-primary,#22272E);}
.mds-usermenu__panel{position:absolute;right:0;top:calc(100% + 6px);min-width:220px;z-index:60;
  background:var(--bg-light,#fff);border:1px solid var(--border-strong,#99A5B2);
  box-shadow:var(--shadow-menu);}
.mds-usermenu__id{padding:11px 14px;border-bottom:1px solid var(--border,#CBD3DC);
  display:flex;flex-direction:column;gap:3px;}
.mds-usermenu__name{font-size:13px;font-weight:600;color:var(--text-primary,#22272E);}
.mds-usermenu__sub{font-family:var(--font-data);font-size:11px;color:var(--text-muted,#59636F);}
.mds-usermenu__item{display:block;width:100%;text-align:left;appearance:none;border:none;
  background:transparent;cursor:pointer;padding:8px 14px;font-family:var(--font-body);
  font-size:12px;color:var(--text-secondary,#4D5967);}
.mds-usermenu__item:hover{background:var(--bg-hover,#EAEEF3);color:var(--text-primary,#22272E);}
.mds-usermenu__item:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:-2px;}
.mds-usermenu__item--danger{color:var(--red-dim,#8C2F40);}
.mds-usermenu__item--danger:hover{background:var(--red-a10,rgba(186,63,85,.10));color:var(--red-dim,#8C2F40);}
.mds-usermenu__sep{height:1px;background:var(--border-divider,#D2D9E2);margin:4px 0;}
.mds-rolebadge{display:inline-flex;align-items:center;gap:5px;padding:2px 8px;
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-chip,2px);
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.04em;background:var(--bg-medium,#EBEFF4);color:var(--text-secondary,#4D5967);}
.mds-rolebadge--admin{border-color:var(--purple,#6F5BA7);color:var(--purple-dim,#54448D);
  background:var(--purple-a10,rgba(111,91,167,.10));}
.mds-rolebadge--operator{border-color:var(--accent,#2F6F8F);color:var(--accent-dim,#255B75);
  background:var(--accent-ghost,#E1EAF2);}
.mds-rolebadge--viewer{border-color:var(--border-strong,#99A5B2);}
.mds-readonly{display:flex;align-items:center;gap:10px;padding:7px 14px;
  background:var(--orange-a10,rgba(138,82,14,.10));border:1px solid var(--orange,#8A520E);
  border-left-width:1px;font-family:var(--font-body);font-size:12px;color:var(--orange-dim,#67400B);}
.mds-readonly__tag{font-family:var(--font-data);font-size:9px;font-weight:700;letter-spacing:.06em;
  text-transform:uppercase;padding:2px 6px;border:1px solid var(--orange,#8A520E);
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
  React.useEffect(() => {
    if (!open) return;
    const onDown = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);
  return (
    <div className="mds-usermenu" ref={rootRef}>
      <button type="button" aria-haspopup="true" aria-expanded={open} aria-label={`Account: ${name}`}
        className={`mds-usermenu__chip${onChrome ? "" : " mds-usermenu__chip--light"}`}
        onClick={() => setOpen((o) => !o)}>
        {initials(name)}
      </button>
      {open && (
        <div className="mds-usermenu__panel" role="menu">
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
              onClick={() => { setOpen(false); it.onSelect && it.onSelect(); }}>
              {it.label}
            </button>
          ))}
          {onSignOut && (
            <React.Fragment>
              {items.length > 0 && <div className="mds-usermenu__sep" />}
              <button type="button" role="menuitem" className="mds-usermenu__item mds-usermenu__item--danger"
                onClick={() => { setOpen(false); onSignOut(); }}>
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
