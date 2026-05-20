import { useCallback, useEffect, useRef } from "react";
import { DatabaseZap, FileCheck2, FlaskConical, Landmark, LayoutGrid, RadioTower, Settings, WalletCards, X } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import {
  resolveMegaMenuKeyCommand,
  useMegaMenuViewModel,
  type MegaMenuFocusBoundary
} from "@/components/meridian/mega-menu.view-model";
import { cn } from "@/lib/utils";
import type { AppShellOperatingScopeInput } from "@/app-shell.view-model";
import type { WorkspaceKey } from "@/types";

const WORKSPACE_ICONS: Record<WorkspaceKey, typeof RadioTower> = {
  trading: RadioTower,
  portfolio: WalletCards,
  accounting: Landmark,
  reporting: FileCheck2,
  strategy: FlaskConical,
  data: DatabaseZap,
  settings: Settings
};

/**
 * Masthead workspace mega menu. The view model owns open state, active route state,
 * labels, and key-command decisions; this view owns DOM refs, focus movement, and markup.
 */
interface MegaMenuProps {
  operatingContextScope?: AppShellOperatingScopeInput | null;
}

export function MegaMenu({ operatingContextScope = null }: MegaMenuProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const { pathname, search } = useLocation();
  const vm = useMegaMenuViewModel(pathname, search, operatingContextScope);

  const closeMenu = useCallback(() => {
    vm.closeMenu();
    restoreFocusRef.current?.focus();
  }, [vm.closeMenu]);

  useEffect(() => {
    if (!vm.open) {
      return;
    }

    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : triggerRef.current;
    requestAnimationFrame(() => focusMegaMenuBoundary(panelRef.current, "focus-first"));
  }, [vm.open]);

  useEffect(() => {
    if (!vm.open) {
      return;
    }

    const handleKey = (event: KeyboardEvent) => {
      const command = resolveMegaMenuKeyCommand({
        key: event.key,
        shiftKey: event.shiftKey,
        focusBoundary: getMegaMenuFocusBoundary(panelRef.current, document.activeElement)
      });

      if (command === "close") {
        event.preventDefault();
        closeMenu();
        return;
      }

      if (command === "focus-first" || command === "focus-last") {
        event.preventDefault();
        focusMegaMenuBoundary(panelRef.current, command);
      }
    };

    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [closeMenu, vm.open]);

  useEffect(() => {
    if (!vm.open) {
      return;
    }

    const handleClick = (event: MouseEvent) => {
      if (
        panelRef.current &&
        !panelRef.current.contains(event.target as Node) &&
        triggerRef.current &&
        !triggerRef.current.contains(event.target as Node)
      ) {
        closeMenu();
      }
    };

    window.addEventListener("mousedown", handleClick);
    return () => window.removeEventListener("mousedown", handleClick);
  }, [closeMenu, vm.open]);

  return (
    <div className="mega-menu-root">
      <button
        ref={triggerRef}
        type="button"
        className={cn(
          "mega-menu-trigger focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          vm.open && "active"
        )}
        aria-label={vm.triggerAriaLabel}
        aria-controls={vm.triggerControlsId}
        aria-expanded={vm.triggerExpanded}
        aria-haspopup="dialog"
        onClick={vm.toggleMenu}
      >
        {vm.open ? <X className="h-4 w-4" aria-hidden="true" /> : <LayoutGrid className="h-4 w-4" aria-hidden="true" />}
      </button>

      {vm.open && (
        <div
          ref={panelRef}
          id={vm.panelId}
          role="dialog"
          aria-label={vm.panelAriaLabel}
          aria-modal="false"
          tabIndex={-1}
          className="mega-menu-panel"
        >
          <div className="mega-menu-grid">
            {vm.sections.map((section) => {
              const SectionIcon = WORKSPACE_ICONS[section.key];
              return (
              <div key={section.key} className={cn("mega-menu-section", section.active && "active")}>
                <div className="mega-menu-section-icon-row">
                  <SectionIcon className="mega-menu-section-icon" aria-hidden="true" />
                </div>
                <div className="mega-menu-section-eyebrow">{section.eyebrow}</div>
                <div className="mega-menu-section-heading">
                  <Link
                    to={section.links[0].route}
                    className="mega-menu-section-title"
                    aria-current={section.ariaCurrent}
                    onClick={closeMenu}
                  >
                    {section.label}
                  </Link>
                  {section.active && <span className="mega-menu-active-pip" aria-label="Current workspace" />}
                </div>
                <ul className="mega-menu-link-list" role="list">
                  {section.links.map((link) => (
                    <li key={link.route}>
                      <Link
                        to={link.route}
                        aria-label={link.ariaLabel}
                        aria-current={link.ariaCurrent}
                        className={cn(
                          "mega-menu-link focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                          link.active && "active"
                        )}
                        onClick={closeMenu}
                      >
                        <span className="mega-menu-link-label">{link.label}</span>
                        <span className="mega-menu-link-desc">{link.description}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
              );
            })}
          </div>

          <div className="mega-menu-footer">
            <span className="mega-menu-footer-label">{vm.footerLabel}</span>
            {vm.operatingScopeLabel ? (
              <span className="mega-menu-footer-scope" aria-label={vm.operatingScopeAriaLabel ?? undefined}>
                {vm.operatingScopeLabel}
              </span>
            ) : null}
            <span className="mega-menu-footer-hint" aria-label="Open command palette with Control K">
              <span className="mega-menu-footer-hint-label">Command palette</span>
              <span className="mega-menu-footer-shortcut">{vm.footerShortcut}</span>
            </span>
          </div>
        </div>
      )}
    </div>
  );
}

const megaMenuFocusableSelector = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  "[tabindex]:not([tabindex='-1'])"
].join(",");

function getMegaMenuFocusableElements(panel: HTMLDivElement | null): HTMLElement[] {
  if (!panel) {
    return [];
  }

  return Array.from(panel.querySelectorAll<HTMLElement>(megaMenuFocusableSelector)).filter(
    (element) => !element.hasAttribute("disabled") && element.getAttribute("aria-hidden") !== "true"
  );
}

function getMegaMenuFocusBoundary(panel: HTMLDivElement | null, activeElement: Element | null): MegaMenuFocusBoundary {
  const focusable = getMegaMenuFocusableElements(panel);
  if (focusable.length === 0) {
    return "none";
  }

  if (!panel || !activeElement || !panel.contains(activeElement)) {
    return "outside";
  }

  if (activeElement === focusable[0]) {
    return "first";
  }

  if (activeElement === focusable[focusable.length - 1]) {
    return "last";
  }

  return "middle";
}

function focusMegaMenuBoundary(panel: HTMLDivElement | null, command: "focus-first" | "focus-last") {
  const focusable = getMegaMenuFocusableElements(panel);
  const target = command === "focus-first" ? focusable[0] : focusable[focusable.length - 1];
  (target ?? panel)?.focus();
}
