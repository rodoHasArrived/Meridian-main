import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "@/app";
import { applyAppearance, readStoredAppearance } from "@/lib/theme";
import { applyDensity, readStoredDensity } from "@/lib/density";
import "@/styles/index.css";
import "@/styles/workspace-surface.css";
import "@/styles/command-palette.css";
import "@/styles/workstation-refinements.css";
import "@/styles/density.css";
import "@/styles/print.css";
import "@/styles/forced-colors.css";

applyAppearance(readStoredAppearance());
applyDensity(readStoredDensity());

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <BrowserRouter basename="/workstation">
      <App />
    </BrowserRouter>
  </React.StrictMode>
);
