/**
 * Meridian Gauge — Circular progress indicator for metrics.
 * Shows value 0–100 as a circular arc with optional center label.
 *
 * @example
 * <Gauge value={75} label="CPU" color="var(--orange)" />
 */
export function Gauge({
  value = 0,
  max = 100,
  label,
  size = 120,
  color = 'var(--accent)',
  thickness = 8,
  showValue = true,
  className = '',
  style = {},
}) {
  const percent = Math.min(Math.max(value / max, 0), 1);
  const radius = (size - thickness) / 2;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference * (1 - percent);

  const cx = size / 2;
  const cy = size / 2;

  return (
    <div
      className={className}
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 'var(--space-md)',
        ...style
      }}
    >
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        style={{ transform: 'rotate(-90deg)' }}
      >
        {/* Background circle */}
        <circle
          cx={cx}
          cy={cy}
          r={radius}
          fill="none"
          stroke="var(--border)"
          strokeWidth={thickness}
        />
        {/* Progress arc */}
        <circle
          cx={cx}
          cy={cy}
          r={radius}
          fill="none"
          stroke={color}
          strokeWidth={thickness}
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          strokeLinecap="round"
          style={{
            transition: 'stroke-dashoffset 300ms ease-out',
          }}
        />
      </svg>

      {/* Center label + value */}
      {(label || showValue) && (
        <div style={{ textAlign: 'center' }}>
          {showValue && (
            <div
              style={{
                fontSize: 'var(--type-metric)',
                fontWeight: 700,
                color: 'var(--text-primary)',
                fontFamily: 'var(--font-data)',
              }}
            >
              {Math.round(value)}%
            </div>
          )}
          {label && (
            <div
              style={{
                fontSize: 'var(--type-label)',
                color: 'var(--text-muted)',
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: 'var(--letter-spacing-label)',
              }}
            >
              {label}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

Gauge.displayName = 'Gauge';
