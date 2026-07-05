import { Suspense, lazy, useEffect, useState, type ComponentType } from "react";
import { useLocation } from "react-router-dom";
import { applyAppearance, readStoredAppearance, writeStoredAppearance } from "@/lib/theme";
import {
  createCompanionBridge,
  openCompanionChannel,
  type BroadcastChannelLike
} from "@/lib/companion-pane/chrome-bridge";
import { resolveCompanionPane } from "@/lib/companion-pane/pane-window";
import "@/styles/companion-pane.css";

const WatchlistScreen = lazy(() =>
  import("@/screens/watchlist-screen").then((module) => ({ default: module.WatchlistScreen }))
);
const LiveQuotesScreen = lazy(() =>
  import("@/screens/live-quotes-screen").then((module) => ({ default: module.LiveQuotesScreen }))
);

const PANE_COMPONENTS: Record<string, ComponentType> = {
  watchlist: WatchlistScreen,
  "live-quotes": LiveQuotesScreen
};

export interface CompanionPaneWindowProps {
  /** Injectable channel opener for tests. */
  openChannel?: (name?: string) => BroadcastChannelLike | null;
}

/**
 * Chrome-less host for a companion pane. Rendered instead of the full app shell
 * for `/panes/*` routes: no masthead, no navigation, no workstation data
 * loading — just the pane surface. Applies the persisted appearance on load and
 * subscribes to the companion bridge so a theme change or session end in the main
 * window propagates here live.
 */
export function CompanionPaneWindow({ openChannel = openCompanionChannel }: CompanionPaneWindowProps = {}) {
  const { pathname } = useLocation();
  const pane = resolveCompanionPane(pathname);
  const [sessionEnded, setSessionEnded] = useState(false);

  // Same-origin localStorage already holds the chosen appearance; apply it so the
  // pane opens in the same theme as the main window.
  useEffect(() => {
    applyAppearance(readStoredAppearance());
  }, []);

  useEffect(() => {
    const channel = openChannel();
    if (!channel) {
      return;
    }
    const bridge = createCompanionBridge(channel);
    const unsubscribe = bridge.subscribe((message) => {
      if (message.type === "appearance") {
        writeStoredAppearance(message.appearance);
        applyAppearance(message.appearance);
      } else if (message.type === "session-expired") {
        setSessionEnded(true);
      }
      // `scope` messages are reserved for scope-aware panes; the exemplar panes
      // (watchlist, live quotes) do not filter by operating scope.
    });
    return () => {
      unsubscribe();
      bridge.close();
    };
  }, [openChannel]);

  if (!pane) {
    return (
      <div className="companion-pane" role="main" aria-label="Unknown companion pane">
        <div className="companion-pane-empty">
          This companion pane is not available. Open it from the main workstation window.
        </div>
      </div>
    );
  }

  const PaneComponent = PANE_COMPONENTS[pane.id];

  return (
    <div className="companion-pane" role="main" aria-label={pane.description}>
      <header className="companion-pane-header">
        <span className="companion-pane-title">{pane.title}</span>
        <span className="companion-pane-badge">Companion pane</span>
      </header>
      {sessionEnded ? (
        <div role="alert" className="companion-pane-session-ended">
          The main workstation window is no longer available. Reopen this pane from the workstation.
        </div>
      ) : null}
      <div className="companion-pane-body">
        <Suspense fallback={<div className="companion-pane-loading">Loading {pane.title}…</div>}>
          <PaneComponent />
        </Suspense>
      </div>
    </div>
  );
}
