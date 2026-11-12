import React from "react";
export interface ToastAction { label: React.ReactNode; onAction?: () => void; }
export interface ToastOptions { tone?: "success"|"warning"|"error"|"info"; title: React.ReactNode; detail?: React.ReactNode; duration?: number; action?: ToastAction | null; }
export interface MeridianToastAPI {
  show(opts: ToastOptions): number;
  success(title: React.ReactNode, detail?: React.ReactNode, duration?: number): number;
  warning(title: React.ReactNode, detail?: React.ReactNode, duration?: number): number;
  error(title: React.ReactNode, detail?: React.ReactNode, duration?: number): number;
  info(title: React.ReactNode, detail?: React.ReactNode, duration?: number): number;
  undo(title: React.ReactNode, onUndo?: () => void, detail?: React.ReactNode, duration?: number): number;
  dismiss(id: number): void;
}
declare global { interface Window { MeridianToast: MeridianToastAPI; } }
export function ToastProvider(): React.ReactElement | null;
export const Toast: typeof ToastProvider;
export function useToast(): MeridianToastAPI;
