import { act, fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ToastProvider, useToast } from "./toast";

function Trigger({ onAction }: { onAction: () => void }) {
  const toast = useToast();
  return (
    <button
      type="button"
      onClick={() => toast.show({ title: "Marked as read", action: { label: "Undo", onAction } })}
    >
      trigger
    </button>
  );
}

describe("toast action", () => {
  it("runs the action and dismisses the toast when the action button is clicked", () => {
    const onAction = vi.fn();
    render(
      <ToastProvider>
        <Trigger onAction={onAction} />
      </ToastProvider>
    );

    act(() => {
      fireEvent.click(screen.getByText("trigger"));
    });
    fireEvent.click(screen.getByRole("button", { name: "Undo" }));

    expect(onAction).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("Marked as read")).toBeNull();
  });

  it("renders no action button when none is supplied", () => {
    function Plain() {
      const toast = useToast();
      return <button type="button" onClick={() => toast.success("Saved")}>plain</button>;
    }
    render(
      <ToastProvider>
        <Plain />
      </ToastProvider>
    );

    act(() => {
      fireEvent.click(screen.getByText("plain"));
    });
    expect(screen.queryByRole("button", { name: "Undo" })).toBeNull();
    expect(screen.getByText("Saved")).toBeTruthy();
  });
});
