/**
 * Meridian Grid — CSS Grid wrapper for responsive layouts.
 * Automatically tracks light/dark theme via CSS vars.
 *
 * @example
 * <Grid cols={3} gap="lg">
 *   <div>Item 1</div>
 *   <div>Item 2</div>
 *   <div>Item 3</div>
 * </Grid>
 */
export function Grid({
  cols = 2,
  rows = 'auto',
  gap = 'md',
  children,
  className = '',
  style = {},
  ...rest
}) {
  const gapVar = gap && typeof gap === 'string' && gap.match(/^(xs|sm|md|lg|xl|2xl)$/)
    ? `var(--space-${gap})`
    : gap || '0';

  const colsStr = cols ? `repeat(${cols}, 1fr)` : 'auto';

  return (
    <div
      className={className}
      style={{
        display: 'grid',
        gridTemplateColumns: colsStr,
        gridTemplateRows: rows,
        gap: gapVar,
        ...style
      }}
      {...rest}
    >
      {children}
    </div>
  );
}

Grid.displayName = 'Grid';
