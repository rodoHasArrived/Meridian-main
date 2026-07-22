import { describe, expect, it, vi } from "vitest";
import {
  companionPaneHref,
  isCompanionPaneRoute,
  openCompanionPane,
  resolveCompanionPane
} from "@/lib/companion-pane/pane-window";

describe("companion pane routing", () => {
  it("builds hrefs and recognizes pane routes", () => {
    expect(companionPaneHref("watchlist")).toBe("/panes/watchlist");
    expect(isCompanionPaneRoute("/panes/watchlist")).toBe(true);
    expect(isCompanionPaneRoute("/data/watchlist")).toBe(false);
  });

  it("resolves known panes and rejects unknown or non-pane paths", () => {
    expect(resolveCompanionPane("/panes/watchlist")?.title).toBe("Watchlist");
    expect(resolveCompanionPane("/panes/live-quotes/")?.id).toBe("live-quotes");
    expect(resolveCompanionPane("/panes/nope")).toBeNull();
    expect(resolveCompanionPane("/data/quotes")).toBeNull();
  });

  it("opens a pane window with noopener and never inspects the return value", () => {
    // window.open with noopener returns null even on success — the helper must not
    // depend on the return value.
    const open = vi.fn().mockReturnValue(null);
    openCompanionPane("live-quotes", { open: open as unknown as Window["open"] });
    expect(open).toHaveBeenCalledWith("/panes/live-quotes", "_blank", "noopener,noreferrer");
  });
});
