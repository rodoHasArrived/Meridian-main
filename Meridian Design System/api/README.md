# Meridian public API families

Ten families describe the public surface. They are the contract consumers write against; the
folders under `components/` are an implementation detail that 4.0 is free to rearrange.

Load the bundle, then the families a page needs:

```html
<script src="../_ds_bundle.js"></script>
<script src="../api/coordination.js"></script>
<script src="../api/decision.js"></script>
<script>
  const { WorkspaceContextProvider, TimeContextProvider } = MeridianAPI.coordination;
  const { DecisionFlowProvider, ActionGuardList } = MeridianAPI.decision;
</script>
```

| Family | Exports | Owns |
|---|---:|---|
| `primitives` | 69 | Low-level interaction and layout surfaces: buttons, inputs, form composition, overlays, glyphs |
| `coordination` | 34 | The operating-language spine — workspace context, time context, object identity, linked selection, continuity and composition |
| `data` | 30 | Tabular and set-oriented surfaces: the DataGrid table, filters, selection toolbars, bulk action, data-state switching, saved views |
| `analytics` | 41 | Analytical figures and their geometry: the figure contract, plate grid, comparison and domain visualizations, cursor coordination |
| `evidence` | 42 | Why the selected value or object is in this state: sources, coverage, provenance, calculation derivation, value authority, materiality, condition and severity |
| `decision` | 19 | What changes if a proposed action occurs, and whether it may proceed: decision flow, guards, change review, version conflict, resolution, attestation |
| `accounting` | 27 | Ledger, basis, period, posting, tie-out and rollforward surfaces — the accounting domain |
| `trading` | 6 | Order, blotter, depth and tape surfaces — the trading domain |
| `shell` | 16 | Workstation chrome: navigation rail, topbar, status bar, session and role surfaces |
| `legacy` | all | Compatibility surface |

Every name is reachable from one family, and `legacy` keeps the flat namespace working — nothing
in the 3.x line is forced to migrate.

Barrels are **lazy**: each defines getters that read the namespace at access time, so a barrel is
safe to evaluate before the bundle has populated, never injects a script, and never logs at load.
Because the compiler folds project `.js` into the bundle, `MeridianAPI` is in fact already present
on any page that loads `_ds_bundle.js` — the explicit `<script>` above is for pages that load a
barrel on its own. `MeridianAPI.<family>.$missing()` lists names the current bundle does not carry.

## Rules

- A name belongs to exactly one family. `owner` conflicts are a release gate failure.
- Families do not import each other at runtime; they are views of one bundle.
- `evidence` substantiates the current state. `decision` predicts and governs a proposed change.
  A component that does both belongs in the one whose question it answers first.
- `coordination` holds shared analytical identity — context, time, object, selection, continuity.
  A component that only *reads* context is not coordination.
- `primitives` carries no financial semantics. If it knows what a basis or a period is, it is not
  a primitive.

## Family membership

### primitives

Accordion · AsyncCombobox · Badge · Breadcrumb · Button · Callout · Checkbox · Combobox · CommandPalette · ConnectionDot · ContextMenu · DatePicker · DateRangePicker · Delta · Dialog · DialogBody · DialogFooter · DialogForm · DialogHeader · Drawer · DrawerBody · DrawerHeader · ErrorBoundary · Eyebrow · FieldInput · FileUpload · Flex · Form · FormDivider · FormErrorSummary · FormField · FormGrid · FormHooks · FormRow · FormSectionLabel · FormValidation · FreshnessIndicator · GlyphCheck · GlyphChevronDown · GlyphChevronRight · GlyphDash · GlyphInfo · GlyphWarn · GlyphX · Grid · HotkeysProvider · Input · InstrumentChip · Kbd · MultiSelect · NotificationCenter · NumberInput · PanelSurface · Popover · ProgressBar · RadioGroup · SegmentedControl · Select · SelectCheckbox · Slider · Spinner · SplitPane · Stack · StatusBanner · Stepper · TabPanel · Tabs · TagInput · TextArea · Timestamp · Toast · ToastProvider · Toggle · Tooltip · TreeView · Validators

