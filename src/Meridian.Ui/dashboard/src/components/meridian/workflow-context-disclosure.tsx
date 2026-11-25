import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { Link } from "react-router-dom";
import "@/styles/workflow-context-disclosure.css";
import type { AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import { cn } from "@/lib/utils";

/**
 * On-demand workflow detail (current workflow, decision, steps). It lives in the scope picker,
 * which the top-bar scope bar opens, so the header keeps a single row. The worst cross-workspace issue
 * itself renders as the masthead attention pill (AttentionPill).
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
        // One container for the on-demand detail, so a host that places it in a narrow
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
