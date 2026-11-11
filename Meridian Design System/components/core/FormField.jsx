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
}) {
  const generatedId = React.useId();
  const control = React.isValidElement(children) ? children : null;
  const fieldId = control?.props.id || generatedId;
  const helpId = `${fieldId}-help`;
  const describedBy = [...new Set([
    ...(control?.props["aria-describedby"] || "").split(/\s+/).filter(Boolean),
    ...((hint || error) ? [helpId] : []),
  ])].join(" ");

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

      {/* Keep the visible label and supporting message attached to the control. */}
      {control ? React.cloneElement(control, {
        id: fieldId,
        ...(describedBy ? { "aria-describedby": describedBy } : {}),
        ...(error ? { "aria-invalid": true } : {}),
        ...(required ? { "aria-required": true } : {}),
      }) : children}

      {/* Hint or error message */}
      {(hint || error) && (
        <div id={helpId}
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
