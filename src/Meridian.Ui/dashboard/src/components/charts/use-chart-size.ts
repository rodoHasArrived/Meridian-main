// Measures a chart container so SVG charts draw in real pixels (UX Phase F). Drawing a fixed
// 960×460 canvas into an arbitrary box with `preserveAspectRatio="none"` stretched every axis
// label sideways; with a viewBox equal to the measured box, one unit is one CSS pixel and text
// keeps its shape at any width.
import { useLayoutEffect, useRef, useState, type RefObject } from "react";

export interface ChartSize {
  width: number;
  height: number;
}

/**
 * Returns a ref for the element the chart fills and its measured size. Until a measurement exists
 * (first render, jsdom, a hidden tab) the fallback size is returned so the chart still renders.
 */
export function useChartSize<T extends HTMLElement = HTMLDivElement>(fallback: ChartSize): [RefObject<T | null>, ChartSize] {
  const ref = useRef<T | null>(null);
  const [size, setSize] = useState<ChartSize | null>(null);

  useLayoutEffect(() => {
    const element = ref.current;
    if (!element) return;
    const apply = (width: number, height: number) => {
      const next = normalizeChartSize(width, height);
      if (!next) return;
      setSize((current) => current && current.width === next.width && current.height === next.height ? current : next);
    };
    const rect = element.getBoundingClientRect();
    apply(rect.width, rect.height);
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect;
      if (box) apply(box.width, box.height);
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return [ref, size ?? fallback];
}

/** Whole-pixel size, or null when the box is too small to be a real layout (hidden, unmounted). */
export function normalizeChartSize(width: number, height: number): ChartSize | null {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width < 40 || height < 40) return null;
  return { width: Math.round(width), height: Math.round(height) };
}
