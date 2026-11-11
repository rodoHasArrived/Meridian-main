// Meridian Hotkeys — chord + sequence keyboard bindings, and the cheat-sheet overlay that
// makes them discoverable. Makes the keyboard-first claim real: NavRail's "G D" hints and the
// topbar's Ctrl+K promise now have a binding surface.
//
// HotkeysProvider listens at document level. Binding syntax:
//   "ctrl+k"  — modifier chord (ctrl/alt/shift/meta + key)
//   "g d"     — two-key sequence (800ms window), ignored while typing in a field
//   "?"       — single key, ignored while typing in a field
// ShortcutSheet renders automatically inside the provider: "?" or ctrl+/ opens it.
import React from "react";
import { useOverlayFocus } from "./useOverlayFocus";
import { Kbd } from "./Kbd";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-hotkeys-sheet{position:fixed;inset:0;background:var(--scrim, rgba(17,23,29,.5));z-index:var(--z-hotkey,400);
  display:flex;align-items:flex-start;justify-content:center;padding-top:9vh;
  animation:mds-hotkeys-fade var(--motion-base,150ms) var(--ease-standard,ease-out);}
@keyframes mds-hotkeys-fade{from{opacity:0}to{opacity:1}}
.mds-hotkeys-panel{width:520px;max-width:92vw;max-height:76vh;overflow-y:auto;
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border-strong,#AAA49A);
  box-shadow:var(--shadow-menu);font-family:var(--font-body);}
.mds-hotkeys-hd{display:flex;align-items:center;justify-content:space-between;padding:12px 16px;
  border-bottom:1px solid var(--border,#D2CDC3);position:sticky;top:0;background:var(--bg-light,#F8F7F3);}
.mds-hotkeys-title{font-size:13px;font-weight:600;color:var(--text-primary,#20252B);}
.mds-hotkeys-x{appearance:none;border:none;background:transparent;cursor:pointer;font-size:16px;
  color:var(--text-muted,#606870);padding:2px 6px;}
.mds-hotkeys-x:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);}
.mds-hotkeys-group{padding:10px 16px 4px;font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.04em;color:var(--text-muted,#606870);}
.mds-hotkeys-row{display:flex;align-items:center;justify-content:space-between;gap:12px;
  padding:6px 16px;font-size:12px;color:var(--text-secondary,#535C65);}
.mds-hotkeys-row+.mds-hotkeys-row{border-top:1px solid var(--border-divider,#E2DED5);}
.mds-hotkeys-keys{display:flex;gap:4px;align-items:center;flex:none;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "hotkeys");
  el.textContent = css;
  document.head.appendChild(el);
}

function isTyping(target) {
  if (!target) return false;
  const tag = (target.tagName || "").toLowerCase();
  return tag === "input" || tag === "textarea" || tag === "select" || target.isContentEditable;
}

function parseBinding(keys) {
  const s = String(keys).trim().toLowerCase();
  if (s.includes("+")) {
    const parts = s.split("+").map((p) => p.trim());
    return { type: "chord", key: parts[parts.length - 1], mods: new Set(parts.slice(0, -1)) };
  }
  const seq = s.split(/\s+/);
  return seq.length > 1 ? { type: "sequence", seq } : { type: "single", key: s };
}

function eventKey(e) {
  return e.key.length === 1 ? e.key.toLowerCase() : e.key.toLowerCase();
}

export function HotkeysProvider({ bindings = [], sheet = true, sheetTitle = "Keyboard shortcuts" }) {
  inject();
  const [open, setOpen] = React.useState(false);
  const sheetRef = React.useRef(null);
  useOverlayFocus(sheetRef, sheet && open, ".mds-hotkeys-panel");
  const parsed = React.useMemo(
    () => bindings.map((b) => ({ ...b, parsed: parseBinding(b.keys) })),
    [bindings]
  );
  const pendingRef = React.useRef(null); // { key, at } for sequences

  React.useEffect(() => {
    const onKeyDown = (e) => {
      const k = eventKey(e);
      const typing = isTyping(e.target);
      const mods = new Set(
        ["ctrl", "alt", "shift", "meta"].filter((m) => e[m + "Key"])
      );

      // Sheet toggles: "?" or ctrl+/
      if (sheet && !typing && ((k === "?" && mods.size <= 1) || (k === "/" && mods.has("ctrl")))) {
        e.preventDefault();
        setOpen((o) => !o);
        return;
      }
      if (open && k === "escape") { setOpen(false); return; }

      for (const b of parsed) {
        const p = b.parsed;
        if (p.type === "chord") {
          if (k === p.key && p.mods.size === mods.size && [...p.mods].every((m) => mods.has(m))) {
            e.preventDefault();
            b.action && b.action();
            pendingRef.current = null;
            return;
          }
        } else if (!typing && mods.size === 0) {
          if (p.type === "single" && k === p.key) {
            e.preventDefault();
            b.action && b.action();
            return;
          }
          if (p.type === "sequence") {
            const pending = pendingRef.current;
            if (pending && pending.key === p.seq[0] && Date.now() - pending.at < 800 && k === p.seq[1]) {
              e.preventDefault();
              b.action && b.action();
              pendingRef.current = null;
              return;
            }
          }
        }
      }
      // Remember a possible sequence starter.
      if (!typing && mods.size === 0 && parsed.some((b) => b.parsed.type === "sequence" && b.parsed.seq[0] === k)) {
        pendingRef.current = { key: k, at: Date.now() };
      } else if (pendingRef.current && k !== pendingRef.current.key) {
        pendingRef.current = null;
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [parsed, open, sheet]);

  if (!sheet || !open) return null;
  const groups = [];
  for (const b of parsed) {
    const g = b.group || "General";
    let entry = groups.find((x) => x.name === g);
    if (!entry) { entry = { name: g, items: [] }; groups.push(entry); }
    entry.items.push(b);
  }
  return (
    <div ref={sheetRef} className="mds-hotkeys-sheet" onClick={() => setOpen(false)}>
      <div className="mds-hotkeys-panel" role="dialog" aria-modal="true" aria-label={sheetTitle} onClick={(e) => e.stopPropagation()}>
        <div className="mds-hotkeys-hd">
          <span className="mds-hotkeys-title">{sheetTitle}</span>
          <button type="button" className="mds-hotkeys-x" aria-label="Close" onClick={() => setOpen(false)}>×</button>
        </div>
        {groups.map((g) => (
          <React.Fragment key={g.name}>
            <div className="mds-hotkeys-group">{g.name}</div>
            {g.items.map((b, i) => (
              <div className="mds-hotkeys-row" key={i}>
                <span>{b.label}</span>
                <span className="mds-hotkeys-keys">
                  {String(b.keys).split(/(\s+|\+)/).filter((p) => p.trim() && p !== "+").map((p, j) => (
                    <Kbd key={j}>{p.length === 1 ? p.toUpperCase() : p[0].toUpperCase() + p.slice(1)}</Kbd>
                  ))}
                </span>
              </div>
            ))}
          </React.Fragment>
        ))}
        <div className="mds-hotkeys-row" style={{ borderTop: "1px solid var(--border, #D2CDC3)" }}>
          <span>Show this sheet</span>
          <span className="mds-hotkeys-keys"><Kbd>?</Kbd></span>
        </div>
      </div>
    </div>
  );
}
