import React from "react";
export interface ToastOptions { tone?: "success"|"warning"|"error"|"info"; title: string; detail?: string; duration?: number; }
export interface MeridianToastAPI { show(opts: ToastOptions): void; success(title: string, detail?: string, duration?: number): void; warning(title: string, detail?: string, duration?: number): void; error(title: string, detail?: string, duration?: number): void; info(title: string, detail?: string, duration?: number): void; }
declare global { interface Window { MeridianToast: MeridianToastAPI; } }
export function ToastProvider(): React.ReactElement | null;
export function useToast(): MeridianToastAPI;
