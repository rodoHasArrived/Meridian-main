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

export interface DialogContentViewModel {
  role: "dialog";
  ariaModal: true;
  tabIndex: number;
  className: string;
}

export interface DialogCloseButtonOptions {
  label?: string;
  disabled?: boolean;
  disabledReason?: string | null;
}

export interface DialogCloseButtonViewModel {
  type: "button";
  ariaLabel: string;
  title: string;
  disabled: boolean;
  iconAriaHidden: true;
  className: string;
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

export function buildDialogContentViewModel(tabIndex = -1): DialogContentViewModel {
  return {
    role: "dialog",
    ariaModal: true,
    tabIndex,
    className: [
      "w-full max-w-lg",
      "max-h-[calc(100dvh-2rem)] overflow-y-auto overscroll-contain",
      "rounded-[2px] border border-border bg-card p-5 text-card-foreground shadow-[0_2px_6px_rgba(0,0,0,0.18)]",
      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
    ].join(" ")
  };
}

export function buildDialogCloseButtonViewModel({
  label = "Close dialog",
  disabled = false,
  disabledReason = null
}: DialogCloseButtonOptions = {}): DialogCloseButtonViewModel {
  const title = disabled && disabledReason ? disabledReason : label;

  return {
    type: "button",
    ariaLabel: label,
    title,
    disabled,
    iconAriaHidden: true,
    className: [
      "inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-[2px] border [outline-offset:-2px]",
      "border-border bg-transparent text-muted-foreground transition-colors duration-150",
      "hover:border-[#ADB8C4] hover:bg-[#EAEEF3] hover:text-foreground",
      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
      "disabled:cursor-not-allowed disabled:opacity-50"
    ].join(" ")
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
