// Scoped-stylesheet injection helper for the Concrete design-system components.
//
// Each native component injects its own stylesheet once (keyed by a `data-mds`
// attribute), mirroring the handoff bundle's per-component CSS injection. This helper
// centralises the idempotency guard so every component doesn't re-implement the
// `injected` boolean + document-guard boilerplate. Safe to call on every render and
// in non-DOM (SSR / unit-test import) environments.

const injectedKeys = new Set<string>();

/**
 * Inject `css` into `<head>` exactly once per `key`. No-op when there is no `document`
 * (e.g. server render) or when the key has already been injected.
 */
export function injectStyle(key: string, css: string): void {
  if (typeof document === "undefined") return;
  if (injectedKeys.has(key)) return;
  injectedKeys.add(key);
  const el = document.createElement("style");
  el.setAttribute("data-mds", key);
  el.textContent = css;
  document.head.appendChild(el);
}
