// Meridian Pagination (Concrete) — prev/next controls, windowed page numbers with
// ellipses, and a jump-to-page input. Flat hairline buttons; the active page carries the
// steel accent. No external deps.
import { useEffect, useState } from "react";
import { injectStyle } from "../operations/inject-style";

const CSS = `
.pgn-wrap{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;
  padding:12px;background:var(--bg-medium,#F3F6F9);border-radius:var(--radius-chip,2px);
  border:1px solid var(--border,#CBD3DC);}
.pgn-info{font-family:var(--font-data,monospace);font-size:12px;color:var(--text-muted,#59636F);
  font-variant-numeric:tabular-nums;}
.pgn-controls{display:flex;align-items:center;gap:4px;}
.pgn-btn{appearance:none;border:1px solid var(--border,#CBD3DC);background:var(--bg-panel,var(--bg-light,#fff));
  color:var(--text-primary,#22272E);min-width:28px;height:28px;padding:0 6px;border-radius:var(--radius-button,2px);
  cursor:pointer;font-family:var(--font-data,monospace);font-size:12px;font-weight:500;
  display:flex;align-items:center;justify-content:center;
  transition:background .1s ease,border-color .1s ease;}
.pgn-btn:hover:not(:disabled){background:var(--bg-active,#E6EEF5);border-color:var(--accent,#2F6F8F);}
.pgn-btn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
.pgn-btn:disabled{opacity:.4;cursor:not-allowed;}
.pgn-btn--active{background:var(--accent,#2F6F8F);color:#fff;border-color:var(--accent,#2F6F8F);}
.pgn-ellipsis{padding:0 4px;color:var(--text-muted,#59636F);}
.pgn-jump{height:28px;padding:4px 8px;width:52px;text-align:center;
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);
  background:var(--bg-panel,var(--bg-light,#fff));color:var(--text-primary,#22272E);
  font-family:var(--font-data,monospace);font-size:12px;transition:border-color .1s ease;}
.pgn-jump:focus{outline:none;border-color:var(--accent,#2F6F8F);}
`;

export interface PaginationProps {
  currentPage?: number;
  totalPages?: number;
  onPageChange?: (page: number) => void;
  totalItems?: number;
  itemsPerPage?: number;
  siblingCount?: number;
}

/**
 * Pagination — page controls with a windowed number range and jump-to input.
 *
 * @example
 * <Pagination currentPage={page} totalPages={20} totalItems={987} itemsPerPage={50} onPageChange={setPage} />
 */
export function Pagination({
  currentPage = 1,
  totalPages = 1,
  onPageChange = () => {},
  totalItems = 0,
  itemsPerPage = 50,
  siblingCount = 1,
}: PaginationProps) {
  injectStyle("pagination", CSS);
  const [jumpValue, setJumpValue] = useState(String(currentPage));

  useEffect(() => {
    setJumpValue(String(currentPage));
  }, [currentPage]);

  const goTo = (page: number) => {
    const clamped = Math.max(1, Math.min(totalPages, page));
    onPageChange(clamped);
  };

  const handleJump = () => {
    const page = Math.max(1, Math.min(totalPages, parseInt(jumpValue, 10) || 1));
    setJumpValue(String(page));
    onPageChange(page);
  };

  const getPageRange = (): Array<number | "..."> => {
    const range: Array<number | "..."> = [];
    const leftSibling = Math.max(2, currentPage - siblingCount);
    const rightSibling = Math.min(totalPages - 1, currentPage + siblingCount);

    range.push(1);
    if (leftSibling > 2) range.push("...");
    for (let i = leftSibling; i <= rightSibling; i++) range.push(i);
    if (rightSibling < totalPages - 1) range.push("...");
    if (totalPages > 1) range.push(totalPages);
    return range;
  };

  const pageRange = getPageRange();
  const startItem = (currentPage - 1) * itemsPerPage + 1;
  const endItem = Math.min(currentPage * itemsPerPage, totalItems || 0);

  return (
    <div className="pgn-wrap">
      <div className="pgn-info">
        {totalItems > 0
          ? `${startItem}–${endItem} of ${totalItems}`
          : `Page ${currentPage} of ${totalPages}`}
      </div>

      <div className="pgn-controls">
        <button
          type="button"
          className="pgn-btn"
          onClick={() => goTo(currentPage - 1)}
          disabled={currentPage === 1}
          aria-label="Previous page"
        >
          ‹
        </button>

        {pageRange.map((page, idx) =>
          page === "..." ? (
            <span key={`e${idx}`} className="pgn-ellipsis">
              …
            </span>
          ) : (
            <button
              type="button"
              key={page}
              className={`pgn-btn${page === currentPage ? " pgn-btn--active" : ""}`}
              onClick={() => goTo(page)}
              aria-label={`Page ${page}`}
              aria-current={page === currentPage ? "page" : undefined}
            >
              {page}
            </button>
          ),
        )}

        <button
          type="button"
          className="pgn-btn"
          onClick={() => goTo(currentPage + 1)}
          disabled={currentPage === totalPages}
          aria-label="Next page"
        >
          ›
        </button>

        <input
          type="number"
          className="pgn-jump"
          value={jumpValue}
          onChange={(e) => setJumpValue(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && handleJump()}
          onBlur={handleJump}
          min={1}
          max={totalPages}
          aria-label="Jump to page"
        />
      </div>
    </div>
  );
}
