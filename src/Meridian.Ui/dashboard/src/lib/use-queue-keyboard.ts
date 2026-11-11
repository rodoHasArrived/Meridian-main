import { useCallback, type KeyboardEvent } from "react";

export interface QueueKeyboardAction {
  key: "a" | "r" | "d";
  label: string;
  enabled: boolean;
  run: () => void;
}

/** Attach to the queue container's capture handler. Never captures typing or modal commands. */
export function useQueueKeyboard<T>({ rows, selectedId, getId, onSelect, actions = [] }: {
  rows: readonly T[];
  selectedId: string | null;
  getId: (row: T) => string;
  onSelect: (row: T) => void;
  actions?: readonly QueueKeyboardAction[];
}) {
  return useCallback((event: KeyboardEvent<HTMLElement>) => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey
      || event.nativeEvent.isComposing || !(event.target instanceof HTMLElement)
      || event.target.isContentEditable
      || event.target.closest("input,textarea,select,button,a,summary,[contenteditable],[role=textbox],[role=combobox],[role=listbox],[role=dialog],[role=menu],[aria-modal=true]")) return;
    const rowElement = event.target.closest<HTMLElement>("[data-dense-row-id]");
    if (!rowElement || !event.currentTarget.contains(rowElement)) return;
    const index = rows.findIndex((row) => getId(row) === (rowElement.dataset.denseRowId ?? selectedId));
    if (index < 0) return;
    if (event.key === "j" || event.key === "k") {
      event.preventDefault();
      event.stopPropagation();
      const next = rows[Math.min(rows.length - 1, Math.max(0, index + (event.key === "j" ? 1 : -1)))];
      if (!next) return;
      onSelect(next);
      const nextId = getId(next);
      const container = event.currentTarget;
      requestAnimationFrame(() => {
        const target = [...container.querySelectorAll<HTMLElement>("[data-dense-row-id]")]
          .find((element) => element.dataset.denseRowId === nextId);
        target?.focus();
        target?.scrollIntoView?.({ block: "nearest" });
      });
      return;
    }
    const action = actions.find((candidate) => candidate.key === event.key && candidate.enabled);
    if (action) {
      event.preventDefault();
      event.stopPropagation();
      action.run();
    }
  }, [rows, selectedId, getId, onSelect, actions]);
}
