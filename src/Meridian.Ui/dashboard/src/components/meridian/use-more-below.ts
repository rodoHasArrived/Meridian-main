// "More below" cue for scrollable side panels (UX Phase F). A fixed-height detail panel that hides
// its overflow made the hidden fields look absent; this tracks whether a scroll container still has
// content beneath its visible area so the panel can say so.
import { useEffect, useState, type RefObject } from "react";

/** Pixels of slack so sub-pixel rounding does not keep the cue visible at the very bottom. */
const BOTTOM_SLACK_PX = 4;

export function hasMoreBelow(metrics: { scrollHeight: number; clientHeight: number; scrollTop: number }): boolean {
  return metrics.scrollHeight - metrics.clientHeight - metrics.scrollTop > BOTTOM_SLACK_PX;
}

/** True while `ref`'s element has content below its visible area; updates on scroll, resize and content changes. */
export function useMoreBelow(ref: RefObject<HTMLElement | null>, enabled = true): boolean {
  const [moreBelow, setMoreBelow] = useState(false);

  useEffect(() => {
    const element = ref.current;
    if (!enabled || !element) {
      setMoreBelow(false);
      return;
    }
    const update = () => setMoreBelow(hasMoreBelow(element));
    update();
    element.addEventListener("scroll", update, { passive: true });
    const resize = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(update);
    resize?.observe(element);
    const mutations = typeof MutationObserver === "undefined" ? null : new MutationObserver(update);
    mutations?.observe(element, { childList: true, subtree: true, characterData: true });
    return () => {
      element.removeEventListener("scroll", update);
      resize?.disconnect();
      mutations?.disconnect();
    };
  }, [ref, enabled]);

  return moreBelow;
}
