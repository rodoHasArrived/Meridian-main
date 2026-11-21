# Meridian public API families

The public families describe the contracts consumers write against; folders under
`components/` are implementation details. `legacy` is a compatibility view, not an
additional owner of exports. Counts and membership below are generated from the
same record as `families.json`; the release gate checks both for drift.

Load the consuming application's React peer before the bundle, then the families a page needs:

```html
<script src="../_ds_bundle.js"></script>
<script src="../api/coordination.js"></script>
<script src="../api/decision.js"></script>
<script>
  const { WorkspaceContextProvider, TimeContextProvider } = MeridianAPI.coordination;
  const { DecisionFlowProvider, ActionGuardList } = MeridianAPI.decision;
</script>
```

Every current public name belongs to one family, and `legacy` views the same flat
namespace. It does not restore symbols removed in earlier major releases; consult
`../docs/UPGRADING.md` before migrating an older consumer.

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

<!-- families:generated:begin -->
**351 exports in 9 public families**, plus the `legacy` compatibility view of the same namespace. **28 exports** are release-candidate maturity; per-export stability is recorded in `families.json`.

| Family | Exports | Owns |
| --- | ---: | --- |
| `primitives` | 84 | Low-level interaction and layout surfaces: buttons, inputs, form composition, and overlays |
| `coordination` | 41 | The operating-language spine — workspace context, time context, object identity, linked selection, continuity, and composition |
| `data` | 40 | Tabular and set-oriented surfaces: DataGrid, filters, selection toolbars, bulk action, data-state switching, saved views, optimistic editing, and selection comparison |
| `analytics` | 49 | Analytical figures and geometry: the figure contract, plate grid, comparison and domain visualizations, annotations, and cursor coordination |
| `evidence` | 49 | Why the selected value or object is in this state: sources, coverage, provenance, calculation derivation, value authority, materiality, condition, severity, and operator annotations |
| `decision` | 33 | What changes if a proposed action occurs, and whether it may proceed: decision flow, guards, change review, version conflict, resolution, attestation, and instruction state |
| `accounting` | 31 | Ledger, basis, period, posting, assurance, tie-out, and rollforward surfaces — the accounting domain |
| `trading` | 6 | Order, blotter, depth, and tape surfaces — the trading domain |
| `shell` | 18 | Workstation chrome: navigation, topbar, status bar, session and role surfaces, global search, and workspace layout |
| `legacy` | 351 (view) | Compatibility view; no additional owned exports |

## Family membership

### primitives

`Accordion` · `AsyncCombobox` · `Badge` · `Breadcrumb` · `Button` · `Callout` · `Checkbox` · `Combobox` · `CommandPalette` · `ConnectionDot` · `ContextMenu` · `DETAIL_LEVELS` · `DOW` · `DatePicker` · `DateRangePicker` · `Delta` · `DetailDial` · `DetailOnly` · `Dialog` · `DialogBody` · `DialogFooter` · `DialogForm` · `DialogHeader` · `Disclosure` · `DisclosureHooks` · `DisclosureProvider` · `DisclosureRegion` · `DisclosureResidue` · `Drawer` · `DrawerBody` · `DrawerHeader` · `ErrorBoundary` · `Eyebrow` · `FieldInput` · `FileUpload` · `Flex` · `Form` · `FormDivider` · `FormErrorSummary` · `FormField` · `FormGrid` · `FormHooks` · `FormRow` · `FormSectionLabel` · `FormValidation` · `Format` · `FormatHooks` · `FormatProvider` · `FreshnessIndicator` · `GLYPHS` · `Grid` · `HotkeysProvider` · `Input` · `InstrumentChip` · `Kbd` · `LocalDate` · `LocalDateText` · `MultiSelect` · `NEGATIVE_STYLES` · `NotificationCenter` · `NumberInput` · `PanelSurface` · `Popover` · `ProgressBar` · `RadioGroup` · `SegmentedControl` · `Select` · `Slider` · `Spinner` · `SplitPane` · `Stack` · `StatusBanner` · `Stepper` · `TabPanel` · `Tabs` · `TagInput` · `TextArea` · `Timestamp` · `Toast` · `ToastProvider` · `Toggle` · `Tooltip` · `TreeView` · `Validators`

### coordination

`ANALYTICAL_SCOPES` · `Capsule` · `CapsuleBody` · `CapsuleFooter` · `CapsuleHeader` · `ContinuityFrame` · `ContinuityStatus` · `Datum` · `DatumGroup` · `DensityToggle` · `HandoverBrief` · `HandoverBriefChip` · `LinkedSelectionProvider` · `LinkedSelectionTarget` · `OBJECT_TYPES` · `ObjectHeader` · `ObjectHooks` · `ObjectModel` · `ObjectScope` · `ObjectScopeProvider` · `ObjectTrail` · `ObjectTrailList` · `ObjectTrailProvider` · `STRATEGY_FORMATION_STEPS` · `STRATEGY_LIFECYCLE` · `ScopeHooks` · `ScopeMark` · `ScopeMismatch` · `ScopedValue` · `SelectionHooks` · `StrategyModel` · `TIME_ROLES` · `TimeContext` · `TimeContextPanel` · `TimeContextProvider` · `TimeHooks` · `TimeRole` · `TrailHooks` · `WorkspaceContextBar` · `WorkspaceContextProvider` · `WorkspaceHooks`

### data

