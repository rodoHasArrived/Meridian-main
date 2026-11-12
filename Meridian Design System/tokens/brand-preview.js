/* Demo-only brand catalogue. All swatches resolve the canonical theme rules. */
(() => {
  const definitions = [
    ["", "Default (Copper)"], ["steel", "Steel"], ["indigo", "Indigo"],
    ["emerald", "Emerald"], ["rose", "Rose"], ["slate", "Slate"],
    ["cyan", "Cyan"], ["amber", "Amber"]
  ];
  function capture() {
    const root = document.documentElement;
    const prior = root.getAttribute("data-brand");
    try {
      return definitions.map(([id, label]) => {
        if (id) root.setAttribute("data-brand", id); else root.removeAttribute("data-brand");
        return { id, label, color: getComputedStyle(root).getPropertyValue("--accent").trim() };
      });
    } finally {
      if (prior === null) root.removeAttribute("data-brand"); else root.setAttribute("data-brand", prior);
    }
  }
  window.MeridianBrandPreview = { capture };
})();
