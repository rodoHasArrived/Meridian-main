import { ArrowRightLeft, CircleDot, Database, Eye, FlaskConical, GitBranch, Layers, Network, PlayCircle, Save, Search, ShieldCheck, Sparkles, Workflow } from "lucide-react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { MetricSnapshotCard } from "@/components/meridian/metric-card";
import { EmptyState as ConcreteEmptyState } from "@/components/data/empty-state";
import { GateRail, SeverityBadge } from "@/components/operations";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import {
  useStrategyDesignerViewModel,
  type ParticipationSliceViewModel,
  type ParticipationViewModel,
  type PayoffChartViewModel,
  type StrategyBuilderCellDetailViewModel,
  type StrategyBuilderCellKind,
  type StrategyBuilderCellViewModel,
  type StrategyBuilderFieldCatalogItem,
  type StrategyBuilderTemplate,
  type StrategyBuilderTransitionViewModel,
  type StrategyBuilderWorkbenchViewModel,
  type StrategyDesignerViewModel,
  type StrategyLegDetailFieldViewModel
} from "@/screens/strategy-designer-screen.view-model";

function cellKindBadgeVariant(kind: StrategyBuilderCellKind): "default" | "outline" | "success" | "warning" | "danger" | "research" {
  switch (kind) {
    case "governance":       return "warning";
    case "code":             return "research";
    case "state":            return "default";
    case "concurrent":       return "success";
    case "universe-builder": return "success";
    case "trade":            return "danger";
    case "options-payoff":   return "warning";
    default:                 return "outline";
  }
}

function CellKindIcon({ kind }: { kind: StrategyBuilderCellKind }) {
  switch (kind) {
    case "state":            return <CircleDot className="h-3 w-3 text-muted-foreground" aria-hidden="true" />;
    case "concurrent":       return <Network className="h-3 w-3 text-muted-foreground" aria-hidden="true" />;
    case "universe-builder": return <Layers className="h-3 w-3 text-muted-foreground" aria-hidden="true" />;
    case "trade":            return <ArrowRightLeft className="h-3 w-3 text-muted-foreground" aria-hidden="true" />;
    default:                 return null;
  }
}

export function StrategyDesignerScreen() {
  const vm = useStrategyDesignerViewModel();

  return (
    <div className="space-y-6" data-testid="strategy-designer-screen">
      <WorkbenchHero vm={vm.workbench} />

      <div
        className="grid gap-4 xl:grid-cols-[280px_minmax(0,1fr)]"
        data-testid="strategy-designer-primary-workbench"
      >
        <FieldCatalogPanel vm={vm.workbench} />
        <CellCanvasPanel vm={vm.workbench} />
        <div className="xl:col-span-2">
          <InspectorPanel vm={vm.workbench} />
        </div>
      </div>

      <details className="rounded-lg border border-border/70 bg-secondary/15 px-4 py-3">
        <summary className="cursor-pointer font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
          Templates and backtest proof
        </summary>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          Load a governed starting point or inspect validation, trace, and endpoint proof after the active design is ready.
        </p>
        <div className="mt-4 grid gap-4 xl:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
          <TemplateGallery vm={vm.workbench} />
          <BacktestProofPanel vm={vm.workbench} />
        </div>
      </details>

      <details className="rounded-lg border border-border/70 bg-secondary/15 px-4 py-3">
        <summary className="cursor-pointer font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
          Transition and payoff analysis
        </summary>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          Review graph transitions and sampled payoff evidence without crowding the primary cell-authoring canvas.
        </p>
        <div className="mt-4 grid gap-4 xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
          <TransitionMap transitions={vm.workbench.transitions} />
          <OptionsPayoffPanel vm={vm} />
        </div>
      </details>
    </div>
  );
}

