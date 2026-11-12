// Formation — DecisionFlow bridge. Hands the flow context value out to the template logic class
// so the consequence bar and exception jumps can call flow.goTo without re-implementing step
// state. Renders nothing; the provider stays the single owner of step order and URL restoration.
function FlowBridge({ onFlow }) {
  const ns = window.MeridianDesignSystem_205d17 || {};
  const f = ns.DecisionFlow ? ns.DecisionFlow.use() : null;
  React.useEffect(() => { if (onFlow) onFlow(f); });
  return null;
}
window.FlowBridge = FlowBridge;
if (typeof module !== "undefined") module.exports = { FlowBridge };