`BulkActionBar` · `BulkSelectCheckbox` · `CELL_FORMATS` · `ColumnChooser` · `ColumnManager` · `CompareSheet` · `CompareToggle` · `CompareTray` · `DENSITY` · `DataGrid` · `DataStateSwitch` · `EditableCell` · `EmptyState` · `EntitySummary` · `FilterBuilder` · `GRID_SCHEMAS` · `KeyValueGrid` · `MetricCard` · `OptimisticCell` · `OptimisticHooks` · `OptimisticProvider` · `OptimisticRowBar` · `Pagination` · `Registry` · `RegistryTable` · `SEVERITY` · `SavedViews` · `SelectionCompare` · `SelectionCompareHooks` · `SelectionCompareProvider` · `SelectionToolbar` · `Skeleton` · `SkeletonTable` · `TableHooks` · `Toolbar` · `ToolbarDivider` · `ToolbarGroup` · `ToolbarSpacer` · `VirtualizedList` · `WorksheetGrid`

### analytics

`AXIS_FONT` · `AnalyticalField` · `AnalyticalFigure` · `AnalyticalFigureGrid` · `AsOfControl` · `AxisChip` · `BarChart` · `CandleChart` · `CashFlowLadder` · `ChartAnnotations` · `ChartCard` · `ChartCursor` · `ChartCursorSync` · `ChartFrame` · `ChartTooltip` · `ComparisonPlate` · `ControlDependencyGraph` · `CorrelationHeatmap` · `DepthChart` · `DistributionRange` · `DonutChart` · `DotMatrix` · `DrawdownChart` · `DurationContribution` · `EquityCurve` · `FIGURE_ROLES` · `Figure` · `FigureSet` · `Fmt` · `GEOMETRIES` · `Histogram` · `IntervalBar` · `MATURITY_BUCKETS` · `MaturityLadder` · `OwnershipGraph` · `PeerComparisonStrip` · `PlateGrid` · `PnLCalendar` · `ROLES` · `ScatterChart` · `ScenarioSelector` · `SmallMultiples` · `Sparkline` · `TimeframeSwitcher` · `TraceChart` · `Treemap` · `WatchlistStrip` · `WaterfallChart` · `YieldCurve`

### evidence

`ADAPTER_POSTURES` · `AUTHORITIES` · `AdapterDiagnostics` · `AdapterHooks` · `AdapterPostureChip` · `AnnotationAnchor` · `AnnotationHooks` · `AnnotationLayer` · `AnnotationPanel` · `AnnotationProvider` · `AuthorityChip` · `AuthorityPanel` · `AuthorityStack` · `CalculationTrace` · `ConnectionState` · `CoverageMatrix` · `DataCondition` · `DataConditionLegend` · `DataConditionValue` · `DiffView` · `ENVELOPE_MARKS` · `EVIDENCE_SECTIONS` · `EnvelopeCoherence` · `EnvelopeMark` · `EnvelopePanel` · `EventTimeline` · `EvidenceInspector` · `EvidenceLink` · `EvidencePreview` · `EvidenceRail` · `EvidenceSection` · `GateRail` · `LineageGraph` · `LogTail` · `MaterialityAssessment` · `MaterialityBadge` · `ProvenanceChip` · `ProvenanceTrace` · `ReadinessPanel` · `STRATEGY_RUN_AXES` · `SeverityBadge` · `SlaChip` · `SourceCoverageSummary` · `Status` · `StrategyRunModel` · `TrustStrip` · `ValidationIssueList` · `ValueAuthority` · `ValueEnvelope`

### decision

`ACTION_STATES` · `ActionAvailability` · `ActionEnvelope` · `ActionEnvelopeProvider` · `ActionGuard` · `ActionGuardList` · `ActionHooks` · `ActionQueue` · `ActionQueueChip` · `ActionStateChip` · `AttestationPanel` · `CONSEQUENCE_KINDS` · `CaseQueue` · `ChangeReviewBar` · `ChangeSetDiff` · `ChangeSetHooks` · `ChangeSetProvider` · `ChangeSetReview` · `ClaimHeader` · `ConsequenceRail` · `DecisionConsequenceBar` · `DecisionFlow` · `DecisionFlowProvider` · `DecisionFlowRail` · `DecisionFlowStage` · `DecisionSnapshot` · `DecisionSnapshotPanel` · `InstructionLedger` · `QUEUE_RUNGS` · `ResolutionDrawer` · `SnapshotHooks` · `VersionConflictNotice` · `WorkspaceSection`

### accounting

`ASSURANCE_STAGES` · `AccountTree` · `AccountingPeriodSelector` · `AgingTable` · `AllocationEditor` · `AmountCell` · `Assurance` · `AssuranceChip` · `BasisBridge` · `BasisComparisonMatrix` · `CertificationMatrix` · `CloseCalendar` · `CommitmentBar` · `FxRevaluationTable` · `IncomeSchedule` · `JournalEntryForm` · `LedgerTable` · `LifecycleLanes` · `LifecycleRibbon` · `Money` · `PostingImpactPreview` · `ReconciliationPanel` · `RollforwardMatrix` · `RollforwardTrace` · `ScheduleLadder` · `ScheduleLadderSet` · `StatementTable` · `TaxLotTable` · `TieOutPanel` · `TrialBalance` · `VarianceCell`

### trading

`Blotter` · `DepthLadder` · `FillsFeed` · `OptionChainTable` · `OrderTicket` · `TimeAndSalesTape`

### shell

`ARCHETYPES` · `GlobalSearch` · `LayoutHooks` · `LayoutRegion` · `NavRail` · `ReadOnlyBanner` · `ReturnPath` · `RoleBadge` · `ScreenComposition` · `SessionControls` · `StatusBar` · `UserMenu` · `WorkspaceLayout` · `WorkspaceLayoutBar` · `WorkspaceLayoutGrid` · `WorkspaceLayoutProvider` · `WorkstationShell` · `WorkstationTopbar`
<!-- families:generated:end -->
