import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Pagination } from "./pagination";

describe("Pagination (Concrete)", () => {
  it("renders an item range summary when totalItems is provided", () => {
    render(<Pagination currentPage={2} totalPages={5} totalItems={230} itemsPerPage={50} />);
    expect(screen.getByText("51–100 of 230")).toBeInTheDocument();
  });

  it("disables prev on the first page and next on the last", () => {
    const { rerender } = render(<Pagination currentPage={1} totalPages={3} />);
    expect(screen.getByLabelText("Previous page")).toBeDisabled();
    rerender(<Pagination currentPage={3} totalPages={3} />);
    expect(screen.getByLabelText("Next page")).toBeDisabled();
  });

  it("emits the next page when the next control is clicked", async () => {
    const onPageChange = vi.fn();
    render(<Pagination currentPage={2} totalPages={5} onPageChange={onPageChange} />);
    await userEvent.click(screen.getByLabelText("Next page"));
    expect(onPageChange).toHaveBeenCalledWith(3);
  });

  it("marks the current page control as current", () => {
    render(<Pagination currentPage={2} totalPages={5} />);
    expect(screen.getByLabelText("Page 2")).toHaveAttribute("aria-current", "page");
  });

  it("keeps the jump input synchronized with currentPage changes", () => {
    const { rerender } = render(<Pagination currentPage={1} totalPages={5} />);
    expect(screen.getByLabelText("Jump to page")).toHaveValue(1);
    rerender(<Pagination currentPage={4} totalPages={5} />);
    expect(screen.getByLabelText("Jump to page")).toHaveValue(4);
  });
});
