// Meridian linked selection — one committed entity shared by rows, marks, graphs, and inspectors.
// 3.2.0: named scopes (several linked groups per screen), multi-select, and selection history.
import React from "react";

const LinkedSelectionContext = React.createContext(null);
const ScopeRegistryContext = React.createContext(null);

function normalize(value, source = "programmatic") {
  if (value == null) return null;
  if (typeof value === "string" || typeof value === "number") return { id: String(value), value, source };
  const id = value.id ?? value.key ?? value.value;
  return { ...value, id: String(id), source: value.source || source };
}
function toList(value, source) {
  if (value == null) return [];
  return (Array.isArray(value) ? value : [value]).map((entry) => normalize(entry, source)).filter(Boolean);
}
function sameList(a, b) {
  return a.length === b.length && a.every((entry, i) => entry.id === b[i].id);
}
function readUrl(param, multiple) {
  if (!param || typeof window === "undefined") return null;
  const raw = new URLSearchParams(window.location.search).get(param);
  if (!raw) return null;
  const ids = multiple ? raw.split(",").filter(Boolean) : [raw];
  return ids.map((id) => ({ id, value: id, source: "url" }));
}
function readStorage(key) {
  if (!key || typeof localStorage === "undefined") return null;
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    return toList(parsed, "storage");
  } catch { return null; }
}

export function LinkedSelectionProvider({
  value, defaultValue = null, onChange, urlParam, storageKey, clearOnEscape = true,
  scope, multiple = false, max, items, historyLimit = 12, children,
}) {
  const controlled = value !== undefined;
  const [internal, setInternal] = React.useState(
    () => readUrl(urlParam, multiple) || readStorage(storageKey) || toList(defaultValue, "default")
  );
  const [history, setHistory] = React.useState([]);
  const selections = controlled ? toList(value, "controlled") : internal;
  const selectionsRef = React.useRef(selections);
  selectionsRef.current = selections;

  const persist = React.useCallback((list) => {
    if (storageKey && typeof localStorage !== "undefined") {
      try {
        if (list.length) localStorage.setItem(storageKey, JSON.stringify(multiple ? list : list[0]));
        else localStorage.removeItem(storageKey);
      } catch { /* storage unavailable */ }
    }
    if (urlParam && typeof window !== "undefined") {
      const url = new URL(window.location.href);
      if (list.length) url.searchParams.set(urlParam, list.map((entry) => entry.id).join(","));
      else url.searchParams.delete(urlParam);
      window.history.replaceState(null, "", url);
    }
  }, [multiple, storageKey, urlParam]);

  const commit = React.useCallback((next, meta = {}) => {
    const previous = selectionsRef.current;
    let list = toList(next, meta.source || "programmatic");
    if (!multiple) list = list.slice(0, 1);
    else if (typeof max === "number") list = list.slice(0, max);
    if (sameList(previous, list) && !meta.force) return;
    if (!meta.replaceHistory) {
      setHistory((entries) => [previous, ...entries].slice(0, Math.max(0, historyLimit)));
    }
    if (!controlled) setInternal(list);
    selectionsRef.current = list;
    persist(list);
    onChange?.(multiple ? list : (list[0] ?? null), { ...meta, selections: list });
  }, [controlled, historyLimit, max, multiple, onChange, persist]);

  const api = React.useMemo(() => {
    const idOf = (candidate) => {
      if (candidate == null) return null;
      if (typeof candidate === "object") return String(candidate.id ?? candidate.key ?? candidate.value);
      return String(candidate);
    };
    const ids = selections.map((entry) => entry.id);
    return {
      scope: scope ?? null,
      multiple,
      // 3.1.0 surface — unchanged for single-selection consumers.
      selection: selections[0] ?? null,
      selectedId: selections[0]?.id ?? null,
      select: (next, meta) => commit(next, meta),
      clear: (meta) => commit(null, meta),
      isSelected: (candidate) => ids.includes(idOf(candidate)),
      // 3.2.0 additions.
      selections,
      selectedIds: ids,
      count: selections.length,
      items: items ?? null,
      toggle: (candidate, meta = {}) => {
        const id = idOf(candidate);
        if (!multiple) return commit(ids.includes(id) ? null : candidate, meta);
        return commit(
          ids.includes(id) ? selections.filter((entry) => entry.id !== id) : [...selections, candidate],
          meta
        );
      },
      add: (candidate, meta) => (multiple ? commit([...selections, candidate], meta) : commit(candidate, meta)),
      remove: (candidate, meta) => commit(selections.filter((entry) => entry.id !== idOf(candidate)), meta),
      selectRange: (fromId, toId, meta = {}) => {
        const order = (items || []).map((entry) => idOf(entry));
        const a = order.indexOf(idOf(fromId)), b = order.indexOf(idOf(toId));
        if (a < 0 || b < 0) return commit(toId, meta);
        const span = items.slice(Math.min(a, b), Math.max(a, b) + 1);
        return commit(multiple ? span : span[span.length - 1], { ...meta, source: meta.source || "range" });
      },
      selectAll: (meta) => (items ? commit(items, { ...meta, source: "select-all" }) : undefined),
      history,
      canGoBack: history.length > 0,
      back: (meta = {}) => {
        if (!history.length) return;
        const [previous, ...rest] = history;
        setHistory(rest);
        commit(previous, { ...meta, source: meta.source || "history", replaceHistory: true });
      },
    };
  }, [commit, history, items, multiple, scope, selections]);

  React.useEffect(() => {
    if (!clearOnEscape || typeof document === "undefined") return undefined;
    const listener = (event) => { if (event.key === "Escape") commit(null, { source: "keyboard" }); };
    document.addEventListener("keydown", listener);
    return () => document.removeEventListener("keydown", listener);
  }, [clearOnEscape, commit]);

  const parentRegistry = React.useContext(ScopeRegistryContext);
  const registry = React.useMemo(() => {
    const map = new Map(parentRegistry || []);
    if (scope) map.set(scope, api);
    return map;
  }, [api, parentRegistry, scope]);

  return (
    <ScopeRegistryContext.Provider value={registry}>
      <LinkedSelectionContext.Provider value={api}>{children}</LinkedSelectionContext.Provider>
    </ScopeRegistryContext.Provider>
  );
}

