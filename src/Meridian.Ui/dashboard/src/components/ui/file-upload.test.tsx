import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FileUpload } from "@/components/ui/file-upload";

describe("FileUpload", () => {
  it("selects files through the input and lists them", async () => {
    const user = userEvent.setup();
    const onFilesSelected = vi.fn();
    const { container } = render(<FileUpload label="Evidence" onFilesSelected={onFilesSelected} />);

    const input = container.querySelector("input[type='file']") as HTMLInputElement;
    const file = new File(["x"], "trades.csv", { type: "text/csv" });
    await user.upload(input, file);

    expect(onFilesSelected).toHaveBeenCalledWith([file]);
    expect(screen.getByText("trades.csv")).toBeInTheDocument();
  });

  it("removes a selected file", async () => {
    const user = userEvent.setup();
    const onFilesSelected = vi.fn();
    const { container } = render(<FileUpload onFilesSelected={onFilesSelected} />);

    const input = container.querySelector("input[type='file']") as HTMLInputElement;
    const file = new File(["x"], "ledger.json", { type: "application/json" });
    await user.upload(input, file);

    await user.click(screen.getByRole("button", { name: "Remove ledger.json" }));
    expect(onFilesSelected).toHaveBeenLastCalledWith([]);
    expect(screen.queryByText("ledger.json")).not.toBeInTheDocument();
  });
});
