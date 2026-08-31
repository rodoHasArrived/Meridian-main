export interface StepperProps {
  steps: Array<{
    label: string;
    badge?: string;
  }>;
  activeStep?: number;
  onStepChange?: (stepIndex: number) => void;
  showStepNumber?: boolean;
}

export const Stepper: React.FC<StepperProps>;
