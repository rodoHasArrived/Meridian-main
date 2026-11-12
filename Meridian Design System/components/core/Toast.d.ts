import React from "react";
export interface ToastOptions { tone?: "success"|"warning"|"error"|"info"; title: string; detail?: string; duration?: number; action?: { label: string; onAction?: () => void } | null; }
export interface MeridianToastAPI {
  show(opts: ToastOptions): number;
  success(title: string, detail?: string, duration?: number): number;
  warning(title: string, detail?: string, duration?: number): number;
  error(title: string, detail?: string, duration?: number): number;
  info(title: string, detail?: string, duration?: number): number;
  undo(title: string, onUndo: () => void, detail?: string, duration?: number): number;
  dismiss(id: number): void;
}
declare global { interface Window { MeridianToast: MeridianToastAPI; } }
export function ToastProvider(): React.ReactElement | null;
export function useToast(): MeridianToastAPI;
export const Toast: typeof ToastProvider;