function WorkbenchHero({ vm }: { vm: StrategyBuilderWorkbenchViewModel }) {
  return (
    <section className="space-y-4" aria-labelledby="strategy-builder-title">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <div className="eyebrow-label">Strategy workspace</div>
          <h1 id="strategy-builder-title" className="flex items-center gap-2 text-2xl font-semibold tracking-normal text-foreground">
            <Workflow className="h-6 w-6 text-primary" aria-hidden="true" />
            Strategy Builder Workbench
          </h1>
          <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
            {vm.document.name} · {vm.document.datasetReference} · {vm.document.universe.join(", ")}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/*
            Both actions are deliberately disabled rather than bound to a handler. The workstation
            endpoints and typed clients exist (`saveStrategyDesignerDraft`,
            `runStrategyDesignerBacktest`), but two contract gaps block an honest wire-up:
            the canvas holds a `StrategyBuilderDocument` and the API takes a `StrategyDesignDocument`
            with no mapper between them, and a backtest run additionally requires governed evidence
            references (operator acceptance, retained evidence, accounting, approvals, paper
            validation, governed reports) that this screen cannot collect. Rendering an enabled
            control that silently discards the operator's work — or posts empty evidence arrays into
            a governance flow — is worse than a disabled one that says why.
          */}
          <Button
            type="button"
            variant="outline"
            aria-label="Save strategy design draft"
            disabled
            disabledReason="Saving drafts needs a canvas-to-design-document mapping that is not implemented yet, so this action would discard your work."
          >
            <Save className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">Save draft</span>
          </Button>
          <Button
            type="button"
            variant="secondary"
            aria-label={vm.backtest.runCommand.ariaLabel}
            disabled
            disabledReason={
              vm.backtest.runCommand.disabledReason ??
              "Running a backtest proof needs governed evidence references this screen cannot collect yet."
            }
          >
            <PlayCircle className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{vm.backtest.runCommand.label}</span>
          </Button>
        </div>
      </div>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {vm.metrics.map((metric) => (
          <MetricSnapshotCard
            key={metric.id}
            id={metric.id}
            label={metric.label}
            value={metric.value}
            delta={metric.detail}
            tone={metric.tone}
          />
        ))}
      </div>
      <div className="sr-only" aria-live="polite">
        {vm.liveRegionMessage}
      </div>
    </section>
  );
}

function FieldCatalogPanel({ vm }: { vm: StrategyBuilderWorkbenchViewModel }) {
  return (
    <Card data-testid="strategy-builder-field-catalog">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Database className="h-4 w-4 text-primary" aria-hidden="true" />
          Field catalog
        </CardTitle>
        <CardDescription>AMX-style vocabulary mapped to Meridian canonical data.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <label className="relative block" htmlFor={vm.fieldSearchState.inputId}>
          <span className="sr-only">{vm.fieldSearchState.label}</span>
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" aria-hidden="true" />
          <Input
            id={vm.fieldSearchState.inputId}
            value={vm.fieldSearch}
            onChange={(event) => vm.setFieldSearch(event.target.value)}
            className="pl-9"
            placeholder={vm.fieldSearchState.placeholder}
            aria-label={vm.fieldSearchState.ariaLabel}
            aria-describedby={vm.fieldSearchState.describedBy}
          />
        </label>
        <p id={vm.fieldSearchState.helperId} className="text-xs leading-5 text-muted-foreground">
          {vm.fieldSearchState.helperText}
        </p>
        <p
          id="strategy-builder-field-catalog-search-status"
          role="status"
          aria-live="polite"
          className="text-xs leading-5 text-muted-foreground"
        >
          {vm.fieldSearchState.resultCountText}
        </p>
        <div className="space-y-2" role="list" aria-label={vm.fieldSearchState.resultsLabel}>
          {vm.filteredFields.length > 0 ? vm.filteredFields.map((field) => (
            <FieldCatalogItem key={field.fieldId} field={field} />
          )) : (
            <FieldCatalogEmptyState state={vm.fieldSearchState.emptyState} />
          )}
        </div>
      </CardContent>
    </Card>
  );
}

function FieldCatalogEmptyState({ state }: { state: StrategyBuilderWorkbenchViewModel["fieldSearchState"]["emptyState"] }) {
  if (!state) return null;

  return (
    <ConcreteEmptyState
      role="status"
      ariaLabel={state.ariaLabel}
      icon="search"
      title={state.title}
      detail={state.description}
      compact
      className="rounded-sm border border-dashed border-border/80 bg-secondary/20"
    />
  );
}

