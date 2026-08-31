// Meridian useThemeRowHeight — resolves the effective table row height from the density
// token (--theme-row-height: 32 compact · 40 cozy · 48 spacious) so virtualization math
// stays in sync with the CSS and with DensityToggle. Pass an explicit override to opt out;
// otherwise the hook re-reads the token whenever body[data-theme-density] changes.
import React from "react";

function readRowHeight() {
  if (typeof document === "undefined" || !document.body) return 40;
  const v = parseFloat(getComputedStyle(document.body).getPropertyValue("--theme-row-height"));
  return Number.isFinite(v) && v > 0 ? v : 40;
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
