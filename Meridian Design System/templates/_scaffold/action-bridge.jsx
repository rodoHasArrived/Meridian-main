// ActionBridge — hands the ActionEnvelopeProvider API (submit · retry · instructions) to a DC logic
// class. Mount INSIDE the provider: <x-import component="ActionBridge" from="../_scaffold/action-bridge.jsx" on-api="{{ onActionApi }}" hint-size="0px,0px"></x-import>
function ActionBridge({ onApi }) {
  const DS = window.MeridianDesignSystem_205d17 || {};
  const use = DS.ActionHooks && DS.ActionHooks.useAction;
  let api = null;
  try { api = use ? use() : null; } catch (e) { api = null; }
  React.useEffect(() => { if (api && onApi) onApi(api); }, [api, onApi]);
  return null;
}
window.ActionBridge = ActionBridge;
