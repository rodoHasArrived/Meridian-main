// Meridian bulk action bar — floating toolbar for selected rows.
// Integrates checkbox select, context menu (right-click), and floating action bar.
// Actions: copy, delete, export, assign, tag, move, etc.

const BulkActionBar = ({ selectedCount = 0, onAction, actions = [] }) => {
  const css = `
.mds-bulk-bar{position:fixed;bottom:32px;left:50%;transform:translateX(-50%);
  background:var(--accent);color:var(--text-on-accent,#fff);border-radius:var(--radius-button,2px);padding:12px 16px;
  box-shadow:var(--shadow-menu);display:flex;align-items:center;gap:16px;
  z-index:1000;font-size:13px;animation:bulk-slide-up .2s ease;}
@keyframes bulk-slide-up{from{opacity:0;transform:translateX(-50%) translateY(12px);}}
.mds-bulk-bar-info{display:flex;align-items:center;gap:8px;}
.mds-bulk-bar-count{font-weight:600;}
.mds-bulk-bar-actions{display:flex;align-items:center;gap:8px;}
.mds-bulk-action-btn{appearance:none;border:1px solid color-mix(in srgb, currentColor 30%, transparent);
  background:color-mix(in srgb, currentColor 10%, transparent);
  color:var(--text-on-accent,#fff);cursor:pointer;font-family:var(--font-body);font-size:12px;padding:6px 12px;
  border-radius:var(--radius-chip,2px);transition:all .12s;}
.mds-bulk-action-btn:hover{background:color-mix(in srgb, currentColor 20%, transparent);border-color:color-mix(in srgb, currentColor 40%, transparent);}
.mds-bulk-action-btn:active{background:color-mix(in srgb, currentColor 30%, transparent);}
.mds-bulk-action-btn--danger{border-color:var(--red,#BA3F55);background:var(--red,#BA3F55);color:var(--text-on-accent,#fff);}
.mds-bulk-action-btn--danger:hover{background:var(--red-dim,#8C2F40);border-color:var(--red-dim,#8C2F40);}
`;
  if (!document.getElementById("mds-bulk-bar-css")) {
    const el = document.createElement("style");
    el.id = "mds-bulk-bar-css";
    el.textContent = css;
    document.head.appendChild(el);
  }

  if (selectedCount === 0) return null;

  return (
    <div className="mds-bulk-bar">
      <div className="mds-bulk-bar-info">
        <span className="mds-bulk-bar-count">{selectedCount}</span>
        <span>selected</span>
      </div>
      <div className="mds-bulk-bar-actions">
        {actions.map((action) => (
          <button key={action.id} className={`mds-bulk-action-btn${action.danger ? " mds-bulk-action-btn--danger" : ""}`}
            onClick={() => onAction && onAction(action.id)} title={action.title}>
            {action.label}
          </button>
        ))}
      </div>
    </div>
  );
};

// BulkSelectCheckbox — checkbox for row selection in tables
const BulkSelectCheckbox = ({ checked, onChange, indeterminate = false }) => {
  const ref = React.useRef(null);
  React.useEffect(() => {
    if (ref.current) ref.current.indeterminate = indeterminate;
  }, [indeterminate]);

  return (
    <input type="checkbox" ref={ref} checked={checked} onChange={onChange}
      style={{ width: 16, height: 16, cursor: "pointer", accentColor: "var(--accent)" }} />
  );
};

// ContextMenu — right-click menu for selected rows
const ContextMenu = ({ x, y, items, onClose }) => {
  const ref = React.useRef(null);

  React.useEffect(() => {
    const handleClick = (e) => {
      if (ref.current && !ref.current.contains(e.target)) onClose && onClose();
    };
    document.addEventListener("click", handleClick);
    return () => document.removeEventListener("click", handleClick);
  }, [onClose]);

  const css = `
.mds-context-menu{position:fixed;background:var(--bg);border:1px solid var(--border);
  border-radius:var(--radius-button,2px);box-shadow:var(--shadow-menu);z-index:1001;
  min-width:160px;padding:4px 0;}
.mds-context-item{appearance:none;border:none;background:transparent;cursor:pointer;
  width:100%;text-align:left;padding:8px 16px;font-family:var(--font-body);font-size:12px;
  color:var(--text-primary);transition:background .12s;}
.mds-context-item:hover{background:var(--bg-hover);}
.mds-context-item--danger{color:var(--red);}
.mds-context-item--danger:hover{background:var(--red-a10,rgba(186,63,85,.10));}
`;
  if (!document.getElementById("mds-context-menu-css")) {
    const el = document.createElement("style");
    el.id = "mds-context-menu-css";
    el.textContent = css;
    document.head.appendChild(el);
  }

  return (
    <div ref={ref} className="mds-context-menu" style={{ top: y, left: x }}>
      {items.map((item) => (
        <button key={item.id} className={`mds-context-item${item.danger ? " mds-context-item--danger" : ""}`}
          onClick={() => { item.action && item.action(); onClose && onClose(); }}>
          {item.label}
        </button>
      ))}
    </div>
  );
};

export { BulkActionBar, BulkSelectCheckbox };
