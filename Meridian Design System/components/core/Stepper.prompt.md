Horizontal step rail for a bounded flow — backtest setup, onboarding, a wizard. Shows progress and lets the operator jump back to a completed step.

```jsx
<Stepper activeStep={1} onStepChange={setStep} steps={[
  { label: "Universe" }, { label: "Signals", badge: "3" }, { label: "Review" },
]} />
```

Drive `activeStep` yourself; use `badge` for a per-step count. Gate forward movement on validity — don't let the operator skip an incomplete step.
