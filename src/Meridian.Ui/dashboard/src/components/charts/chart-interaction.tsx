// Shared interaction layer for Meridian's stateless SVG charts. The charts render
// under `preserveAspectRatio="none"`, so horizontal screen space maps linearly to
// the point index — a transparent overlay rect can translate pointer/keyboard
// input into a bar/point index and emit crosshair + activation events, without the
// chart itself owning any state.
import type { KeyboardEvent, PointerEvent as ReactPointerEvent, MouseEvent as ReactMouseEvent } from "react";

/**
 * Map a pointer's client X to the nearest point index. Linear because the charts
 * stretch to fill their container (`preserveAspectRatio="none"`), so the overlay's
 * client width covers exactly the plotted index range.
 */
export function indexFromPointerX(clientX: number, rectLeft: number, rectWidth: number, count: number): number | null {
  if (count <= 0 || rectWidth <= 0) return null;
  const fraction = (clientX - rectLeft) / rectWidth;
  const clamped = Math.min(1, Math.max(0, fraction));
  return Math.round(clamped * (count - 1));
}

/**
 * Resolve the next crosshair index for a keyboard event, or null when the key is
 * not a navigation key. A null current index starts at the near edge for the
 * pressed direction.
 */
export function stepCrosshairIndex(current: number | null, key: string, count: number): number | null {
  if (count <= 0) return null;
  switch (key) {
    case "ArrowRight":
      return current == null ? 0 : Math.min(count - 1, current + 1);
    case "ArrowLeft":
      return current == null ? count - 1 : Math.max(0, current - 1);
    case "Home":
      return 0;
    case "End":
      return count - 1;
    default:
      return null;
  }
}

export interface ChartInteractionOverlayProps {
  /** Plot-area geometry in SVG user units. */
  x: number;
  y: number;
  width: number;
  height: number;
  /** Number of plotted points/bars. */
  count: number;
  /** Current crosshair index (for keyboard stepping + slider value). */
  crosshairIndex: number | null;
  onCrosshairChange?: (index: number | null) => void;
  onPointActivate?: (index: number) => void;
  ariaLabel: string;
}

/**
 * Transparent, focusable overlay that turns pointer and keyboard input over a
 * chart's plot area into crosshair-move and point-activate events. Rendered last
 * (on top) so it captures events; its fill is transparent so chart visuals show
 * through. Exposes slider semantics keyed to the point index.
 */
export function ChartInteractionOverlay({
  x,
  y,
  width,
  height,
  count,
  crosshairIndex,
  onCrosshairChange,
  onPointActivate,
  ariaLabel
}: ChartInteractionOverlayProps) {
  if (count <= 0 || (!onCrosshairChange && !onPointActivate)) {
    return null;
  }

  const indexFromEvent = (event: ReactPointerEvent<SVGRectElement> | ReactMouseEvent<SVGRectElement>): number | null => {
    const rect = event.currentTarget.getBoundingClientRect();
    return indexFromPointerX(event.clientX, rect.left, rect.width, count);
  };

  const handlePointerMove = (event: ReactPointerEvent<SVGRectElement>) => {
    if (!onCrosshairChange) return;
    const index = indexFromEvent(event);
    if (index != null) {
      onCrosshairChange(index);
    }
  };

  const handleClick = (event: ReactMouseEvent<SVGRectElement>) => {
    const index = indexFromEvent(event);
    if (index == null) return;
    onCrosshairChange?.(index);
    onPointActivate?.(index);
  };

  const handleKeyDown = (event: KeyboardEvent<SVGRectElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      if (crosshairIndex != null) {
        event.preventDefault();
        onPointActivate?.(crosshairIndex);
      }
      return;
    }
    const next = stepCrosshairIndex(crosshairIndex, event.key, count);
    if (next != null) {
      event.preventDefault();
      onCrosshairChange?.(next);
    }
  };

  return (
    <rect
      x={x}
      y={y}
      width={width}
      height={height}
      fill="transparent"
      style={{ cursor: "crosshair", outline: "none" }}
      tabIndex={0}
      role="slider"
      aria-label={ariaLabel}
      aria-valuemin={0}
      aria-valuemax={Math.max(0, count - 1)}
      aria-valuenow={crosshairIndex ?? undefined}
      onPointerMove={handlePointerMove}
      onPointerLeave={() => onCrosshairChange?.(null)}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
    />
  );
}
