// Meridian useRowSelection — the selection model tables were missing: click, ctrl/cmd-toggle,
// and shift-click range select against the CURRENT row order, plus a select-across-pages
// contract (selectAllMatching / allMatchingSelected) so "select all 40k" doesn't mean
// "select the 50 loaded rows". Identity by a stable key, never row index.
//
//   const sel = useRowSelection(rows, (r) => r.id);
//   <tr onClick={(e) => sel.onRowClick(row, i, e)} aria-selected={sel.isSelected(row)}>
import React from "react";

export function useRowSelection(rows, getKey = (r) => r.id) {
  const [selected, setSelected] = React.useState(() => new Set());
  const [allMatching, setAllMatching] = React.useState(false); // "select all across pages" mode
  const anchorRef = React.useRef(null); // index of last single-click, for shift-range

  const keys = React.useMemo(() => rows.map(getKey), [rows, getKey]);

  const isSelected = React.useCallback(
    (row) => allMatching || selected.has(getKey(row)),
    [allMatching, selected, getKey]
  );

  const onRowClick = React.useCallback((row, index, e) => {
    const key = getKey(row);
    setAllMatching(false);
    setSelected((prev) => {
      const next = new Set(prev);
      if (e && e.shiftKey && anchorRef.current != null) {
        const [a, b] = [anchorRef.current, index].sort((x, y) => x - y);
        for (let i = a; i <= b; i++) next.add(keys[i]);
      } else if (e && (e.metaKey || e.ctrlKey)) {
        next.has(key) ? next.delete(key) : next.add(key);
        anchorRef.current = index;
      } else {
        // plain click: toggle-select just this row, reset anchor
        const wasOnly = next.size === 1 && next.has(key);
        next.clear();
        if (!wasOnly) next.add(key);
        anchorRef.current = index;
      }
      return next;
    });
  }, [getKey, keys]);

  const toggle = React.useCallback((row) => {
    const key = getKey(row);
    setAllMatching(false);
    setSelected((prev) => {
      const next = new Set(prev);
      next.has(key) ? next.delete(key) : next.add(key);
      return next;
    });
  }, [getKey]);

  const toggleAllLoaded = React.useCallback(() => {
    setAllMatching(false);
    setSelected((prev) => (prev.size === rows.length ? new Set() : new Set(keys)));
  }, [rows.length, keys]);

  const selectAllMatching = React.useCallback(() => { setAllMatching(true); setSelected(new Set(keys)); }, [keys]);
  const clear = React.useCallback(() => { setAllMatching(false); setSelected(new Set()); anchorRef.current = null; }, []);

  const count = allMatching ? "all" : selected.size;
  const allLoadedSelected = rows.length > 0 && selected.size === rows.length;
  const someSelected = selected.size > 0 && selected.size < rows.length;

  return {
    isSelected, onRowClick, toggle, toggleAllLoaded, selectAllMatching, clear,
    selectedKeys: selected, count, allMatching, allLoadedSelected, someSelected,
    selectedRows: allMatching ? rows : rows.filter((r) => selected.has(getKey(r))),
  };
}
