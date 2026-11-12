// Meridian public family barrel — accounting
// Ledger, basis, period, posting, tie-out and rollforward surfaces — the accounting domain.
//
// Meridian compiles to one namespaced runtime bundle, so a barrel here is a NAMED VIEW of that
// bundle, not a separate build. Load the bundle first, then this file:
//
//   <script src="_ds_bundle.js"></script>
//   <script src="api/accounting.js"></script>
//   const { AccountTree } = MeridianAPI.accounting;
//
// The family boundaries are the public contract; the physical folders under components/ are not.
// 4.0 turns these views into real subpath exports ("@meridian/workstation/accounting") — code written
// against MeridianAPI.accounting today keeps working through that change.
// Lazy by construction: the barrel defines getters that read the namespace at ACCESS time. It is
// safe to evaluate before the bundle has populated (which is what happens when the compiler folds
// this file into the bundle itself), and it never injects a script or logs at load.
(function(global){
  var NS = "MeridianDesignSystem_205d17";
  var NAMES = ["AccountingPeriodSelector","AccountTree","AgingTable","AllocationEditor","AmountCell","Assurance","ASSURANCE_STAGES","AssuranceChip","CertificationMatrix","BasisBridge","BasisComparisonMatrix","CloseCalendar","CommitmentBar","FxRevaluationTable","IncomeSchedule","JournalEntryForm","LedgerTable","LifecycleLanes","LifecycleRibbon","Money","PostingImpactPreview","ReconciliationPanel","RollforwardMatrix","RollforwardTrace","ScheduleLadder","ScheduleLadderSet","StatementTable","TaxLotTable","TieOutPanel","TrialBalance","VarianceCell"];
  var root = global.MeridianAPI = global.MeridianAPI || {};
  var api = root.accounting || {};
  function ns(){ return global[NS] || {}; }
  for (var i = 0; i < NAMES.length; i++) (function(n){
    if (Object.prototype.hasOwnProperty.call(api, n)) return;
    Object.defineProperty(api, n, { enumerable: true, configurable: true, get: function(){ return ns()[n]; } });
  })(NAMES[i]);
  api.$family = "accounting";
  api.$names = NAMES;
  api.$missing = function(){ var m = [], o = ns(); for (var i = 0; i < NAMES.length; i++) if (!(NAMES[i] in o)) m.push(NAMES[i]); return m; };
  root.accounting = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
