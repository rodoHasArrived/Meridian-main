import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { APPEARANCE_STORAGE_KEY } from "@/lib/theme";
import type { BroadcastChannelLike } from "@/lib/companion-pane/chrome-bridge";
import { CompanionPaneWindow } from "@/components/meridian/companion-pane-window";

vi.mock("@/screens/watchlist-screen", () => ({
  WatchlistScreen: () => <div data-testid="watchlist-screen">watchlist</div>
}));
vi.mock("@/screens/live-quotes-screen", () => ({
  LiveQuotesScreen: () => <div data-testid="live-quotes-screen">live quotes</div>
}));

class FakeChannel implements BroadcastChannelLike {
  private listeners = new Set<(event: MessageEvent) => void>();
  postMessage() {}
  addEventListener(_type: "message", listener: (event: MessageEvent) => void) {
    this.listeners.add(listener);
  }
  removeEventListener(_type: "message", listener: (event: MessageEvent) => void) {
    this.listeners.delete(listener);
  }
  close() {}
  emit(data: unknown) {
    this.listeners.forEach((listener) => listener({ data } as MessageEvent));
  }
}

function renderPane(path: string, channel: FakeChannel) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <CompanionPaneWindow openChannel={() => channel} />
    </MemoryRouter>
  );
}

describe("CompanionPaneWindow", () => {
  afterEach(() => {
    cleanup();
    window.localStorage.clear();
  });

  it("renders the resolved pane surface chrome-less", async () => {
    renderPane("/panes/watchlist", new FakeChannel());
    expect(screen.getByRole("main", { name: "Watchlist companion pane" })).toBeInTheDocument();
    expect(await screen.findByTestId("watchlist-screen")).toBeInTheDocument();
    // chrome-less: none of the shell's workspace navigation is rendered
    expect(screen.queryByRole("navigation")).toBeNull();
  });

  it("shows a message for an unknown pane", () => {
    renderPane("/panes/does-not-exist", new FakeChannel());
    expect(screen.getByText(/not available/i)).toBeInTheDocument();
  });

  it("applies a live appearance change broadcast from the main window", () => {
    const channel = new FakeChannel();
    renderPane("/panes/watchlist", channel);

    channel.emit({ type: "appearance", appearance: "dark" });
    expect(window.localStorage.getItem(APPEARANCE_STORAGE_KEY)).toBe("dark");
  });

  it("surfaces a session-ended alert when the main window goes away", async () => {
    const channel = new FakeChannel();
    renderPane("/panes/watchlist", channel);
    expect(screen.queryByRole("alert")).toBeNull();

    channel.emit({ type: "session-expired" });
    expect(await screen.findByRole("alert")).toHaveTextContent(/no longer available/i);
  });
});
