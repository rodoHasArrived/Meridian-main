// Theme comparisons need separate documents: canonical theme tokens bind at :root.
// An arbitrary nested .dark class cannot change those resolved aliases.
(() => {
  const styles = [...document.querySelectorAll('link[rel="stylesheet"], style')]
    .map((element) => element.outerHTML).join("\n");
  const base = document.baseURI.replaceAll('"', "&quot;");
  document.querySelectorAll("[data-theme-specimen]").forEach((specimen) => {
    const theme = specimen.dataset.themeSpecimen;
    if (theme !== "light" && theme !== "dark") return;
    const frame = document.createElement("iframe");
    frame.title = `${theme === "dark" ? "Dark" : "Light"} theme specimen`;
    frame.style.cssText = "width:100%;border:0;display:block;min-width:0;";
    frame.srcdoc = `<!doctype html><html lang="en" data-theme="${theme}"><head>
      <meta charset="utf-8"><title>${frame.title}</title><base href="${base}">${styles}
      <style>html,body{margin:0!important;padding:0!important;display:block!important;min-width:0!important;}body{background:var(--bg);}</style>
      </head><body>${specimen.outerHTML}</body></html>`;
    frame.addEventListener("load", () => {
      const content = frame.contentDocument;
      if (!content) return;
      const fit = () => { frame.style.height = `${content.body.scrollHeight + 2}px`; };
      new ResizeObserver(fit).observe(content.body);
      fit();
    });
    specimen.replaceWith(frame);
  });
})();
