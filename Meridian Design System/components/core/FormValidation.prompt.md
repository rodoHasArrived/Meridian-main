Validation toolkit — composable `Validators`, a `validateForm(state, schema)` runner, and the `FieldInput` / `FormErrorSummary` helpers. Pure logic plus thin inputs; no form-state library required.

```jsx
const schema = {
  name:  [FormValidation.Validators.required],
  email: [FormValidation.Validators.required, FormValidation.Validators.email],
};
const errors = FormValidation.validateForm(values, schema); // { field: message }
<FormErrorSummary errors={errors} fields={{ name: { label: "Name" }, email: { label: "Email" } }} />
```

Compose validators per field (`minLength(3)`, `pattern(/…/, msg)`, `custom(fn)`). Run `validateForm` on blur and on submit; block submit while any message is non-empty.
