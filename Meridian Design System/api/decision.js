// Meridian public family barrel — decision
// What changes if a proposed action occurs, and whether it may proceed: decision flow, guards, change review, version conflict, resolution, attestation.
//
// Meridian compiles to one namespaced runtime bundle, so a barrel here is a NAMED VIEW of that
// bundle, not a separate build. Load the bundle first, then this file:
//
//   <script src="_ds_bundle.js"></script>
//   <script src="api/decision.js"></script>
//   const { ActionAvailability } = MeridianAPI.decision;
//
// The family boundaries are the public contract; the physical folders under components/ are not.
// 4.0 turns these views into real subpath exports ("@meridian/workstation/decision") — code written
// against MeridianAPI.decision today keeps working through that change.
// Lazy by construction: the barrel defines getters that read the namespace at ACCESS time. It is
// safe to evaluate before the bundle has populated (which is what happens when the compiler folds
// this file into the bundle itself), and it never injects a script or logs at load.
(function(global){
  var NS = "MeridianDesignSystem_205d17";
  var NAMES = ["ACTION_STATES","ActionAvailability","ActionEnvelope","ActionEnvelopeProvider","ActionGuard","ActionGuardList","ActionHooks","ActionStateChip","AttestationPanel","CaseQueue","ChangeReviewBar","ChangeSetDiff","ChangeSetHooks","ChangeSetProvider","ChangeSetReview","ClaimHeader","CONSEQUENCE_KINDS","ConsequenceRail","DecisionConsequenceBar","DecisionFlow","DecisionFlowProvider","DecisionFlowRail","DecisionFlowStage","DecisionSnapshot","DecisionSnapshotPanel","InstructionLedger","ResolutionDrawer","SnapshotHooks","VersionConflictNotice","WorkspaceSection","ActionQueue","ActionQueueChip","QUEUE_RUNGS"];
  var root = global.MeridianAPI = global.MeridianAPI || {};
  var api = root.decision || {};
  function ns(){ return global[NS] || {}; }
  for (var i = 0; i < NAMES.length; i++) (function(n){
    if (Object.prototype.hasOwnProperty.call(api, n)) return;
    Object.defineProperty(api, n, { enumerable: true, configurable: true, get: function(){ return ns()[n]; } });
  })(NAMES[i]);
  api.$family = "decision";
  api.$names = NAMES;
  api.$missing = function(){ var m = [], o = ns(); for (var i = 0; i < NAMES.length; i++) if (!(NAMES[i] in o)) m.push(NAMES[i]); return m; };
  root.decision = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
