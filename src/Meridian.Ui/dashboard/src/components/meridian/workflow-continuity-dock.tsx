import { useState } from "react";
import { ChevronDown, GitBranch, SlidersHorizontal, X } from "lucide-react";
import { Link } from "react-router-dom";
import "@/styles/workflow-continuity-dock.css";
import type { AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import type { AppShellOperatingScopeQueryKey } from "@/app-shell.operating-scope";
import { cn } from "@/lib/utils";

export function WorkflowContinuityDock({
  viewModel,
  onClearOperatingContext,
  onEditOperatingContext,
  scopeDimensionsInEffect
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
  onClearOperatingContext?: () => void;
  onEditOperatingContext?: () => void;
  scopeDimensionsInEffect?: AppShellOperatingScopeQueryKey[];
}) {
  const decision = viewModel.decisionBrief;
  const scopeSummary = viewModel.operatingScope.items.map((item) => `${item.label} ${item.value}`).join(", ");
  const isDimensionInEffect = (id: string) =>
    !scopeDimensionsInEffect || scopeDimensionsInEffect.includes(id as AppShellOperatingScopeQueryKey);
  const [operatorFlowOpen, setOperatorFlowOpen] = useState(false);
  const toggleOperatorFlow = () => setOperatorFlowOpen((isOpen) => !isOpen);

  return (
    <section
      className="workflow-continuity-dock"
      aria-label={viewModel.ariaLabel}
      aria-describedby="workflow-continuity-screenreader-summary"
    >
      <p id="workflow-continuity-screenreader-summary" className="sr-only">
        {viewModel.title}. {viewModel.summary} Current route {viewModel.routeLabel}. Next action: {viewModel.nextActionLabel}.
      </p>
      <div className="workflow-continuity-context">
        <div className="flex min-w-0 items-center gap-2">
          <GitBranch className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
          <div className="min-w-0">
            <div className="eyebrow-label">{viewModel.contextLabel}</div>
            <h2 className="workflow-continuity-title">{viewModel.title}</h2>
          </div>
        </div>
        <p className="sr-only">{viewModel.summary}</p>
        <div className="workflow-continuity-meta" aria-label={`Current route ${viewModel.routeLabel}`}>
          <span>{viewModel.contextValue}</span>
          <span>{viewModel.routeLabel}</span>
          {onEditOperatingContext ? (
            <button
              type="button"
              className="workflow-continuity-clear focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              onClick={onEditOperatingContext}
              aria-label={viewModel.operatingScope.hasScope ? "Change operating scope" : "Set operating scope"}
              title={viewModel.operatingScope.hasScope ? "Change operating scope" : "Set operating scope"}
            >
              <SlidersHorizontal className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          ) : null}
          {viewModel.operatingScope.hasScope && viewModel.clearSubjectAriaLabel && onClearOperatingContext ? (
            <button
              type="button"
              className="workflow-continuity-clear focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              onClick={onClearOperatingContext}
              aria-label={viewModel.clearSubjectAriaLabel}
              title={viewModel.clearSubjectAriaLabel}
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          ) : null}
        </div>
        {scopeSummary ? <p className="sr-only">Operating scope: {scopeSummary}.</p> : null}
        {viewModel.operatingScope.items.length > 0 ? (
          <dl className="workflow-continuity-scope" aria-label={viewModel.operatingScope.label}>
            {viewModel.operatingScope.items.map((item) => {
              const inEffect = isDimensionInEffect(item.id);
              return (
                <div
                  key={item.id}
                  className={cn("workflow-continuity-scope-chip", !inEffect && "workflow-continuity-scope-chip-inactive")}
                  aria-label={inEffect ? item.ariaLabel : `${item.ariaLabel}. Not applied on this workspace.`}
                  title={inEffect ? undefined : "Carried, but not applied on this workspace"}
                >
                  <dt>{item.label}</dt>
                  <dd>{item.value}</dd>
                </div>
              );
            })}
          </dl>
        ) : null}
      </div>

      {/* The cross-workspace decision brief renders as the masthead status pill
          (DecisionBriefPill); the dock keeps only operating context and the
          on-demand flow details so a blocked item never repaints every route. */}
      <details
        className="workflow-continuity-operator-flow"
        aria-label={`${viewModel.primaryOperatorFlowLabel}: ${viewModel.primaryOperatorFlowSummary}`}
        open={operatorFlowOpen}
      >
        <summary
          tabIndex={0}
          onClick={(event) => {
            event.preventDefault();
            toggleOperatorFlow();
          }}
          onKeyDown={(event) => {
            if (event.key !== "Enter" && event.key !== " ") {
              return;
            }
            event.preventDefault();
            toggleOperatorFlow();
          }}
        >
          <span className="workflow-continuity-flow-summary">
            <span className="workflow-continuity-flow-label">Flow details</span>
            <span className="sr-only">
              {viewModel.primaryOperatorFlowLabel}: {viewModel.primaryOperatorFlowSummary}
            </span>
          </span>
          <ChevronDown className="workflow-continuity-disclosure-icon h-4 w-4" aria-hidden="true" />
        </summary>
        {operatorFlowOpen ? (
          <>
            <p className="workflow-continuity-expanded-flow">
              <span>{viewModel.primaryOperatorFlowLabel}</span>
              <span>{viewModel.primaryOperatorFlowSummary}</span>
            </p>
            <div className="workflow-continuity-expanded-decision" aria-label={`${decision.label} detail`}>
              <span className="workflow-continuity-decision-status">{decision.statusLabel}</span>
              <p>{decision.summary}</p>
              <Link to={viewModel.nextActionHref} aria-label={viewModel.nextActionAriaLabel}>
                {viewModel.nextActionLabel}
              </Link>
            </div>
            <nav className="workflow-continuity-steps" aria-label={viewModel.stepsLabel}>
              {viewModel.steps.map((step) => (
                <Link
                  key={step.id}
                  to={step.href}
                  aria-label={step.ariaLabel}
                  aria-current={step.active ? "step" : undefined}
                  className={cn(
                    "workflow-continuity-step focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                    `workflow-continuity-step-${step.statusTone}`,
                    step.active && "workflow-continuity-step-active",
                    step.next && "workflow-continuity-step-next"
                  )}
                >
                  <span className="workflow-continuity-step-label">{step.label}</span>
                  <span className="workflow-continuity-step-description">{step.description}</span>
                  <span className="workflow-continuity-step-status">{step.statusLabel}</span>
                </Link>
              ))}
            </nav>
            <nav className="workflow-continuity-steps" aria-label={viewModel.primaryOperatorFlowStepsLabel}>
              {viewModel.primaryOperatorFlowSteps.map((step) => (
                <Link
                  key={step.id}
                  to={step.href}
                  aria-label={step.ariaLabel}
                  aria-current={step.active ? "step" : undefined}
                  className={cn(
                    "workflow-continuity-step focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                    `workflow-continuity-step-${step.statusTone}`,
                    step.active && "workflow-continuity-step-active"
                  )}
                >
                  <span className="workflow-continuity-step-label">{step.label}</span>
                  <span className="workflow-continuity-step-description">{step.description}</span>
                  <span className="workflow-continuity-step-status">{step.statusLabel}</span>
                </Link>
              ))}
            </nav>
          </>
        ) : null}
      </details>
    </section>
  );
}
