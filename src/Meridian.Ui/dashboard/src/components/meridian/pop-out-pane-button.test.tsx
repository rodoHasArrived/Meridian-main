import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { PopOutPaneButton } from "@/components/meridian/pop-out-pane-button";

describe("PopOutPaneButton", () => {
  afterEach(() => cleanup());

  it("opens the pane via the injected opener with noopener", () => {
    const open = vi.fn().mockReturnValue(null);
    render(<PopOutPaneButton paneId="watchlist" opener={{ open: open as unknown as Window["open"] }} />);

    fireEvent.click(screen.getByRole("button", { name: "Pop out in a companion window" }));
    expect(open).toHaveBeenCalledWith("/panes/watchlist", "_blank", "noopener,noreferrer");
  });

  it("always renders a persistent fallback link that opens a new tab safely", () => {
    render(<PopOutPaneButton paneId="live-quotes" />);
    const link = screen.getByRole("link", { name: "Open Live quotes in a new tab" });
    expect(link).toHaveAttribute("href", "/panes/live-quotes");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });
});
