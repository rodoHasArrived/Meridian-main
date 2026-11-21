// Meridian operator layer — the keyboard & command layer every template mounts (docs/UX_DEPTH_4_1.md
// track D). Pure helpers, no UI: turn a screen's nav sections + actions + objects into the props the
// design system's CommandPalette and HotkeysProvider take. Load after ds-base.js:
//   <helmet><script src="./ds-base.js"></script><script src="../_scaffold/operator-layer.js"></script></helmet>
// Shipped with the scaffold; sync_template_scaffold.py copies nothing for it — templates reference it in place.
(() => {
  const isMac = () => typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent || "");
  const sessionStore = () => { try { return window.sessionStorage; } catch { return undefined; } };

  // "G D" on a NavRail item is a promise; this makes it a binding and a palette row at once.
  function navCommands(sections, go, activeId) {
    const cmds = [], keys = [];
    for (const sec of sections || []) for (const it of sec.items || []) {
      if (!it || it.id === activeId && !it.href) { /* still listed, so the palette says where you are */ }
      const sc = it.shortcut ? String(it.shortcut).trim() : null;
      cmds.push({ id: "go-" + it.id, group: "Go to", label: it.label, hint: it.id === activeId ? "here" : undefined,
        shortcut: sc ? sc.split(/\s+/) : undefined, keywords: [sec.label, it.id], run: () => go && go(it.id, it) });
      if (sc) keys.push({ keys: sc.toLowerCase(), label: "Go to " + it.label, group: "Go to", action: () => go && go(it.id, it) });
    }
    return { cmds, keys };
  }

  // Screen actions: [{ id, label, keys?, shortcut?, run, danger?, keywords? }] → palette rows + bindings.
  function actionCommands(actions) {
    const cmds = [], keys = [];
    for (const a of actions || []) {
      if (!a) continue;
      const sc = a.keys ? String(a.keys) : null;
      cmds.push({ id: "act-" + a.id, group: a.group || "Actions", label: a.label, danger: a.danger, keywords: a.keywords,
        shortcut: sc ? sc.split(/[\s+]+/).map((k) => k.length === 1 ? k.toUpperCase() : k[0].toUpperCase() + k.slice(1)) : a.shortcut, run: a.run });
      if (sc) keys.push({ keys: sc.toLowerCase(), label: a.label, group: a.group || "Actions", action: a.run });
    }
    return { cmds, keys };
  }

  // Object lookup: `objects` = [{ type, id, label, meta?, keywords?, open(obj), verbs? }] → a `resolve` fn.
  // Sync over the screen's own data; a real screen swaps in a fetch and keeps the row shape.
  function objectResolver(objects, defaultVerbs) {
    const list = objects || [];
    return (q) => {
      const s = q.toLowerCase();
      return list.filter((o) => [o.id, o.label, o.type, ...(o.keywords || [])].filter(Boolean).some((v) => String(v).toLowerCase().includes(s)))
        .slice(0, 8)
        .map((o) => ({ id: "obj-" + o.type + ":" + o.id, group: (o.typeLabel || o.type || "Objects").replace(/^\w/, (c) => c.toUpperCase()), label: o.label || o.id, meta: o.id,
          keywords: o.keywords, run: () => o.open && o.open(o),
          verbs: (o.verbs || defaultVerbs || []).map((v) => ({ id: v.id, label: v.label, run: () => v.run(o) })) }));
    };
  }

  // The layer: standard bindings every screen gets (Ctrl+K is the palette's own; "?" is the sheet's own).
  function build({ screenId, sections, activeId, go, actions, objects, verbs, extraKeys }) {
    const nav = navCommands(sections, go, activeId);
    const act = actionCommands(actions);
    const layer = {
      commands: [...act.cmds, ...nav.cmds],
      groupOrder: ["Recent", ...new Set(act.cmds.map((c) => c.group)), "Go to"],
      bindings: [...act.keys, ...nav.keys, ...(extraKeys || [])],
      resolve: objects ? objectResolver(objects, verbs) : undefined,
      scope: screenId || "global",
      screenId: screenId || "global",
      placeholder: objects ? "Search objects, commands…  (" + (isMac() ? "⌘K" : "Ctrl K") + ")" : "Search commands…",
    };
    // Publish the live layer so chrome the SYSTEM owns can reach the screen's own objects —
    // ds-base.js's GlobalSearch searches `current.resolve` with no per-screen wiring.
    window.MeridianOperatorLayer.current = layer;
    return layer;
  }

  window.MeridianOperatorLayer = { build, navCommands, actionCommands, objectResolver, sessionStore, isMac, current: null };
})();
