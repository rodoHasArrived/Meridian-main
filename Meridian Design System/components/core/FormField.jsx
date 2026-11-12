import React from "react";

/**
 * Meridian FormField — Wrapper combining label, input, error, and hint.
 * Reduces boilerplate in forms by bundling all field furniture.
 *
 * @example
 * <FormField label="Email" error={errors.email} hint="Required for login">
 *   <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
 * </FormField>
 */
export function FormField({
  label,
  hint,
  error,
  required = false,
  children,
  className = '',
  style = {},
  id,
}) {
  const generatedId = React.useId();
  const control = React.isValidElement(children) && children.type !== React.Fragment ? children : null;
  const fieldId = control?.props.id || id || generatedId;
  const feedbackId = `${fieldId}--feedback`;
  const describedBy = [control?.props["aria-describedby"], hint || error ? feedbackId : null].filter(Boolean).join(" ") || undefined;

  return (
    <div
      className={className}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-sm)',
        ...style
      }}
    >
      {/* Label */}
      {label && (
        <label
          htmlFor={control ? fieldId : undefined}
          style={{
            fontSize: 'var(--type-label)',
            fontWeight: 600,
            color: 'var(--text-primary)',
            textTransform: 'uppercase',
            letterSpacing: 'var(--letter-spacing-label)',
          }}
        >
          {label}
          {required && (
            <span aria-hidden="true" style={{ color: 'var(--red-dim)', marginLeft: '4px' }}>*</span>
          )}
        </label>
      )}

      {/* Input (cloned with id and aria attrs) */}
      {control ? React.cloneElement(control, {
        id: fieldId,
        required: required || control.props.required,
        "aria-invalid": error ? true : control.props["aria-invalid"],
        "aria-describedby": describedBy,
      }) : children}

      {/* Hint or error message */}
      {(hint || error) && (
        <div
          id={feedbackId}
          role={error ? "alert" : undefined}
          style={{
            fontSize: 'var(--type-caption)',
            color: error ? 'var(--red-dim)' : 'var(--text-muted)',
            marginTop: '-4px',
          }}
        >
          {error || hint}
        </div>
      )}
    </div>
  );
}

FormField.displayName = 'FormField';
