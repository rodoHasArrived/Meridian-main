// Main-window side of the companion bridge. Lazily opens a single shared channel
// and posts updates to any open panes. A no-op when BroadcastChannel is
// unavailable (e.g. SSR or older environments), so callers can broadcast
// unconditionally.
import {
  createCompanionBridge,
  openCompanionChannel,
  type CompanionBridge,
  type CompanionBridgeMessage
} from "./chrome-bridge";

let cachedBridge: CompanionBridge | null | undefined;

function getOpenerBridge(): CompanionBridge | null {
  if (cachedBridge === undefined) {
    const channel = openCompanionChannel();
    cachedBridge = channel ? createCompanionBridge(channel) : null;
  }
  return cachedBridge;
}

/** Broadcast a companion-bridge message from the main workstation window. */
export function broadcastCompanionState(message: CompanionBridgeMessage): void {
  getOpenerBridge()?.post(message);
}

/** Test seam: reset the cached opener bridge. */
export function resetOpenerBridgeForTests(): void {
  cachedBridge?.close();
  cachedBridge = undefined;
}