/** No argument → the nearest provider. A scope name → that named provider, wherever it sits above. */
export function useLinkedSelection(scope) {
  const nearest = React.useContext(LinkedSelectionContext);
  const registry = React.useContext(ScopeRegistryContext);
  const api = scope ? registry?.get(scope) : nearest;
  if (!api) {
    throw new Error(
      scope
        ? `useLinkedSelection("${scope}"): no LinkedSelectionProvider with scope="${scope}" above this component`
        : "useLinkedSelection must be used inside LinkedSelectionProvider"
    );
  }
  return api;
}

/** Bundle carrier — the compiler only exposes capitalized exports, so consumers of
 *  `_ds_bundle.js` reach the hook through this object:
 *  `const { SelectionHooks } = window.<Namespace>; const { useLinkedSelection } = SelectionHooks;`
 *  Source-level consumers can keep importing `useLinkedSelection` directly. */
export const SelectionHooks = { useLinkedSelection };

export function LinkedSelectionTarget({
  selection, scope, index, as: Tag = "div", className = "", children,
  onClick, onKeyDown, tabIndex, ...rest
}) {
  const api = useLinkedSelection(scope);
  const selected = api.isSelected(selection);
  const activate = (event, source) => {
    const additive = event && (event.metaKey || event.ctrlKey);
    const extend = event && event.shiftKey;
    if (extend && api.items && api.selection) api.selectRange(api.selection, selection, { source });
    else if (additive && api.multiple) api.toggle(selection, { source });
    else api.select(selection, { source });
  };
  const handleKeyDown = (event) => {
    if (event.key === "Enter" || event.key === " ") { event.preventDefault(); activate(event, "keyboard"); }
    onKeyDown?.(event);
  };
  return (
    <Tag
      className={`${className}${selected ? " is-selected" : ""}`}
      aria-selected={selected}
      data-selected={selected ? "true" : undefined}
      data-index={index}
      tabIndex={tabIndex ?? 0}
      onClick={(event) => { activate(event, "target"); onClick?.(event); }}
      onKeyDown={handleKeyDown}
      {...rest}
    >{children}</Tag>
  );
}
