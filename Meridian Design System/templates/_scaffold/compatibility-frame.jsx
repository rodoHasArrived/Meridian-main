// Retained template URLs delegate to the documented current workstation view.
// The frame keeps the old address usable without reviving obsolete namespaces,
// table contracts, or a second copy of a financial workflow.
function MeridianTemplateCompatibility({ target, title, theme, density, environment }) {
  const src = React.useMemo(() => {
    const url = new URL(target, window.location.href);
    if (url.origin !== window.location.origin) throw new Error("Compatibility targets must stay in this package");
    const current = new URL(window.location.href);
    current.searchParams.forEach((value, key) => url.searchParams.set(key, value));
    for (const [key, value] of Object.entries({ theme, density, environment })) {
      if (value != null && !url.searchParams.has(key)) url.searchParams.set(key, value);
    }
    url.hash = current.hash;
    return url.href;
  }, [target, theme, density, environment]);
  return <iframe data-meridian-compatibility-target={target} src={src} title={title}
    style={{ display: "block", border: 0, width: "100%", height: "100vh", minHeight: 480 }} />;
}
window.MeridianTemplateCompatibility = MeridianTemplateCompatibility;
