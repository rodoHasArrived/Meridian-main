// Meridian Pagination — prev/next buttons, page numbers, jump-to input.
// Lightweight, no external deps. Returns current page + pages to display.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.pgn-wrap { display: flex; align-items: center; justify-content: space-between; gap: 12px;
  padding: 12px; background: var(--bg-medium, #F5F7FA); border-radius: var(--radius-chip,2px);
  border: 1px solid var(--border, #D7DCE2); flex-wrap: wrap; }
.pgn-info { font-family: var(--font-data); font-size: 12px; color: var(--text-muted, #59636F); }
.pgn-controls { display: flex; align-items: center; gap: 4px; }
.pgn-btn { appearance: none; border: 1px solid var(--border, #D7DCE2); background: var(--bg-light, #fff);
  color: var(--text-primary, #22272E); width: 28px; height: 28px; border-radius: var(--radius-button,2px);
  cursor: pointer; font-family: var(--font-data); font-size: 12px; font-weight: 500;
  display: flex; align-items: center; justify-content: center;
  transition: background 100ms ease, border-color 100ms ease; }
.pgn-btn:hover:not(:disabled) { background: var(--bg-active, #E6EEF5); border-color: var(--accent, #2F6F8F); }
.pgn-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.pgn-btn--active { background: var(--accent, #2F6F8F); color: var(--text-on-accent, #fff); border-color: var(--accent, #2F6F8F); }
.pgn-jump { height: 28px; padding: 4px 8px; border: 1px solid var(--border, #D7DCE2);
  border-radius: var(--radius-button,2px); background: var(--bg-light, #fff);
  font-family: var(--font-data); font-size: 12px; width: 50px; text-align: center;
  transition: border-color 100ms ease; }
.pgn-jump:focus { outline: none; border-color: var(--accent, #2F6F8F);
  box-shadow: 0 0 0 2px color-mix(in srgb,var(--border-focus) 35%,transparent); }
`;
  const el = document.createElement("style");
  el.setAttribute("data-pgn", "pagination");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Pagination({
  currentPage = 1,
  totalPages = 1,
  onPageChange = () => {},
  totalItems = 0,
  itemsPerPage = 50,
  siblingCount = 1,
}) {
  inject();
  const [jumpValue, setJumpValue] = React.useState(String(currentPage));

  const handlePrev = () => {
    if (currentPage > 1) onPageChange(currentPage - 1);
  };

  const handleNext = () => {
    if (currentPage < totalPages) onPageChange(currentPage + 1);
  };

  const handlePageClick = (page) => {
    onPageChange(page);
  };

  const handleJump = () => {
    const page = Math.max(1, Math.min(totalPages, parseInt(jumpValue, 10) || 1));
    setJumpValue(String(page));
    onPageChange(page);
  };

  // Calculate page range to display
  const getPageRange = () => {
    const delta = siblingCount;
    const range = [];
    const leftSibling = Math.max(2, currentPage - delta);
    const rightSibling = Math.min(totalPages - 1, currentPage + delta);

    // Always show first page
    range.push(1);

    // Left ellipsis
    if (leftSibling > 2) range.push("...");

    // Sibling pages
    for (let i = leftSibling; i <= rightSibling; i++) {
      range.push(i);
    }

    // Right ellipsis
    if (rightSibling < totalPages - 1) range.push("...");

    // Always show last page if totalPages > 1
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
          className="pgn-btn"
          onClick={handlePrev}
          disabled={currentPage === 1}
          aria-label="Previous page"
        >
          ‹
        </button>

        {pageRange.map((page, idx) =>
          page === "..." ? (
            <span key={idx} style={{ padding: "0 4px", color: "var(--text-muted)" }}>…</span>
          ) : (
            <button
              key={idx}
              className={`pgn-btn ${page === currentPage ? "pgn-btn--active" : ""}`}
              onClick={() => handlePageClick(page)}
            >
              {page}
            </button>
          )
        )}

        <button
          className="pgn-btn"
          onClick={handleNext}
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
          min="1"
          max={totalPages}
          aria-label="Jump to page"
        />
      </div>
    </div>
  );
}
