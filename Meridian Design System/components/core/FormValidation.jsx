// Meridian form validation layer — field-level + form-level + real-time + submit-time.
// Validators: required, email, number, minLength, maxLength, pattern, custom.
// UI: red borders, inline hints, error icons, form-level summary, tooltip errors.

const Validators = {
  required: (val) => !val || val.toString().trim() === "" ? "Required" : null,
  email: (val) => val && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(val) ? "Invalid email" : null,
  number: (val) => val && isNaN(val) ? "Must be a number" : null,
  minLength: (min) => (val) => val && val.length < min ? `Minimum ${min} characters` : null,
  maxLength: (max) => (val) => val && val.length > max ? `Maximum ${max} characters` : null,
  pattern: (pattern, msg) => (val) => val && !pattern.test(val) ? msg : null,
  custom: (fn) => fn,
};

const FormValidation = {
  Validators,

  // Validate a single field against one or more validators
  validateField: (value, validators) => {
    if (!validators || validators.length === 0) return null;
    for (const v of validators) {
      const error = typeof v === "function" ? v(value) : null;
      if (error) return error;
    }
    return null;
  },

  // Validate all fields in a form state object
  validateForm: (state, schema) => {
    const errors = {};
    for (const [field, validators] of Object.entries(schema)) {
      const error = FormValidation.validateField(state[field], validators);
      if (error) errors[field] = error;
    }
    return errors;
  },
};

// FieldInput — field with real-time + blur validation, inline hint, error icon, tooltip
const FieldInput = ({ label, value, onChange, onBlur, validators, hint, placeholder, type = "text" }) => {
  const [touched, setTouched] = React.useState(false);
  const [error, setError] = React.useState(null);

  const handleBlur = (e) => {
    setTouched(true);
    const err = FormValidation.validateField(value, validators);
    setError(err);
    onBlur && onBlur(e);
  };

  const handleChange = (e) => {
    onChange && onChange(e);
    if (touched) {
      const err = FormValidation.validateField(e.target.value, validators);
      setError(err);
    }
  };

  const css = `
.mds-field{display:flex;flex-direction:column;gap:4px;}
.mds-field-label{font-size:12px;font-weight:600;color:var(--text-primary);}
.mds-field-input{padding:8px 12px;border:1px solid var(--border);background:var(--bg-light);
  color:var(--text-primary);font-family:var(--font-body);font-size:13px;border-radius:var(--radius-chip,2px);
  transition:border-color .12s,box-shadow .12s;}
.mds-field-input:focus{outline:none;border-color:var(--accent);outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-field-input--error{border-color:var(--red);}
.mds-field-input--error:focus{outline:2px solid color-mix(in srgb,var(--red) 55%,transparent);outline-offset:1px;}
.mds-field-hint{font-size:11px;color:var(--text-muted);line-height:1.4;}
.mds-field-error{display:flex;align-items:flex-start;gap:6px;font-size:11px;color:var(--red);}
.mds-field-error-icon{flex:0 0 16px;line-height:1.4;font-weight:600;}
`;
  if (!document.getElementById("mds-field-css")) {
    const el = document.createElement("style");
    el.id = "mds-field-css";
    el.textContent = css;
    document.head.appendChild(el);
  }

  return (
    <div className="mds-field">
      {label && <label className="mds-field-label">{label}</label>}
      <input className={`mds-field-input${error && touched ? " mds-field-input--error" : ""}`}
        type={type} value={value} onChange={handleChange} onBlur={handleBlur}
        placeholder={placeholder} />
      {hint && !error && <div className="mds-field-hint">{hint}</div>}
      {error && touched && <div className="mds-field-error"><span className="mds-field-error-icon">!</span><span>{error}</span></div>}
    </div>
  );
};

// FormErrorSummary — form-level error list at top
const FormErrorSummary = ({ errors, fields }) => {
  const errorList = Object.entries(errors)
    .filter(([_, err]) => err)
    .map(([field, err]) => ({ field, label: fields[field]?.label || field, error: err }));

  if (errorList.length === 0) return null;

  const css = `
.mds-form-errors{background:var(--red-a10,rgba(186,63,85,.10));border:1px solid color-mix(in srgb,var(--red) 30%,transparent);border-radius:var(--radius-chip,2px);
  padding:12px;margin-bottom:16px;}
.mds-form-errors-title{font-size:12px;font-weight:600;color:var(--red);margin-bottom:8px;}
.mds-form-errors-list{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:6px;}
.mds-form-errors-item{font-size:12px;color:var(--red);display:flex;gap:6px;}
.mds-form-errors-item::before{content:'•';flex:0 0 6px;}
`;
  if (!document.getElementById("mds-form-errors-css")) {
    const el = document.createElement("style");
    el.id = "mds-form-errors-css";
    el.textContent = css;
    document.head.appendChild(el);
  }

  return (
    <div className="mds-form-errors">
      <div className="mds-form-errors-title">{errorList.length} error{errorList.length > 1 ? "s" : ""}</div>
      <ul className="mds-form-errors-list">
        {errorList.map(({ field, label, error }) => (
          <li key={field} className="mds-form-errors-item">{label}: {error}</li>
        ))}
      </ul>
    </div>
  );
};

export { FormValidation, FieldInput, FormErrorSummary };
