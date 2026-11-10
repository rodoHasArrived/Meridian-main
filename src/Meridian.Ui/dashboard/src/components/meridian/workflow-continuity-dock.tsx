import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { Link } from "react-router-dom";
import "@/styles/workflow-continuity-dock.css";
import type { AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import type { AppShellOperatingScopeQueryKey } from "@/app-shell.operating-scope";
import { cn } from "@/lib/utils";

/** Shown in the scope chip when no scope dimension is set: nothing is narrowed. */
export const NO_SCOPE_LABEL = "All funds";

/**
 * The operating scope as one header chip: it shows the scope values and opens the scope picker.
 * Dimensions the current screen does not filter by stay in the chip, muted, so a carried scope is
 * never invisible; the picker states per dimension whether it is applied here or retained.
 */
export function OperatingScopeChip({
  viewModel,
  onEditOperatingContext,
  scopeDimensionsInEffect
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
  onEditOperatingContext?: () => void;
  scopeDimensionsInEffect?: AppShellOperatingScopeQueryKey[];
}) {
  const routePathLabel = viewModel.routeLabel.split(/[?#]/, 1)[0] || "/";
  const isDimensionInEffect = (id: string) =>
    !scopeDimensionsInEffect || scopeDimensionsInEffect.includes(id as AppShellOperatingScopeQueryKey);
  const items = viewModel.operatingScope.items;
  const appliedScope = items.filter((item) => isDimensionInEffect(item.id));
  const retainedScope = items.filter((item) => !isDimensionInEffect(item.id));
  const describeScope = (scopeItems: typeof items) => scopeItems.map((item) => `${item.label} ${item.value}`).join(", ");
  const hasScope = viewModel.operatingScope.hasScope && items.length > 0;
  const valueLabel = hasScope ? items.map((item) => item.value).join(" · ") : NO_SCOPE_LABEL;

  if (viewModel.mode === "hidden") return null;

  const value = hasScope
    ? items.map((item, index) => (
        <span
          key={item.id}
          className={cn("operating-scope-chip-item", !isDimensionInEffect(item.id) && "operating-scope-chip-item-retained")}
          title={isDimensionInEffect(item.id) ? `${item.label}: applied here` : `${item.label}: carried, but not applied on this screen`}
        >
          {index > 0 ? <span aria-hidden="true" className="operating-scope-chip-sep">·</span> : null}
          {item.value}
        </span>
      ))
    : NO_SCOPE_LABEL;

  return (
    <section
      className="operating-scope"
      aria-label={viewModel.ariaLabel}
      aria-describedby="workflow-continuity-screenreader-summary"
    >
      <p id="workflow-continuity-screenreader-summary" className="sr-only">
        {viewModel.title}. {viewModel.summary} Current route {routePathLabel}. Next action: {viewModel.nextActionLabel}.
      </p>
      {hasScope ? (
        <p id="operating-scope-applicability" className="sr-only">
          {appliedScope.length > 0 ? `Applied on this screen: ${describeScope(appliedScope)}.` : "No selected scope dimensions apply on this screen."}
          {retainedScope.length > 0 ? ` Retained for other screens: ${describeScope(retainedScope)}.` : ""}
        </p>
      ) : null}
      {onEditOperatingContext ? (
        <button
          type="button"
          className="operating-scope-chip"
          onClick={onEditOperatingContext}
          aria-label={`Operating scope: ${valueLabel}`}
          aria-describedby={hasScope ? "operating-scope-applicability" : undefined}
          aria-haspopup="dialog"
          title="Change operating scope and view workflow context"
        >
          <span className="operating-scope-chip-value">{value}</span>
          <ChevronDown className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        </button>
      ) : (
        <span className="operating-scope-chip operating-scope-chip-static" aria-label={`Operating scope: ${valueLabel}`}>
          <span className="operating-scope-chip-value">{value}</span>
        </span>
      )}
    </section>
  );
}

/**
 * On-demand workflow detail (current workflow, decision, steps). It lives in the scope chip's
 * popover, the scope picker, so the header keeps a single row. The cross-workspace decision
 * brief itself renders as the masthead status pill (DecisionBriefPill).
 */
export function WorkflowContextDisclosure({ viewModel }: { viewModel: AppShellWorkflowContinuityViewModel }) {
  const decision = viewModel.decisionBrief;
  const [operatorFlowOpen, setOperatorFlowOpen] = useState(false);
  const toggleOperatorFlow = () => setOperatorFlowOpen((isOpen) => !isOpen);

  if (viewModel.mode === "hidden") return null;

  return (
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
          <span className="workflow-continuity-flow-label">Workflow context</span>
          <span className="sr-only">
            {viewModel.primaryOperatorFlowLabel}: {viewModel.primaryOperatorFlowSummary}
          </span>
        </span>
        <ChevronDown className="workflow-continuity-disclosure-icon h-4 w-4" aria-hidden="true" />
      </summary>
      {operatorFlowOpen ? (
        // One container for the on-demand detail, so a host that places the dock in a narrow
        // slot (the compact workspace header's breadcrumb row) can show it as a popover.
        <div className="workflow-continuity-expanded">
          <p className="workflow-continuity-expanded-flow">
            <span>{viewModel.primaryOperatorFlowLabel}</span>
            <span>{viewModel.primaryOperatorFlowSummary}</span>
          </p>
          <p className="workflow-continuity-expanded-flow">
            <span>Current workflow</span>
            <span>{viewModel.title}</span>
          </p>
          <div className="workflow-continuity-expanded-decision" aria-label={`${decision.label} detail`}>
            <span className="workflow-continuity-decision-status">{decision.statusLabel}</span>
            <p>{decision.summary}</p>
            <Link to={viewModel.nextActionHref} aria-label={viewModel.nextActionAriaLabel}>
              {viewModel.nextActionLabel}
            </Link>
          </div>
          {viewModel.steps.length > 0 ? (
            <nav className="workflow-continuity-steps" aria-label={viewModel.stepsLabel}>
              {viewModel.steps.map((step) => (
                <Link
                  key={step.id}
                  to={step.href}
                  aria-label={step.ariaLabel}
                  aria-current={step.active ? "step" : undefined}
                  className={cn(
                    "workflow-continuity-step focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40",
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
          ) : null}
          <nav className="workflow-continuity-steps" aria-label={viewModel.primaryOperatorFlowStepsLabel}>
            {viewModel.primaryOperatorFlowSteps.map((step) => (
              <Link
                key={step.id}
                to={step.href}
                aria-label={step.ariaLabel}
                aria-current={step.active ? "step" : undefined}
                className={cn(
                  "workflow-continuity-step focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40",
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
        </div>
      ) : null}
    </details>
  );
}
