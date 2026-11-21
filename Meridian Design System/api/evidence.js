// Meridian public family barrel — evidence
// Why the selected value or object is in this state: sources, coverage, provenance, calculation derivation, value authority, materiality, condition and severity.
//
// Meridian compiles to one namespaced runtime bundle, so a barrel here is a NAMED VIEW of that
// bundle, not a separate build. Load the bundle first, then this file:
//
//   <script src="_ds_bundle.js"></script>
//   <script src="api/evidence.js"></script>
//   const { AuthorityChip } = MeridianAPI.evidence;
//
// The family boundaries are the public contract; the physical folders under components/ are not.
// 4.0 turns these views into real subpath exports ("@meridian/workstation/evidence") — code written
// against MeridianAPI.evidence today keeps working through that change.
// Lazy by construction: the barrel defines getters that read the namespace at ACCESS time. It is
// safe to evaluate before the bundle has populated (which is what happens when the compiler folds
// this file into the bundle itself), and it never injects a script or logs at load.
(function(global){
  var NS = "MeridianDesignSystem_205d17";
  var NAMES = ["ADAPTER_POSTURES","AdapterDiagnostics","AdapterHooks","AdapterPostureChip","AUTHORITIES","AuthorityChip","AuthorityPanel","AuthorityStack","CalculationTrace","ConnectionState","CoverageMatrix","DataCondition","DataConditionLegend","DataConditionValue","DiffView","ENVELOPE_MARKS","EnvelopeCoherence","EnvelopeMark","EnvelopePanel","EventTimeline","EVIDENCE_SECTIONS","EvidenceInspector","EvidenceLink","EvidencePreview","EvidenceRail","EvidenceSection","GateRail","LineageGraph","LogTail","MaterialityAssessment","MaterialityBadge","ProvenanceChip","ProvenanceTrace","ReadinessPanel","SeverityBadge","SlaChip","SourceCoverageSummary","Status","STRATEGY_RUN_AXES","StrategyRunModel","TrustStrip","ValidationIssueList","ValueAuthority","ValueEnvelope","AnnotationLayer","AnnotationProvider","AnnotationAnchor","AnnotationPanel","AnnotationHooks"];
  var root = global.MeridianAPI = global.MeridianAPI || {};
  var api = root.evidence || {};
  function ns(){ return global[NS] || {}; }
  for (var i = 0; i < NAMES.length; i++) (function(n){
    if (Object.prototype.hasOwnProperty.call(api, n)) return;
    Object.defineProperty(api, n, { enumerable: true, configurable: true, get: function(){ return ns()[n]; } });
  })(NAMES[i]);
  api.$family = "evidence";
  api.$names = NAMES;
  api.$missing = function(){ var m = [], o = ns(); for (var i = 0; i < NAMES.length; i++) if (!(NAMES[i] in o)) m.push(NAMES[i]); return m; };
  root.evidence = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
