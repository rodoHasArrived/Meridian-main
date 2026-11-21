// Meridian public family barrel — coordination
// The operating-language spine — workspace context, time context, object identity, linked selection, continuity and composition. Owns shared analytical identity so no template invents its own.
//
// Meridian compiles to one namespaced runtime bundle, so a barrel here is a NAMED VIEW of that
// bundle, not a separate build. Load the bundle first, then this file:
//
//   <script src="_ds_bundle.js"></script>
//   <script src="api/coordination.js"></script>
//   const { Capsule } = MeridianAPI.coordination;
//
// The family boundaries are the public contract; the physical folders under components/ are not.
// 4.0 turns these views into real subpath exports ("@meridian/workstation/coordination") — code written
// against MeridianAPI.coordination today keeps working through that change.
// Lazy by construction: the barrel defines getters that read the namespace at ACCESS time. It is
// safe to evaluate before the bundle has populated (which is what happens when the compiler folds
// this file into the bundle itself), and it never injects a script or logs at load.
(function(global){
  var NS = "MeridianDesignSystem_205d17";
  var NAMES = ["ANALYTICAL_SCOPES","Capsule","CapsuleBody","CapsuleFooter","CapsuleHeader","ContinuityFrame","ContinuityStatus","Datum","DatumGroup","DensityToggle","HandoverBrief","HandoverBriefChip","LinkedSelectionProvider","LinkedSelectionTarget","OBJECT_TYPES","ObjectHeader","ObjectHooks","ObjectModel","ObjectScope","ObjectScopeProvider","ObjectTrail","ObjectTrailList","ObjectTrailProvider","STRATEGY_FORMATION_STEPS","STRATEGY_LIFECYCLE","ScopeHooks","ScopeMark","ScopeMismatch","ScopedValue","SelectionHooks","StrategyModel","TIME_ROLES","TimeContext","TimeContextPanel","TimeContextProvider","TimeHooks","TimeRole","TrailHooks","WorkspaceContextBar","WorkspaceContextProvider","WorkspaceHooks"];
  var root = global.MeridianAPI = global.MeridianAPI || {};
  var api = root.coordination || {};
  function ns(){ return global[NS] || {}; }
  for (var i = 0; i < NAMES.length; i++) (function(n){
    if (Object.prototype.hasOwnProperty.call(api, n)) return;
    Object.defineProperty(api, n, { enumerable: true, configurable: true, get: function(){ return ns()[n]; } });
  })(NAMES[i]);
  api.$family = "coordination";
  api.$names = NAMES;
  api.$missing = function(){ var m = [], o = ns(); for (var i = 0; i < NAMES.length; i++) if (!(NAMES[i] in o)) m.push(NAMES[i]); return m; };
  root.coordination = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