### coordination

Capsule · CapsuleBody · CapsuleFooter · CapsuleHeader · ContinuityFrame · ContinuityStatus · Datum · DatumGroup · DensityToggle · LinkedSelectionProvider · LinkedSelectionTarget · OBJECT_TYPES · ObjectHeader · ObjectHooks · ObjectModel · STRATEGY_FORMATION_STEPS · STRATEGY_LIFECYCLE · SelectionHooks · StrategyModel · TIME_ROLES · TimeContext · TimeContextPanel · TimeContextProvider · TimeHooks · TimeRole · WorkspaceContextBar · WorkspaceContextProvider · WorkspaceHooks

### data

BulkActionBar · BulkSelectCheckbox · ColumnChooser · ColumnManager · DataStateSwitch · EditableCell · EmptyState · EntitySummary · FilterBuilder · KeyValueGrid · MetricCard · Pagination · SavedViews · SelectionToolbar · Skeleton · SkeletonTable · TableHooks · Toolbar · ToolbarDivider · ToolbarGroup · ToolbarSpacer · VirtualizedList · WorksheetGrid

### analytics

AXIS_FONT · AnalyticalField · AnalyticalFigure · AnalyticalFigureGrid · AsOfControl · AxisChip · BarChart · CandleChart · CashFlowLadder · ChartCard · ChartCursor · ChartCursorSync · ChartFrame · ChartTooltip · ComparisonPlate · ControlDependencyGraph · CorrelationHeatmap · DepthChart · DrawdownChart · DurationContribution · EquityCurve · Fmt · Histogram · IntervalBar · MATURITY_BUCKETS · MaturityLadder · OwnershipGraph · PlateGrid · PnLCalendar · ROLES · ScatterChart · Sparkline · TimeframeSwitcher · TraceChart · Treemap · WaterfallChart · YieldCurve

### evidence

AuthorityChip · AuthorityPanel · AuthorityStack · CalculationTrace · ConnectionState · CoverageMatrix · DataCondition · DataConditionLegend · DataConditionValue · DiffView · EventTimeline · EvidenceInspector · EvidenceLink · EvidencePreview · EvidenceSection · GateRail · LineageGraph · LogTail · MaterialityAssessment · MaterialityBadge · ProvenanceChip · ProvenanceTrace · ReadinessPanel · SeverityBadge · SlaChip · STRATEGY_RUN_AXES · SourceCoverageSummary · Status · StrategyRunModel · TrustStrip · ValidationIssueList · ValueAuthority

### decision

ActionAvailability · ActionGuard · ActionGuardList · AttestationPanel · CaseQueue · ChangeReviewBar · ChangeSetDiff · ChangeSetHooks · ChangeSetProvider · ChangeSetReview · ClaimHeader · DecisionConsequenceBar · DecisionFlow · DecisionFlowProvider · DecisionFlowRail · DecisionFlowStage · ResolutionDrawer · VersionConflictNotice · WorkspaceSection

### accounting

AccountTree · AccountingPeriodSelector · AgingTable · AllocationEditor · AmountCell · BasisBridge · BasisComparisonMatrix · CloseCalendar · CommitmentBar · FxRevaluationTable · IncomeSchedule · JournalEntryForm · LedgerTable · LifecycleLanes · LifecycleRibbon · Money · PostingImpactPreview · ReconciliationPanel · RollforwardMatrix · RollforwardTrace · StatementTable · TaxLotTable · TieOutPanel · TrialBalance · VarianceCell

### trading

Blotter · DepthLadder · FillsFeed · OptionChainTable · OrderTicket · TimeAndSalesTape

### shell

NavRail · ReadOnlyBanner · RoleBadge · SessionControls · StatusBar · UserMenu · WorkstationShell · WorkstationTopbar
