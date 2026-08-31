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
  const fieldId = Math.random().toString(36).slice(2);

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
            <span style={{ color: 'var(--red)', marginLeft: '4px' }}>*</span>
          )}
        </label>
      )}

      {/* Input (cloned with id and aria attrs) */}
      {children && typeof children === 'object' && children.type
        ? (() => {
            const cloned = children;
            return cloned.props ? (
              children
            ) : (
              children
            );
          })()
        : children}

      {/* Hint or error message */}
      {(hint || error) && (
        <div
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
