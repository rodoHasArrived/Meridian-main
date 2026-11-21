Ordered multi-step navigation — onboarding, reconciliation wizards, trade capture, period close. An ordered list, **not** tabs: each step announces "Step n of m, completed / current / not started" and the current step carries `aria-current="step"`. Step height tracks `--theme-row-height`, so it re-densifies with `DensityToggle`; override this component alone with `--stepper-height`.

```jsx
<Stepper steps={steps} activeStep={step} onStepChange={setStep} />
```

**Omit `onStepChange` for a read-only progress display** — the steps render as static text rather than buttons that do nothing when clicked.

Per-step state carries the wizard's real condition: `complete` (defaults to "before the active step"), `error` for a step whose validation failed (red mark, `!` in place of the number), `badge` for a count, `description` for a second line, and two flavors of unreachable — `disabled` (never) and `locked` (not until completed, for forward-locked flows). Both are non-focusable.

```jsx
<Stepper
  activeStep={1}
  onStepChange={setStep}
  steps={[
    { id: "load",  label: "Load file", complete: true },
    { id: "match", label: "Match", description: "412 of 480", badge: "3 breaks", error: true },
    { id: "post",  label: "Post", locked: true },
  ]}
/>
```

`orientation="vertical"` stacks the steps into a left rail — the right choice past about five steps, or when each step needs a description. Pair with `Form`/`FormValidation` and drive `error` off the step's field-level validity so a failed step stays visible after the user moves on.
