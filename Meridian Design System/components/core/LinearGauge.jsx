/**
 * Meridian LinearGauge — Horizontal progress indicator (like ProgressBar but with label).
 * Shows value 0–100 as a filled bar with optional label and value display.
 *
 * @example
 * <LinearGauge value={65} label="Memory" />
 */
export function LinearGauge({
  value = 0,
  max = 100,
  label,
  showValue = true,
  color = 'var(--accent)',
  height = 8,
  className = '',
  style = {},
}) {
  const percent = Math.min(Math.max(value / max, 0), 1) * 100;

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
      {/* Label + value row */}
      {(label || showValue) && (
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'baseline',
          }}
        >
          {label && (
            <div
              style={{
                fontSize: 'var(--type-label)',
                fontWeight: 600,
                color: 'var(--text-muted)',
                textTransform: 'uppercase',
                letterSpacing: 'var(--letter-spacing-label)',
              }}
            >
              {label}
            </div>
          )}
          {showValue && (
            <div
              style={{
                fontSize: 'var(--type-data-value)',
                fontWeight: 600,
                color: 'var(--text-primary)',
                fontFamily: 'var(--font-data)',
              }}
            >
              {Math.round(percent)}%
            </div>
          )}
        </div>
      )}

      {/* Bar */}
      <div
        style={{
          position: 'relative',
          width: '100%',
          height,
          backgroundColor: 'var(--border)',
          borderRadius: 'var(--radius-card)',
          overflow: 'hidden',
        }}
      >
        <div
          style={{
            position: 'absolute',
            left: 0,
            top: 0,
            height: '100%',
            width: `${percent}%`,
            backgroundColor: color,
            borderRadius: 'var(--radius-card)',
            transition: 'width 300ms ease-out',
          }}
        />
      </div>
    </div>
  );
}

LinearGauge.displayName = 'LinearGauge';
