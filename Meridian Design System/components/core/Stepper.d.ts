import React from "react";

export interface StepperStep {
  /** Stable key; falls back to the index */
  id?: string | number;
  label: React.ReactNode;
  /** Secondary line under the label (owner, due date, count) */
  description?: React.ReactNode;
  /** Right-aligned chip — a count, "3 breaks", "optional" */
  badge?: React.ReactNode;
  /** Forces completed styling; defaults to `index < activeStep` */
  complete?: boolean;
  /** Validation failed on this step — red mark, `!` in place of the number */
  error?: boolean;
  /** Never reachable */
  disabled?: boolean;
  /** Not reachable until completed (forward-locked wizards) */
  locked?: boolean;
}

export interface StepperProps extends React.OlHTMLAttributes<HTMLOListElement> {
  steps: StepperStep[];
  /** Index of the current step. @default 0 */
  activeStep?: number;
  /** Omit to render a read-only progress display instead of buttons */
  onStepChange?: (stepIndex: number) => void;
  /** @default true */
  showStepNumber?: boolean;
  /** @default "horizontal" */
  orientation?: "horizontal" | "vertical";
  /** Accessible name of the step list. @default "Steps" */
  label?: string;
}

/**
 * Ordered multi-step navigation — onboarding, reconciliation wizards, trade capture. An ordered
 * list (`<ol>`), **not** tabs: each step announces "Step n of m, state" and the current step
 * carries `aria-current="step"`. Step height tracks `--theme-row-height`; override with
 * `--stepper-height`.
 *
 * With `onStepChange` the steps are buttons; without it they are static text (no dead buttons for
 * keyboard users). `disabled` and `locked` steps are unreachable and non-focusable.
 *
 * @example
 * <Stepper
 *   steps={[
 *     { id: "load", label: "Load file", complete: true },
 *     { id: "match", label: "Match", description: "412 of 480", badge: "3 breaks", error: true },
 *     { id: "post", label: "Post", locked: true },
 *   ]}
 *   activeStep={1}
 *   onStepChange={setStep}
 * />
 */
export declare function Stepper(props: StepperProps): React.JSX.Element;
