import { act, render, type RenderOptions } from "@testing-library/react";
import type { ReactElement, ReactNode } from "react";
import { MemoryRouter, type MemoryRouterProps } from "react-router-dom";

type TestMemoryRouterProps = {
  children: ReactNode;
  initialEntries?: MemoryRouterProps["initialEntries"];
};

type RenderWithRouterOptions = Omit<RenderOptions, "wrapper"> & {
  initialEntries?: MemoryRouterProps["initialEntries"];
};

const routerFutureFlags: MemoryRouterProps["future"] = {
  v7_relativeSplatPath: true,
  v7_startTransition: true
};

export function TestMemoryRouter({ children, initialEntries = ["/"] }: TestMemoryRouterProps) {
  return (
    <MemoryRouter future={routerFutureFlags} initialEntries={initialEntries}>
      {children}
    </MemoryRouter>
  );
}

export function renderWithRouter(
  ui: ReactElement,
  { initialEntries = ["/"], ...renderOptions }: RenderWithRouterOptions = {}
) {
  return render(<TestMemoryRouter initialEntries={initialEntries}>{ui}</TestMemoryRouter>, renderOptions);
}

export async function waitForAsyncEffects() {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}
