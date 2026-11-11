/**
 * Meridian FormField — Wrapper combining label, input, error, and hint.
 * Reduces boilerplate in forms by bundling all field furniture.
 *
 * @example
 * <FormField label="Email" error={errors.email} hint="Required for login">
 *   <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
 * </FormField>
 */
import React from "react";

export function FormField({
  label,
  hint,
  error,
  required = false,
  children,
  className = '',
  style = {},
}) {
  const generatedId = React.useId();
  const control = React.isValidElement(children) ? children : null;
  const fieldId = control?.props.id || generatedId;
  const messageId = `${fieldId}-message`;

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
          htmlFor={fieldId}
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
            <span aria-hidden="true" style={{ color: 'var(--red)', marginLeft: '4px' }}>*</span>
          )}
        </label>
      )}

      {/* Input (cloned with id and aria attrs) */}
      {control ? React.cloneElement(control, {
        id: fieldId,
        "aria-required": required || control.props["aria-required"],
        "aria-invalid": error ? true : control.props["aria-invalid"],
        "aria-describedby": [control.props["aria-describedby"], hint || error ? messageId : null].filter(Boolean).join(" ") || undefined,
      }) : children}

      {/* Hint or error message */}
      {(hint || error) && (
        <div
          id={messageId}
          style={{
            fontSize: 'var(--type-caption)',
            color: error ? 'var(--red)' : 'var(--text-muted)',
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
