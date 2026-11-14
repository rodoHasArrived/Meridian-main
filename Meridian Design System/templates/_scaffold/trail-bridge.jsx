// TrailBridge — hands the ObjectTrail context API to a DC logic class (which cannot call hooks).
// Mount INSIDE an ObjectTrailProvider: <x-import component="TrailBridge" from="../_scaffold/trail-bridge.jsx" on-api="{{ onTrailApi }}" hint-size="0px,0px"></x-import>
// Renders nothing. `onApi(api)` fires on mount and whenever the trail state changes.
function TrailBridge({ onApi }) {
  const DS = window.MeridianDesignSystem_205d17 || {};
  const use = DS.TrailHooks && DS.TrailHooks.useObjectTrail;
  let api = null;
  try { api = use ? use() : null; } catch (e) { api = null; }
  React.useEffect(() => { if (api && onApi) onApi(api); }, [api, onApi]);
  return null;
}
window.TrailBridge = TrailBridge;
