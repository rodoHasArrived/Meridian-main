import { useEffect, useRef } from "react";
import { useSearchParams } from "react-router-dom";
import type { AccountingPostedLedgerViewModel } from "@/screens/accounting-screen.posted-ledger.view-model";

/** Ledger ids travel as GUIDs, whose textual form is case-insensitive; the API and .NET
 * `Guid` treat "AA…" and "aa…" as the same id. Comparing the raw query string exactly rejected a
 * valid link written in upper or mixed case, opened the default scope instead, and then rewrote
 * the link to that different scope — losing the operator's bookmark to a formatting difference. */
function idsMatch(left: string | null | undefined, right: string | null | undefined): boolean {
  if (!left || !right) {
    return false;
  }

  return left.localeCompare(right, undefined, { sensitivity: "accent" }) === 0;
}

/**
 * Binds a posted-ledger view model's book and period to `?ledgerBookId=` and `?periodId=`.
 *
 * Shared by every surface that shows the posted book, so the ledger and trial-balance tabs read
 * and write one scope instead of holding two. Each kept its own before, and the ledger tab never
 * wrote to the route: selecting book B there and switching tabs showed A, and switching back
 * showed B under a URL that said A.
 *
 * Only the surface currently on screen syncs. Two active writers would race each other's edits.
 */
export function usePostedLedgerRouteScope(
  postedLedger: AccountingPostedLedgerViewModel,
  active = true
): void {
  const [searchParams, setSearchParams] = useSearchParams();
  const { selectBook, selectPeriod, selectedPeriodId, periodsSettled } = postedLedger;
  const bookOptions = postedLedger.view.bookOptions;
  const periodOptions = postedLedger.view.periodSelector.options;
  const selectedBookId = bookOptions.find((option) => option.isSelected)?.id ?? null;

  // A deep link names a book and a period, and the two have to settle before the URL is written
  // back — otherwise the write below stamps the default selection over the link's own values
  // before they have been applied, and the two effects trade edits without converging.
  const requestedBookId = searchParams.get("ledgerBookId");
  const requestedPeriodId = searchParams.get("periodId");
  // Only a book this deployment actually has can be waited for; anything else resolves to "no
  // book requested" so a stale bookmark still opens the surface on its default.
  const resolvedRequestedBookId = bookOptions.find((option) => idsMatch(option.id, requestedBookId))?.id ?? null;

  const appliedBookIdRef = useRef<string | null>(null);
  const appliedPeriodIdRef = useRef<string | null>(null);

  useEffect(() => {
    // Books have to have landed before a requested one can be judged present or absent.
    if (!active || !requestedBookId || requestedBookId === appliedBookIdRef.current || bookOptions.length === 0) {
      return;
    }

    appliedBookIdRef.current = requestedBookId;
    if (resolvedRequestedBookId) {
      selectBook(resolvedRequestedBookId);
    }
  }, [active, bookOptions, requestedBookId, resolvedRequestedBookId, selectBook]);

  // Applied only once the requested book is the selected one: periods are scoped to the book, so
  // judging a period against the previous book's set would decline a perfectly good link and land
  // silently on that book's default — a deep link opening a different period than it named.
  useEffect(() => {
    if (!active || !requestedPeriodId || requestedPeriodId === appliedPeriodIdRef.current) {
      return;
    }

    if (resolvedRequestedBookId && resolvedRequestedBookId !== selectedBookId) {
      return;
    }

    // Wait only while the periods are still arriving. A book that has finished loading with no
    // periods at all is an answer, not a pending state: treating it as pending left the request
    // permanently unresolved, so the write-back below never ran and the URL kept naming the empty
    // book and its stale period even after the operator moved to a populated one — a copied or
    // refreshed link then reopened the wrong scope.
    if (!periodsSettled) {
      return;
    }

    appliedPeriodIdRef.current = requestedPeriodId;
    const matched = periodOptions.find((option) => idsMatch(option.id, requestedPeriodId));
    if (matched) {
      selectPeriod(matched.id);
    }
  }, [active, periodOptions, periodsSettled, requestedPeriodId, resolvedRequestedBookId, selectPeriod, selectedBookId]);

  // The book goes into the URL with the period. Without it a link named a period but not the book
  // it belongs to, so reopening it resolved against whichever book sorts first.
  useEffect(() => {
    if (!active) {
      return;
    }

    const bookPending = requestedBookId !== null && requestedBookId !== appliedBookIdRef.current;
    const periodPending = requestedPeriodId !== null && requestedPeriodId !== appliedPeriodIdRef.current;
    if (bookPending || periodPending) {
      return;
    }

    // Compared against what the URL actually says, in both directions: testing only
    // "no selection means nothing to write" left a stale periodId in place for a book that has
    // none, which is the case this has to clean up.
    const periodMatches = selectedPeriodId
      ? idsMatch(searchParams.get("periodId"), selectedPeriodId)
      : searchParams.get("periodId") === null;
    const bookMatches = !selectedBookId || idsMatch(searchParams.get("ledgerBookId"), selectedBookId);
    if (periodMatches && bookMatches) {
      return;
    }

    const nextParams = new URLSearchParams(searchParams);
    if (selectedBookId) {
      nextParams.set("ledgerBookId", selectedBookId);
    }
    if (selectedPeriodId) {
      nextParams.set("periodId", selectedPeriodId);
    } else {
      // The book on screen has no period. Leaving the outgoing one in the URL would name a scope
      // this surface is not showing.
      nextParams.delete("periodId");
    }
    setSearchParams(nextParams, { replace: true });
  }, [active, requestedBookId, requestedPeriodId, searchParams, selectedBookId, selectedPeriodId, setSearchParams]);
}
