import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "@/app";
import { applyAppearance, readStoredAppearance } from "@/lib/theme";
import "@/styles/index.css";
import "@/styles/workspace-surface.css";
import "@/styles/command-palette.css";

applyAppearance(readStoredAppearance());

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <BrowserRouter basename="/workstation">
      <App />
    </BrowserRouter>
  </React.StrictMode>
);
