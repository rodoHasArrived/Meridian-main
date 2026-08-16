/// <reference types="vite/client" />

/**
 * The application version, injected by `define` in vite.config.ts at build time.
 *
 * Declared here rather than imported from package.json so the manifest stays out of the
 * client module graph — see #2684.
 */
declare const __APP_VERSION__: string;
