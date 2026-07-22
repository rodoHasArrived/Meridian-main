import { type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface Step {
  label: ReactNode;
  badge?: ReactNode;
}

export interface StepperProps {
  steps: Step[];
  activeStep?: number;
  onStepChange?: (index: number) => void;
  showStepNumber?: boolean;
  className?: string;
}

/**
 * Tab-like multi-step form navigation — jump to any step. Concrete: flat, hairline divider
 * between steps, active step marked by a teal-blue bottom rule and an accent number chip;
 * completed steps show a check.
 */
export function Stepper({ steps, activeStep = 0, onStepChange, showStepNumber = true, className }: StepperProps) {
  return (
    <div role="tablist" className={cn("flex gap-px border-b border-border bg-border", className)}>
      {steps.map((step, index) => {
        const active = index === activeStep;
        const complete = index < activeStep;
        return (
          <button
            key={index}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onStepChange?.(index)}
            className={cn(
              "flex flex-1 items-center gap-2 truncate px-4 py-2.5 text-left text-xs transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 [outline-offset:-2px]",
              active
                ? "border-b-2 border-primary bg-background pb-[9px] font-semibold text-foreground"
                : "bg-card text-muted-foreground hover:bg-[#EAEEF3] hover:text-foreground"
            )}
          >
            {showStepNumber ? (
              <span
                aria-hidden="true"
                className={cn(
                  "inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full font-mono text-[10px] font-semibold",
                  active
                    ? "bg-primary text-primary-foreground"
                    : complete
                      ? "bg-success text-primary-foreground"
                      : "bg-[#F3F6F9] text-muted-foreground"
                )}
              >
                {complete ? "✓" : index + 1}
              </span>
            ) : null}
            <span className="truncate">{step.label}</span>
            {step.badge != null ? (
              <span className="ml-auto shrink-0 rounded-[2px] bg-[#F3F6F9] px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">
                {step.badge}
              </span>
            ) : null}
          </button>
        );
      })}
    </div>
  );
}
