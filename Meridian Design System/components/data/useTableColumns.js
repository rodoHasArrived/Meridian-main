// Meridian useTableColumns — column model for DenseDataTable: resize, reorder, pin, hide.
// Keeps the presentational table dumb; this hook owns the mutable column geometry and
// persists it per-user under a key. Returns the ordered, visible, sized column list plus
// the handlers a header row wires up.
//
//   const cols = useTableColumns(baseColumns, { persistKey: "blotter.cols" });
//   <DenseDataTable columns={cols.visibleColumns}
//     onColumnResize={cols.resize} onColumnReorder={cols.reorder}
//     onColumnPin={cols.togglePin} pinnedKeys={cols.pinnedKeys} />
import React from "react";

export function useTableColumns(baseColumns, { persistKey = null, defaultWidth = 140 } = {}) {
  const baseKeys = baseColumns.map((c) => c.key).join("|");
  const initial = React.useMemo(() => {
    const model = {
      order: baseColumns.map((c) => c.key),
      widths: Object.fromEntries(baseColumns.map((c) => [c.key, c.width || defaultWidth])),
      hidden: {},
      pinned: baseColumns.filter((c) => c.pinned).map((c) => c.key),
    };
    if (persistKey && typeof localStorage !== "undefined") {
      try {
        const saved = JSON.parse(localStorage.getItem(persistKey));
        // Only adopt saved state whose columns still match the current schema.
        if (saved && [...saved.order].sort().join("|") === [...model.order].sort().join("|")) {
          return { ...model, ...saved };
        }
      } catch (e) { /* ignore */ }
    }
    return model;
  }, [baseKeys]);

  const [model, setModel] = React.useState(initial);
  React.useEffect(() => setModel(initial), [initial]);
  React.useEffect(() => {
    if (persistKey && typeof localStorage !== "undefined") {
      localStorage.setItem(persistKey, JSON.stringify(model));
    }
  }, [model, persistKey]);

  const byKey = React.useMemo(() => Object.fromEntries(baseColumns.map((c) => [c.key, c])), [baseKeys]);

  const resize = React.useCallback((key, width) => {
    setModel((m) => ({ ...m, widths: { ...m.widths, [key]: Math.max(48, Math.round(width)) } }));
  }, []);

  const reorder = React.useCallback((fromKey, toKey) => {
    setModel((m) => {
      const order = m.order.filter((k) => k !== fromKey);
      const idx = order.indexOf(toKey);
      order.splice(idx < 0 ? order.length : idx, 0, fromKey);
      return { ...m, order };
    });
  }, []);

  // Keyboard-friendly relative move: shift a column one slot earlier/later in the order.
  // dir: -1 (up/left) | +1 (down/right). No-op at the ends.
  const move = React.useCallback((key, dir) => {
    setModel((m) => {
      const order = [...m.order];
      const from = order.indexOf(key);
      const to = from + dir;
      if (from < 0 || to < 0 || to >= order.length) return m;
      order.splice(to, 0, order.splice(from, 1)[0]);
      return { ...m, order };
    });
  }, []);

  const togglePin = React.useCallback((key) => {
    setModel((m) => ({
      ...m,
      pinned: m.pinned.includes(key) ? m.pinned.filter((k) => k !== key) : [...m.pinned, key],
    }));
  }, []);

  const toggleVisible = React.useCallback((key) => {
    setModel((m) => ({ ...m, hidden: { ...m.hidden, [key]: !m.hidden[key] } }));
  }, []);

  const reset = React.useCallback(() => setModel(initial), [initial]);

  // Pinned columns sort to the front, preserving their relative order.
  const visibleColumns = React.useMemo(() => {
    const pinnedSet = new Set(model.pinned);
    const ordered = model.order
      .filter((k) => byKey[k] && !model.hidden[k])
      .sort((a, b) => (pinnedSet.has(b) ? 1 : 0) - (pinnedSet.has(a) ? 1 : 0));
    let left = 0;
    return ordered.map((k) => {
      const pinned = pinnedSet.has(k);
      const col = { ...byKey[k], width: model.widths[k], pinned, pinnedOffset: pinned ? left : undefined };
      if (pinned) left += model.widths[k];
      return col;
    });
  }, [model, byKey]);

  return {
    visibleColumns,
    order: model.order,
    widths: model.widths,
    hidden: model.hidden,
    pinnedKeys: model.pinned,
    resize,
    reorder,
    move,
    togglePin,
    toggleVisible,
    reset,
    allColumns: baseColumns,
  };
}
