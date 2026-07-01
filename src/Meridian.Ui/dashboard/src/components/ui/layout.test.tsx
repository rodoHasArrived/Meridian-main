import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Flex, Grid, Stack } from "@/components/ui/layout";

describe("layout primitives", () => {
  it("renders a vertical Stack with tokenized spacing", () => {
    render(
      <Stack spacing="lg" aria-label="stack">
        <span>a</span>
        <span>b</span>
      </Stack>
    );

    const stack = screen.getByLabelText("stack");
    expect(stack).toHaveClass("flex", "flex-col", "gap-4");
  });

  it("renders a horizontal Flex with alignment and wrap", () => {
    render(
      <Flex gap="sm" align="center" justify="between" wrap aria-label="flex">
        <span>a</span>
      </Flex>
    );

    const flex = screen.getByLabelText("flex");
    expect(flex).toHaveClass("flex-row", "gap-1.5", "items-center", "justify-between", "flex-wrap");
  });

  it("renders an equal-column Grid", () => {
    render(
      <Grid cols={3} gap="xl" aria-label="grid">
        <span>a</span>
      </Grid>
    );

    const grid = screen.getByLabelText("grid");
    expect(grid).toHaveClass("grid", "grid-cols-3", "gap-6");
  });
});
