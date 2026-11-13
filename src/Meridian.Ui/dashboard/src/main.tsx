import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "@/app";
import { applyDensity, readStoredDensity } from "@/lib/density";
import { applyAppearance, readStoredAppearance } from "@/lib/theme";
import { isDesignSystemWorkbenchEntry } from "@/dev/design-system-workbench-entry";
import "@/design-system/fonts.css";
import "@/styles/index.css";
import "@/styles/workspace-surface.css";
import "@/styles/command-palette.css";
import "@/styles/workstation-refinements.css";
import "@/styles/print.css";

// Select the fixture-only preview before App mounts its data-reading providers.
// Vite removes the guarded import from production builds.
const Workbench = import.meta.env.DEV && isDesignSystemWorkbenchEntry(
  window.location.pathname, import.meta.env.DEV, import.meta.env.VITE_MERIDIAN_DEV_MODE
) ? React.lazy(() => import("@/dev/design-system-workbench").then((module) => ({ default: module.DesignSystemWorkbench }))) : null;

if (!Workbench) {
  applyAppearance(readStoredAppearance());
  // Re-apply operator density on load, including when Settings is not mounted.
  applyDensity(readStoredDensity());
}

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <BrowserRouter basename="/workstation">
      {Workbench ? <React.Suspense fallback={<main aria-busy="true">Loading native component workbench...</main>}><Workbench /></React.Suspense> : <App />}
    </BrowserRouter>
  </React.StrictMode>
);
