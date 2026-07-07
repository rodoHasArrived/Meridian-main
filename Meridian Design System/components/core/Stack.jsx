/**
 * Meridian Stack — Semantic directional flex wrapper.
 * Preferred over Flex when direction is the primary concern.
 *
 * @example
 * <Stack direction="vertical" spacing="lg">
 *   <div>Top</div>
 *   <div>Middle</div>
 *   <div>Bottom</div>
 * </Stack>
 */
export function Stack({
  direction = 'vertical',
  spacing = 'md',
  align = 'stretch',
  justify = 'flex-start',
  children,
  className = '',
  style = {},
  ...rest
}) {
  const isVertical = direction === 'vertical' || direction === 'column';
  const spacingVar = spacing && typeof spacing === 'string' && spacing.match(/^(xs|sm|md|lg|xl|2xl)$/)
    ? `var(--space-${spacing})`
    : spacing || '0';

  return (
    <div
      className={className}
      style={{
        display: 'flex',
        flexDirection: isVertical ? 'column' : 'row',
        gap: spacingVar,
        alignItems: align,
        justifyContent: justify,
        ...style
      }}
      {...rest}
    >
      {children}
    </div>
  );
}

Stack.displayName = 'Stack';