function FieldCatalogItem({ field }: { field: StrategyBuilderFieldCatalogItem }) {
  return (
    <div
      role="listitem"
      className={cn(
        "rounded-md border border-border/70 bg-secondary/20 px-3 py-2",
        !field.isEnabled && "border-warning/50 bg-warning/10"
      )}
    >
      <div className="flex items-start gap-2">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="break-words font-mono text-xs font-semibold text-foreground">{field.fieldId}</span>
            <Badge variant={field.isEnabled ? "success" : "warning"} dot>
              {field.isEnabled ? "Mapped" : "Disabled"}
            </Badge>
          </div>
          <p className="mt-1 text-xs font-medium text-foreground">{field.label}</p>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">{field.source}</p>
          {!field.isEnabled && field.disabledReason ? (
            <p className="mt-1 text-xs leading-5 text-warning" data-testid={`field-disabled-${field.fieldId}`}>
              {field.disabledReason}
            </p>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function CellCanvasPanel({ vm }: { vm: StrategyBuilderWorkbenchViewModel }) {
  const columns: DenseDataTableColumn<StrategyBuilderCellViewModel>[] = [
    {
      id: "cell",
      label: "Cell",
      render: (row) => (
        <div className="min-w-0">
          <div className="font-medium text-foreground">{row.label}</div>
          <div className="mt-1 flex items-center gap-1.5">
            <CellKindIcon kind={row.kind} />
            <Badge variant={cellKindBadgeVariant(row.kind)}>{row.kind}</Badge>
            <span className="font-mono text-xs text-muted-foreground">{row.cellId}</span>
          </div>
        </div>
      )
    },
    {
      id: "purpose",
      label: "Purpose",
      render: (row) => <Badge variant="outline">{row.purpose}</Badge>
    },
    {
      id: "fields",
      label: "Fields",
      render: (row) => (
        <div className="flex flex-wrap gap-1">
          {row.fieldChips.map((field) => (
            <div key={field.fieldId} className="flex flex-col gap-1">
              <Badge variant={field.isEnabled ? "outline" : "warning"}>
                {field.fieldId}
              </Badge>
              {field.disabledReason ? (
                <span className="text-[10px] leading-4 text-warning">{field.disabledReason}</span>
              ) : null}
            </div>
          ))}
        </div>
      )
    },
    {
      id: "status",
      label: "Status",
      render: (row) => <SeverityBadge status={row.status} label={row.statusLabel} />
    }
  ];

  return (
    <Card data-testid="strategy-builder-canvas">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <FlaskConical className="h-4 w-4 text-primary" aria-hidden="true" />
          Cell canvas
        </CardTitle>
        <CardDescription>{vm.validationSummary}</CardDescription>
      </CardHeader>
      <CardContent>
        <DenseDataTable
          rows={vm.cells}
          columns={columns}
          getRowId={(row) => row.cellId}
          getRowSelectAriaLabel={(row) => row.selectButtonAriaLabel}
          getRowAriaControls={(row) => row.detailPanelId}
          getRowAriaExpanded={(row) => row.isSelected}
          onRowSelect={(row) => vm.selectCell(row.cellId)}
          selectedRowId={vm.selectedCellId}
          emptyText="No cells are in the current strategy design."
          ariaLabel="Strategy Builder cells"
          caption="Strategy Builder cells"
        />
      </CardContent>
    </Card>
  );
}

function InspectorPanel({ vm }: { vm: StrategyBuilderWorkbenchViewModel }) {
  return (
    <Card data-testid="strategy-builder-inspector">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <ShieldCheck className="h-4 w-4 text-primary" aria-hidden="true" />
          Inspector
        </CardTitle>
        <CardDescription>{vm.selectedCellDetail?.title ?? "No cell selected"}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <CellDetail detail={vm.selectedCellDetail} />
        <div>
          <h3 className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Validation</h3>
          <div className="mt-2 space-y-2" role="list" aria-label="Strategy Builder validation messages">
            {vm.validationMessages.length === 0 ? (
              <p className="text-sm text-success">Ready for preview and backtest.</p>
            ) : (
              vm.validationMessages.map((message) => (
                <div key={`${message.code}-${message.targetId}`} role="listitem" className="rounded-md border border-border/70 px-3 py-2">
                  <div className="flex items-center gap-2">
                    <SeverityBadge status={message.severity} label={message.severity} />
                    <span className="font-mono text-xs text-muted-foreground">{message.code}</span>
                  </div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{message.message}</p>
                </div>
              ))
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function CellDetail({ detail }: { detail: StrategyBuilderCellDetailViewModel | null }) {
  if (!detail) {
    return <p className="text-sm text-muted-foreground">No selected cell.</p>;
  }

  return (
    <section
      id={detail.id}
      className="row-detail-panel"
      role="region"
      aria-label={detail.ariaLabel}
      data-testid="strategy-builder-selected-cell-detail"
    >
      <div className="head">
        <span>{detail.eyebrow}</span>
      </div>
      <div className="body space-y-3">
        <div>
          <h3 className="break-words text-sm font-semibold text-foreground">{detail.title}</h3>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">{detail.description}</p>
        </div>
        <pre className="max-h-28 overflow-auto rounded-md border border-border/70 bg-secondary/25 p-2 text-xs text-muted-foreground">
          {detail.source}
        </pre>
        <dl className="grid gap-2">
          {detail.fields.map((field) => (
            <StrategyDetailField key={field.id} field={field} />
          ))}
        </dl>
      </div>
    </section>
  );
}

function StrategyDetailField({ field }: { field: StrategyLegDetailFieldViewModel }) {
  return (
    <div className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2">
      <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
      <dd className="mt-1 break-words font-mono text-xs text-foreground">{field.value}</dd>
    </div>
  );
}

function TemplateGallery({ vm }: { vm: StrategyBuilderWorkbenchViewModel }) {
  return (
    <Card data-testid="strategy-builder-template-gallery">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Sparkles className="h-4 w-4 text-primary" aria-hidden="true" />
          Template gallery
        </CardTitle>
        <CardDescription>Prototype concepts rebuilt as Meridian design templates.</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-3 md:grid-cols-3">
        {vm.templates.map((template) => (
          <TemplateCard key={template.templateId} template={template} onLoad={vm.loadTemplate} />
        ))}
      </CardContent>
    </Card>
  );
}

function TemplateCard({ template, onLoad }: { template: StrategyBuilderTemplate; onLoad: (templateId: string) => void }) {
  return (
    <article className="rounded-md border border-border/70 bg-secondary/20 p-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="break-words text-sm font-semibold text-foreground">{template.name}</h3>
          <p className="mt-1 text-xs text-muted-foreground">{template.category}</p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => onLoad(template.templateId)}
          aria-label={template.loadCommand.ariaLabel}
        >
          {template.loadCommand.label}
        </Button>
      </div>
      <p className="mt-2 text-xs leading-5 text-muted-foreground">{template.description}</p>
      <div className="mt-3 flex flex-wrap gap-1">
        {template.tags.map((tag) => (
          <Badge key={tag} variant="outline">
            {tag}
          </Badge>
        ))}
      </div>
    </article>
  );
}

function BacktestProofPanel({ vm }: { vm: StrategyBuilderWorkbenchViewModel }) {
  return (
    <Card data-testid="strategy-builder-backtest-proof">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <PlayCircle className="h-4 w-4 text-primary" aria-hidden="true" />
          Backtest proof
        </CardTitle>
        <CardDescription>{vm.backtest.proofSummary}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {vm.trace.length > 0 ? (
          <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
            <div className="eyebrow-label mb-3">Review gates</div>
            <GateRail
              gates={vm.trace.map((step) => ({
                key: step.stepId,
                label: step.label,
                status: step.status
              }))}
            />
          </div>
        ) : null}
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="rounded-md border border-border/70 p-3">
            <div className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Dataset fingerprint</div>
            <div className="mt-1 break-words font-mono text-xs text-foreground">{vm.backtest.datasetFingerprint}</div>
          </div>
          <div className="rounded-md border border-border/70 p-3">
            <div className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Execution mode</div>
            <div className="mt-1 text-sm font-medium text-foreground">Backtest only</div>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {vm.backtest.routeActions.map((action) => action.isBrowserNavigable ? (
            <a
              key={action.id}
              href={action.href}
              aria-label={action.ariaLabel}
              className="inline-flex items-center gap-1 rounded-md border border-border/70 px-2.5 py-1.5 text-xs text-muted-foreground hover:text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
            >
              <Eye className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{action.method}</span>
              <span>{action.label}</span>
              <span className="rounded-sm border border-border/60 px-1 py-0.5 text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
                {action.interactionLabel}
              </span>
            </a>
          ) : (
            <div
              key={action.id}
              role="group"
              aria-label={action.ariaLabel}
              className="inline-flex items-center gap-1 rounded-md border border-border/70 bg-secondary/20 px-2.5 py-1.5 text-xs text-muted-foreground"
            >
              <Eye className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{action.method}</span>
              <span>{action.label}</span>
              <span className="rounded-sm border border-border/60 px-1 py-0.5 text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
                {action.interactionLabel}
              </span>
            </div>
          ))}
        </div>
        <div className="space-y-2" role="list" aria-label="Strategy Builder run trace">
          {vm.trace.map((step) => (
            <div
              key={step.stepId}
              role="listitem"
              className={cn(
                "rounded-md border border-border/70 px-3 py-2",
                step.isSelectedCell && "border-primary/60 bg-primary/10"
              )}
            >
              <div className="flex items-center gap-2">
                <SeverityBadge status={step.status} label={step.status} />
                <span className="text-sm font-medium text-foreground">{step.label}</span>
              </div>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">{step.detail}</p>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

function TransitionMap({ transitions }: { transitions: StrategyBuilderTransitionViewModel[] }) {
  return (
    <Card data-testid="strategy-builder-transition-map">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <GitBranch className="h-4 w-4 text-primary" aria-hidden="true" />
          Transition map
        </CardTitle>
        <CardDescription>{transitions.length} transition{transitions.length === 1 ? "" : "s"}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        {transitions.map((transition) => (
          <div key={transition.transitionId} className="rounded-md border border-border/70 px-3 py-2">
            <div className="flex flex-wrap items-center gap-2">
              <SeverityBadge status={transition.status} label={transition.statusLabel} />
              <span className="break-words text-sm font-medium text-foreground">{transition.label}</span>
            </div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{transition.detail}</p>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

function OptionsPayoffPanel({ vm }: { vm: StrategyDesignerViewModel }) {
  return (
    <Card data-testid="strategy-designer-options-payoff-panel">
      <CardHeader>
        <CardTitle className="text-base">Options payoff template</CardTitle>
        <CardDescription>{vm.payoff.caption}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" onClick={vm.loadSample} aria-label={vm.loadSampleCommand.ariaLabel}>
            <Sparkles className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{vm.loadSampleCommand.label}</span>
          </Button>
          <Button
            type="button"
            variant={vm.clearCanvasCommand.confirmationPending ? "secondary" : "outline"}
            onClick={vm.clearCanvas}
            disabled={vm.clearCanvasCommand.disabled}
            disabledReason={vm.clearCanvasCommand.disabledReason}
            aria-label={vm.clearCanvasCommand.ariaLabel}
          >
            {vm.clearCanvasCommand.label}
          </Button>
          <div className="ml-auto grid min-w-[11rem] gap-1">
            <label
              htmlFor={vm.spotPriceField.id}
              className="text-xs uppercase tracking-[0.14em] text-muted-foreground"
            >
              {vm.spotPriceField.label}
            </label>
            <Input
              id={vm.spotPriceField.id}
              type="number"
              step={vm.spotPriceField.step}
              min={vm.spotPriceField.min}
              inputMode={vm.spotPriceField.inputMode}
              value={vm.spotPriceField.value}
              error={vm.spotPriceField.invalid}
              onChange={(event) => vm.updateSpotPriceDraft(event.target.value)}
              onBlur={(event) => vm.commitSpotPriceDraft(event.target.value)}
              className="w-full"
              aria-label={vm.spotPriceField.ariaLabel}
              aria-describedby={vm.spotPriceField.describedBy}
              aria-errormessage={vm.spotPriceField.feedbackMessage ? vm.spotPriceField.feedbackId : undefined}
            />
            <p id={vm.spotPriceField.helperId} className="text-xs normal-case leading-5 tracking-normal text-muted-foreground">
              {vm.spotPriceField.helperText}
            </p>
            {vm.spotPriceField.feedbackMessage ? (
              <p
                id={vm.spotPriceField.feedbackId}
                role="status"
                className="text-xs normal-case leading-5 tracking-normal text-warning"
              >
                {vm.spotPriceField.feedbackMessage}
              </p>
            ) : null}
          </div>
        </div>
        <div className="grid gap-4 lg:grid-cols-2">
          <PayoffCard payoff={vm.payoff} legCount={vm.legs.length} />
          <ParticipationCard participation={vm.participation} />
        </div>
      </CardContent>
    </Card>
  );
}

function PayoffCard({ payoff, legCount }: { payoff: PayoffChartViewModel; legCount: number }) {
  return (
    <div data-testid="strategy-designer-payoff">
      {payoff.isEmpty ? (
        <div className="flex h-[220px] items-center justify-center rounded-md border border-dashed border-border/70 text-sm text-muted-foreground">
          Add a leg to render the payoff curve.
        </div>
      ) : (
        <svg viewBox={`0 0 ${payoff.width} ${payoff.height}`} role="img" aria-label={payoff.ariaLabel} className="w-full max-w-full">
          <rect
            x={payoff.paddingLeft}
            y={payoff.paddingTop}
            width={payoff.width - payoff.paddingLeft - payoff.paddingRight}
            height={payoff.height - payoff.paddingTop - payoff.paddingBottom}
            fill="transparent"
            stroke="hsl(var(--border))"
            strokeOpacity={0.4}
          />
          {payoff.zeroLineY !== null && (
            <line
              x1={payoff.paddingLeft}
              x2={payoff.width - payoff.paddingRight}
              y1={payoff.zeroLineY}
              y2={payoff.zeroLineY}
              stroke="hsl(var(--muted-foreground))"
              strokeDasharray="4 4"
              strokeOpacity={0.6}
            />
          )}
          {payoff.spotLineX !== null && (
            <line
              x1={payoff.spotLineX}
              x2={payoff.spotLineX}
              y1={payoff.paddingTop}
              y2={payoff.height - payoff.paddingBottom}
              stroke="hsl(var(--primary))"
              strokeDasharray="2 4"
              strokeOpacity={0.6}
            />
          )}
          <polyline
            points={payoff.points.map((p) => `${p.x},${p.y}`).join(" ")}
            fill="none"
            stroke="hsl(var(--primary))"
            strokeWidth={1.75}
            strokeLinecap="round"
            strokeLinejoin="round"
            data-testid="strategy-designer-payoff-polyline"
          />
        </svg>
      )}
      <p className="mt-2 text-xs text-muted-foreground" data-testid="strategy-designer-payoff-legcount">
        Sampled across {legCount} leg{legCount === 1 ? "" : "s"}.
      </p>
    </div>
  );
}

function ParticipationCard({ participation }: { participation: ParticipationViewModel }) {
  return (
    <div data-testid="strategy-designer-participation">
      {participation.isEmpty ? (
        <div className="flex h-[180px] items-center justify-center rounded-md border border-dashed border-border/70 text-sm text-muted-foreground">
          Awaiting first leg.
        </div>
      ) : (
        <ul className="space-y-2" data-testid="strategy-designer-participation-list">
          {participation.slices.map((slice) => (
            <ParticipationRow key={slice.legId} slice={slice} />
          ))}
        </ul>
      )}
    </div>
  );
}

function ParticipationRow({ slice }: { slice: ParticipationSliceViewModel }) {
  return (
    <li className="space-y-1">
      <div className="flex items-center justify-between gap-2 text-sm">
        <div className="flex items-center gap-2">
          <Badge variant={slice.tone === "long" ? "success" : "warning"} dot>
            {slice.direction}
          </Badge>
          <span className="font-medium text-foreground">{slice.label}</span>
        </div>
        <span className="font-mono text-xs text-muted-foreground">{slice.sharePercent}</span>
      </div>
      <div className="h-2 w-full overflow-hidden rounded-full bg-secondary/40" role="presentation">
        <div
          className={cn("h-full rounded-full", slice.tone === "long" ? "bg-success" : "bg-warning")}
          style={{ width: slice.barWidth }}
          data-testid={`participation-bar-${slice.legId}`}
        />
      </div>
      <div className="text-xs text-muted-foreground">{slice.detail}</div>
    </li>
  );
}
