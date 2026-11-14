import { Info } from "lucide-react";
import { createContext, useContext, useEffect, useId, useRef, useState, type ReactNode } from "react";
import { SectionDescriptionPreferences } from "@/components/ui/section-description";
import { cn } from "@/lib/utils";
import "@/styles/section-heading.css";

const SectionHelpUserContext = createContext<string | null>(null);
const STORAGE_PREFIX = "meridian.workstation.sectionHelp.v1";

/** Scope remembered explanatory text to the current operator; unknown operators do not persist. */
export function SectionHelpProvider({ userKey, children }: { userKey: string | null; children: ReactNode }) {
  return <SectionDescriptionPreferences operatorKey={userKey}><SectionHelpUserContext.Provider value={userKey}>{children}</SectionHelpUserContext.Provider></SectionDescriptionPreferences>;
}

export interface SectionHeadingProps {
  title: ReactNode;
  description: string;
  /** Stable section identity. Only static introductory copy belongs in this disclosure. */
  helpId: string;
  as?: "h2" | "h3" | "h4";
  id?: string;
  className?: string;
  headingClassName?: string;
  descriptionClassName?: string;
  /** Accessible name for non-text titles. */
  helpLabel?: string;
}

/** Show introductory copy once, then keep it available beside the section title. */
export function SectionHeading(props: SectionHeadingProps) {
  const userKey = useContext(SectionHelpUserContext);
  // Reset the first-visit state when either the operator, section, or explanatory copy changes.
  return <RememberedSectionHeading key={JSON.stringify([userKey, props.helpId, props.description])} {...props} userKey={userKey} />;
}

function RememberedSectionHeading({
  title, description, helpId, as: Heading = "h2", id, className, headingClassName,
  descriptionClassName, helpLabel, userKey
}: SectionHeadingProps & { userKey: string | null }) {
  const storageKey = userKey ? `${STORAGE_PREFIX}:${encodeURIComponent(userKey)}:${encodeURIComponent(helpId)}` : null;
  const [seen] = useState(() => {
    try { return Boolean(storageKey && window.localStorage.getItem(storageKey) === description); }
    catch { return false; }
  });
  const [open, setOpen] = useState(false);
  const [helpMaxWidth, setHelpMaxWidth] = useState(448);
  const rootRef = useRef<HTMLDivElement>(null);
  const helpRef = useRef<HTMLSpanElement>(null);
  const descriptionId = useId();
  const label = helpLabel ?? (typeof title === "string" ? title : "this section");

  useEffect(() => {
    if (!storageKey || seen || !rootRef.current) return;
    const remember = () => {
      try { window.localStorage.setItem(storageKey, description); }
      catch { /* A blocked browser store must never prevent reading the introduction. */ }
    };
    if (typeof IntersectionObserver === "undefined") { remember(); return; }
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) { remember(); observer.disconnect(); }
    });
    observer.observe(rootRef.current);
    return () => observer.disconnect();
  }, [description, seen, storageKey]);

  useEffect(() => {
    if (!open) return;
    const sizeHelp = () => setHelpMaxWidth(Math.max(0, Math.min(448, window.innerWidth - (rootRef.current?.getBoundingClientRect().left ?? 0) - 16)));
    sizeHelp();
    const dismiss = (event: PointerEvent) => {
      if (event.target instanceof Node && !helpRef.current?.contains(event.target)) setOpen(false);
    };
    document.addEventListener("pointerdown", dismiss);
    window.addEventListener("resize", sizeHelp);
    return () => {
      document.removeEventListener("pointerdown", dismiss);
      window.removeEventListener("resize", sizeHelp);
    };
  }, [open]);

  return (
    <div ref={rootRef} className={cn("section-heading", className)}>
      <div className="section-heading-title-row">
        <Heading id={id} className={cn("font-display text-lg font-semibold leading-tight text-foreground", headingClassName)}>{title}</Heading>
        {seen ? (
          <span ref={helpRef} className="section-heading-help" onKeyDown={(event) => {
            if (event.key === "Escape" && open) { event.stopPropagation(); setOpen(false); }
          }} onBlur={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false);
          }}>
            <button type="button" className="section-heading-info" aria-label={`About ${label}`}
              aria-expanded={open} aria-controls={descriptionId} aria-describedby={open ? descriptionId : undefined}
              onClick={() => setOpen((current) => !current)}>
              <Info size={14} aria-hidden="true" />
            </button>
            {open ? <span id={descriptionId} role="tooltip" className="section-heading-popover" style={{ maxWidth: helpMaxWidth }}>{description}</span> : null}
          </span>
        ) : null}
      </div>
      {!seen ? <p className={cn("section-heading-description", descriptionClassName)}>{description}</p> : null}
    </div>
  );
}
