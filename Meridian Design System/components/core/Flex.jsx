/**
 * Meridian Flex — Flexbox wrapper for directional layouts.
 * Use for rows by default; set vertical for columns.
 *
 * @example
 * <Flex gap="lg" justify="space-between">
 *   <div>Left</div>
 *   <div>Right</div>
 * </Flex>
 */
export function Flex({
  vertical = false,
  align = 'stretch',
  justify = 'flex-start',
  gap = 'md',
  wrap = false,
  children,
  className = '',
  style = {},
  ...rest
}) {
  const gapVar = gap && typeof gap === 'string' && gap.match(/^(xs|sm|md|lg|xl|2xl)$/)
    ? `var(--space-${gap})`
    : gap || '0';

  return (
    <div
      className={className}
      style={{
        display: 'flex',
        flexDirection: vertical ? 'column' : 'row',
        alignItems: align,
        justifyContent: justify,
        gap: gapVar,
        flexWrap: wrap ? 'wrap' : 'nowrap',
        ...style
      }}
      {...rest}
    >
      {children}
    </div>
  );
}

Flex.displayName = 'Flex';
