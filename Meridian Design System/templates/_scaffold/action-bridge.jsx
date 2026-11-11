// ActionBridge — hands the ActionEnvelopeProvider API (submit · retry · instructions) to a DC logic
// class. Mount INSIDE the provider: <x-import component="ActionBridge" from="../_scaffold/action-bridge.jsx" on-api="{{ onActionApi }}" hint-size="0px,0px"></x-import>
function ActionBridge({ onApi }) {
  const DS = window.MeridianDesignSystem_205d17 || {};
  const use = DS.ActionHooks && DS.ActionHooks.useAction;
  let api = null;
  try { api = use ? use() : null; } catch (e) { api = null; }
  // The parent re-renders on every onApi call and hands back fresh callbacks/declarations, which
  // rebuild the provider's API — notifying on API identity looped forever. Hand the parent ONE stable
  // proxy that always reads the latest API, and notify only when the instruction list changes.
  const apiRef = React.useRef(api);
  apiRef.current = api;
  const onApiRef = React.useRef(onApi);
  onApiRef.current = onApi;
  const proxy = React.useMemo(() => ({
    submit: (...a) => apiRef.current.submit(...a),
    retry: (...a) => apiRef.current.retry(...a),
    instructionFor: (...a) => apiRef.current.instructionFor(...a),
    get instructions() { return apiRef.current ? apiRef.current.instructions : []; },
    get unresolved() { return apiRef.current ? apiRef.current.unresolved : []; },
  }), []);
  const instructions = api ? api.instructions : null;
  React.useEffect(() => {
    if (apiRef.current && onApiRef.current) onApiRef.current(proxy);
  }, [instructions, proxy]);
  return null;
}
window.ActionBridge = ActionBridge;
