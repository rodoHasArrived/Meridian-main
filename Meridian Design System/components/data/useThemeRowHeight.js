// Meridian useThemeRowHeight — resolves the effective table row height from the density
// token (--theme-row-height: 26 terminal · 32 default/compact · 48 spacious) so virtualization math
// stays in sync with the CSS and with DensityToggle. Pass an explicit override to opt out;
// otherwise the hook re-reads the token whenever body[data-theme-density] changes.
import React from "react";

export const DEFAULT_THEME_ROW_HEIGHT = 32;

function readRowHeight() {
  if (typeof document === "undefined" || !document.body) return DEFAULT_THEME_ROW_HEIGHT;
  const v = parseFloat(getComputedStyle(document.body).getPropertyValue("--theme-row-height"));
  return Number.isFinite(v) && v > 0 ? v : DEFAULT_THEME_ROW_HEIGHT;
}

export function useThemeRowHeight(override) {
  const [h, setH] = React.useState(() => (override != null ? override : readRowHeight()));
  React.useEffect(() => {
    if (override != null) return;
    setH(readRowHeight());
    const mo = new MutationObserver(() => setH(readRowHeight()));
    mo.observe(document.body, { attributes: true, attributeFilter: ["data-theme-density"] });
    return () => mo.disconnect();
  }, [override]);
  return override != null ? override : h;
}
