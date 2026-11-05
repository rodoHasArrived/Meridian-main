import { isValidElement, type ReactNode } from "react";

/** Plain text of a label: strings and numbers, through fragments and elements. */
export function statusLabelText(node: ReactNode): string {
  if (node === null || node === undefined || typeof node === "boolean") {
    return "";
  }
  if (typeof node === "string" || typeof node === "number") {
    return String(node);
  }
  if (Array.isArray(node)) {
    return node.map(statusLabelText).join("");
  }
  if (isValidElement<{ children?: ReactNode }>(node)) {
    return statusLabelText(node.props.children);
  }
  return "";
}

/**
 * Tracked caps are for badges of one or two words ("READY", "NEEDS REVIEW"). Counts and
 * sentences ("0 of 2 families current · 2 need attention") read in normal case.
 */
export function usesTrackedCaps(node: ReactNode): boolean {
  const words = statusLabelText(node).trim().split(/\s+/).filter((word) => /[\p{L}\p{N}]/u.test(word));
  return words.length <= 2;
}
