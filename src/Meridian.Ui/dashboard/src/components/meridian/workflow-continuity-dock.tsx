import { ArrowRight, ChevronDown, GitBranch, X } from "lucide-react";
import { Link } from "react-router-dom";
import "@/styles/workflow-continuity-dock.css";
import type { AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export function WorkflowContinuityDock({
  viewModel,
  onClearOperatingContext
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
  onClearOperatingContext?: () => void;
}) {
  const visibleSteps = viewModel.steps.filter((step) => step.active || step.next);
  const dockSteps = visibleSteps.length > 0 ? visibleSteps : viewModel.steps.slice(0, 2);
  const decision = viewModel.decisionBrief;

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
        <p className="workflow-continuity-summary">{viewModel.summary}</p>
        <div className="workflow-continuity-meta" aria-label={`Current route ${viewModel.routeLabel}`}>
          <span>{viewModel.contextValue}</span>
          <span>{viewModel.routeLabel}</span>
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
        {viewModel.operatingScope.items.length > 0 ? (
          <dl className="workflow-continuity-scope" aria-label={viewModel.operatingScope.label}>
            {viewModel.operatingScope.items.map((item) => (
              <div key={item.id} className="workflow-continuity-scope-chip" aria-label={item.ariaLabel}>
                <dt>{item.label}</dt>
                <dd>{item.value}</dd>
              </div>
            ))}
          </dl>
        ) : null}
      </div>

      <article
        className={cn(
          "workflow-continuity-decision",
          `workflow-continuity-decision-${decision.statusTone}`
        )}
        aria-labelledby="workflow-decision-title"
      >
        <div className="workflow-continuity-decision-copy">
          <div className="workflow-continuity-decision-head">
            <span className="eyebrow-label">{decision.label}</span>
            <span className="workflow-continuity-decision-status">{decision.statusLabel}</span>
          </div>
          <h3 id="workflow-decision-title">{decision.title}</h3>
          <p>{decision.summary}</p>
          <div className="workflow-continuity-decision-reason">
            <span>{decision.reasonLabel}</span>
            <span>{decision.reason}</span>
          </div>
          {decision.evidenceLabel ? (
            <span className="workflow-continuity-decision-evidence">{decision.evidenceLabel}</span>
          ) : null}
        </div>
        <Button asChild variant="default" size="sm" className="workflow-continuity-decision-action">
          <Link to={decision.actionHref} aria-label={decision.actionAriaLabel}>
            <span>{decision.actionLabel}</span>
            <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
          </Link>
        </Button>
      </article>

      <nav className="workflow-continuity-steps" aria-label={viewModel.stepsLabel}>
        {dockSteps.map((step) => (
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

      <Button asChild variant="secondary" size="sm" className="workflow-continuity-next">
        <Link to={viewModel.nextActionHref} aria-label={viewModel.nextActionAriaLabel}>
          <span>{viewModel.nextActionLabel}</span>
          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Link>
      </Button>

      <details
        className="workflow-continuity-operator-flow"
        aria-label={`${viewModel.primaryOperatorFlowLabel}: ${viewModel.primaryOperatorFlowSummary}`}
      >
        <summary>
          <span>
            <span className="eyebrow-label">{viewModel.primaryOperatorFlowLabel}</span>
            <span className="workflow-continuity-summary">{viewModel.primaryOperatorFlowSummary}</span>
          </span>
          <ChevronDown className="workflow-continuity-disclosure-icon h-4 w-4" aria-hidden="true" />
        </summary>
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
      </details>
    </section>
  );
}
