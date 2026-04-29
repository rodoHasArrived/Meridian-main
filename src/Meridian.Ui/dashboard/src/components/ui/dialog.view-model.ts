import { useCallback, useEffect, useRef, type MouseEvent as ReactMouseEvent } from "react";

const focusableSelector = [
  "a[href]",
  "button:not([disabled])",
  "textarea:not([disabled])",
  "input:not([disabled]):not([type='hidden'])",
  "select:not([disabled])",
  "[contenteditable='true']",
  "[tabindex]:not([tabindex='-1'])"
].join(",");

interface DialogInteractionOptions {
  open: boolean;
  onOpenChange?: (open: boolean) => void;
}

export function useDialogInteractionViewModel({ open, onOpenChange }: DialogInteractionOptions) {
  const overlayRef = useRef<HTMLDivElement>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);

  const requestClose = useCallback(() => {
    onOpenChange?.(false);
  }, [onOpenChange]);

  const handleDocumentKeyDown = useCallback((event: KeyboardEvent) => {
    if (!open) {
      return;
    }

    if (shouldCloseDialogFromKey(event.key)) {
      event.preventDefault();
      requestClose();
      return;
    }

    if (event.key !== "Tab") {
      return;
    }

    const nextFocus = resolveDialogTabTarget(overlayRef.current, getActiveElement(), event.shiftKey);
    if (!nextFocus) {
      return;
    }

    event.preventDefault();
    nextFocus.focus();
  }, [open, requestClose]);

  useEffect(() => {
    if (!open || typeof window === "undefined") {
      return undefined;
    }

    window.addEventListener("keydown", handleDocumentKeyDown, true);
    return () => window.removeEventListener("keydown", handleDocumentKeyDown, true);
  }, [handleDocumentKeyDown, open]);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    restoreFocusRef.current = getActiveElement();

    const cancelFocus = scheduleFocus(() => {
      resolveInitialDialogFocus(overlayRef.current, getActiveElement())?.focus();
    });

    return () => {
      cancelFocus();

      const restoreFocus = restoreFocusRef.current;
      restoreFocusRef.current = null;

      if (restoreFocus?.isConnected) {
        restoreFocus.focus();
      }
    };
  }, [open]);

  const handleBackdropMouseDown = useCallback((event: ReactMouseEvent<HTMLDivElement>) => {
    if (shouldCloseDialogFromBackdrop(event.target, event.currentTarget)) {
      requestClose();
    }
  }, [requestClose]);

  return {
    overlayRef,
    handleBackdropMouseDown
  };
}

export function shouldCloseDialogFromKey(key: string): boolean {
  return key === "Escape";
}

export function shouldCloseDialogFromBackdrop(target: EventTarget, currentTarget: EventTarget): boolean {
  return target === currentTarget;
}

export function getFocusableDialogElements(root: HTMLElement | null): HTMLElement[] {
  if (!root) {
    return [];
  }

  return Array.from(root.querySelectorAll<HTMLElement>(focusableSelector))
    .filter(isDialogElementFocusable);
}

export function resolveInitialDialogFocus(
  root: HTMLElement | null,
  activeElement: Element | null = getActiveElement()
): HTMLElement | null {
  if (!root) {
    return null;
  }

  if (isHTMLElement(activeElement) && root.contains(activeElement) && activeElement !== root) {
    return activeElement;
  }

  const preferred = root.querySelector<HTMLElement>("[data-dialog-autofocus], [autofocus]");
  if (preferred && isDialogElementFocusable(preferred)) {
    return preferred;
  }

  return getFocusableDialogElements(root)[0]
    ?? root.querySelector<HTMLElement>("[role='dialog']")
    ?? root;
}

export function resolveDialogTabTarget(
  root: HTMLElement | null,
  activeElement: Element | null,
  shiftKey: boolean
): HTMLElement | null {
  const focusableElements = getFocusableDialogElements(root);

  if (focusableElements.length === 0) {
    return resolveInitialDialogFocus(root, activeElement);
  }

  const first = focusableElements[0];
  const last = focusableElements[focusableElements.length - 1];

  if (!isHTMLElement(activeElement) || !focusableElements.includes(activeElement)) {
    return shiftKey ? last : first;
  }

  if (shiftKey && activeElement === first) {
    return last;
  }

  if (!shiftKey && activeElement === last) {
    return first;
  }

  return null;
}

function getActiveElement(): HTMLElement | null {
  if (typeof document === "undefined") {
    return null;
  }

  return isHTMLElement(document.activeElement) ? document.activeElement : null;
}

function isDialogElementFocusable(element: HTMLElement): boolean {
  if (element.hasAttribute("disabled") || element.getAttribute("aria-hidden") === "true") {
    return false;
  }

  return element.tabIndex >= 0 || element.hasAttribute("autofocus") || element.hasAttribute("data-dialog-autofocus");
}

function isHTMLElement(value: unknown): value is HTMLElement {
  return typeof HTMLElement !== "undefined" && value instanceof HTMLElement;
}

function scheduleFocus(callback: () => void): () => void {
  if (typeof window === "undefined") {
    return () => undefined;
  }

  if (typeof window.requestAnimationFrame === "function") {
    const frame = window.requestAnimationFrame(callback);
    return () => window.cancelAnimationFrame(frame);
  }

  const timeout = window.setTimeout(callback, 0);
  return () => window.clearTimeout(timeout);
}
