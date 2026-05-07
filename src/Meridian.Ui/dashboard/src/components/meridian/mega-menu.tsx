import { useEffect, useRef, useState } from "react";
import { LayoutGrid, X } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { buildMegaMenuViewModel } from "@/components/meridian/mega-menu.view-model";
import { cn } from "@/lib/utils";

/**
 * J.P. Morgan-style mega menu triggered by a grid icon in the masthead.
 * Opens a categorized panel showing all 7 Meridian workspaces and their sub-links.
 * Closes on Escape, click outside, or by toggling the trigger button.
 */
export function MegaMenu() {
  const [open, setOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const { pathname } = useLocation();
  const vm = buildMegaMenuViewModel();

  // Close on route change
  useEffect(() => { setOpen(false); }, [pathname]);

  // Close on Escape, trap focus within panel
  useEffect(() => {
    if (!open) return;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("keydown", handleKey);
    return () => document.removeEventListener("keydown", handleKey);
  }, [open]);

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    const handleClick = (e: MouseEvent) => {
      if (
        panelRef.current && !panelRef.current.contains(e.target as Node) &&
        triggerRef.current && !triggerRef.current.contains(e.target as Node)
      ) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, [open]);

  return (
    <div className="mega-menu-root">
      <button
        ref={triggerRef}
        type="button"
        className={cn(
          "mega-menu-trigger focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          open && "active"
        )}
        aria-label={vm.triggerAriaLabel}
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => setOpen((v) => !v)}
      >
        {open ? <X className="h-4 w-4" aria-hidden="true" /> : <LayoutGrid className="h-4 w-4" aria-hidden="true" />}
      </button>

      {open && (
        <div
          ref={panelRef}
          role="dialog"
          aria-label={vm.panelAriaLabel}
          aria-modal="false"
          className="mega-menu-panel"
        >
          <div className="mega-menu-grid">
            {vm.sections.map((section) => {
              const isActive = pathname.startsWith(`/${section.key}`);
              return (
                <div key={section.key} className={cn("mega-menu-section", isActive && "active")}>
                  <div className="mega-menu-section-eyebrow">{section.eyebrow}</div>
                  <div className="mega-menu-section-heading">
                    <Link
                      to={section.links[0].route}
                      className="mega-menu-section-title"
                      aria-current={isActive ? "true" : undefined}
                    >
                      {section.label}
                    </Link>
                    {isActive && (
                      <span className="mega-menu-active-pip" aria-label="Current workspace" />
                    )}
                  </div>
                  <ul className="mega-menu-link-list" role="list">
                    {section.links.map((link) => (
                      <li key={link.route}>
                        <Link
                          to={link.route}
                          className="mega-menu-link focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
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
            <span className="mega-menu-footer-shortcut" aria-label="Open command palette with Control K">
              {vm.footerShortcut}
            </span>
          </div>
        </div>
      )}
    </div>
  );
}
