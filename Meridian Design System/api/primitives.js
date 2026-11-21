// Meridian public family barrel — primitives
// Low-level interaction and layout surfaces: buttons, inputs, form composition, overlays. No financial semantics, no domain knowledge.
//
// Meridian compiles to one namespaced runtime bundle, so a barrel here is a NAMED VIEW of that
// bundle, not a separate build. Load the bundle first, then this file:
//
//   <script src="_ds_bundle.js"></script>
//   <script src="api/primitives.js"></script>
//   const { Accordion } = MeridianAPI.primitives;
//
// The family boundaries are the public contract; the physical folders under components/ are not.
// 4.0 turns these views into real subpath exports ("@meridian/workstation/primitives") — code written
// against MeridianAPI.primitives today keeps working through that change.
// Lazy by construction: the barrel defines getters that read the namespace at ACCESS time. It is
// safe to evaluate before the bundle has populated (which is what happens when the compiler folds
// this file into the bundle itself), and it never injects a script or logs at load.
(function(global){
  var NS = "MeridianDesignSystem_205d17";
  var NAMES = ["Accordion","AsyncCombobox","Badge","Breadcrumb","Button","Callout","Checkbox","Combobox","CommandPalette","ConnectionDot","ContextMenu","DatePicker","DateRangePicker","Delta","DETAIL_LEVELS","DetailDial","DetailOnly","Dialog","DialogBody","DialogFooter","DialogForm","DialogHeader","Disclosure","DisclosureHooks","DisclosureProvider","DisclosureRegion","DisclosureResidue","DOW","Drawer","DrawerBody","DrawerHeader","ErrorBoundary","Eyebrow","FieldInput","FileUpload","Flex","Form","Format","FormatHooks","FormatProvider","FormDivider","FormErrorSummary","FormField","FormGrid","FormHooks","FormRow","FormSectionLabel","FormValidation","FreshnessIndicator","GLYPHS","Grid","HotkeysProvider","Input","InstrumentChip","Kbd","LocalDate","LocalDateText","MultiSelect","NEGATIVE_STYLES","NotificationCenter","NumberInput","PanelSurface","Popover","ProgressBar","RadioGroup","SegmentedControl","Select","Slider","Spinner","SplitPane","Stack","StatusBanner","Stepper","TabPanel","Tabs","TagInput","TextArea","Timestamp","Toast","ToastProvider","Toggle","Tooltip","TreeView","Validators"];
  var root = global.MeridianAPI = global.MeridianAPI || {};
  var api = root.primitives || {};
  function ns(){ return global[NS] || {}; }
  for (var i = 0; i < NAMES.length; i++) (function(n){
    if (Object.prototype.hasOwnProperty.call(api, n)) return;
    Object.defineProperty(api, n, { enumerable: true, configurable: true, get: function(){ return ns()[n]; } });
  })(NAMES[i]);
  api.$family = "primitives";
  api.$names = NAMES;
  api.$missing = function(){ var m = [], o = ns(); for (var i = 0; i < NAMES.length; i++) if (!(NAMES[i] in o)) m.push(NAMES[i]); return m; };
  root.primitives = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
