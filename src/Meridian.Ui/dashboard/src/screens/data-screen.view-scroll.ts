import { useLayoutEffect, type RefObject } from "react";
import { useLocation, useNavigationType } from "react-router-dom";

type ScrollPosition = { top: number; left: number };
// Presentation state only. Bound the history cache; never retain records or authority.
const positions = new Map<string, Record<string, ScrollPosition>>();

/** Restore each Data history entry after its asynchronous rows have mounted. */
export function useDataViewScroll(host: RefObject<HTMLDivElement | null>, ready: boolean) {
  const location = useLocation();
  const navigationType = useNavigationType();
  const entry = `${location.key}|${location.pathname}${location.search}${location.hash}`;
  useLayoutEffect(() => {
    const root = host.current;
    if (!root || !ready) return;
    const viewport = root.closest<HTMLElement>(".workbench-scroll");
    const scrollables = [
      ...(viewport ? [{ id: "workspace", element: viewport }] : []),
      ...Array.from(root.querySelectorAll<HTMLElement>(".dense-data-table-wrap, .workspace-workbench-shell-inspector")).map((element, index) => ({ id: `${element.getAttribute("aria-label") ?? element.className}:${index}`, element }))
    ];
    const retained = positions.get(entry);
    if (navigationType === "POP" && retained) {
      scrollables.forEach(({ id, element }) => {
        const position = retained[id];
        if (!position) return;
        element.scrollTop = position.top;
        element.scrollLeft = position.left;
        element.dispatchEvent(new Event("scroll"));
      });
    }
    const remember = () => {
      positions.set(entry, Object.fromEntries(scrollables.map(({ id, element }) => [id, { top: element.scrollTop, left: element.scrollLeft }])));
      if (positions.size > 100) positions.delete(positions.keys().next().value!);
    };
    remember();
    scrollables.forEach(({ element }) => element.addEventListener("scroll", remember, { passive: true }));
    return () => scrollables.forEach(({ element }) => element.removeEventListener("scroll", remember));
  }, [entry, host, navigationType, ready]);
}
