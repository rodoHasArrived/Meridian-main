// Meridian public family barrel — legacy
// Compatibility surface. The whole namespace as published before the family split — kept so existing
// consumers are never forced to migrate within the 3.x line.
//
//   MeridianAPI.legacy.VarianceCell
//
(function(global){
  var NS = "MeridianDesignSystem_205d17";
  var root = global.MeridianAPI = global.MeridianAPI || {};
  if (!Object.prototype.hasOwnProperty.call(root, "legacy")) {
    Object.defineProperty(root, "legacy", { enumerable: true, configurable: true, get: function(){ return global[NS] || {}; } });
  }
  if (typeof module !== "undefined" && module.exports) module.exports = root.legacy;
})(typeof window !== "undefined" ? window : globalThis);
