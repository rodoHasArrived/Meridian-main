import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "@/app";
import { applyDensity, readStoredDensity } from "@/lib/density";
import { applyAppearance, readStoredAppearance } from "@/lib/theme";
import "@/styles/index.css";
import "@/styles/workspace-surface.css";
import "@/styles/command-palette.css";
import "@/styles/workstation-refinements.css";

applyAppearance(readStoredAppearance());
// Re-apply the operator's display density on load; before this it only took effect while the
// Settings screen (which hosts the density control) was mounted.
applyDensity(readStoredDensity());

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <BrowserRouter basename="/workstation">
      <App />
    </BrowserRouter>
  </React.StrictMode>
);
