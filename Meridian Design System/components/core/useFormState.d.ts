import type React from "react";
export interface DirtyState<T extends Record<string, unknown>> {
  values: T; baseline: T; dirty: boolean; dirtyFields: Set<keyof T>;
  set<K extends keyof T>(key: K, value: T[K]): void;
  set(patch: Partial<T>): void;
  setValues: React.Dispatch<React.SetStateAction<T>>;
  isDirty(key: keyof T): boolean; reset(): void; commit(): void;
}
export function useDirtyState<T extends Record<string, unknown>>(initial: T): DirtyState<T>;
export interface AsyncValidationState {
  status: "idle" | "checking" | "valid" | "invalid"; error: string | null;
  checking: boolean; valid: boolean; invalid: boolean;
}
export function useAsyncValidation<T>(value: T, validator: (value: T, signal?: AbortSignal) => Promise<string | null | undefined>, options?: { debounceMs?: number; skipEmpty?: boolean }): AsyncValidationState;
export interface WizardState<T> {
  step: number; stepName: T | undefined; steps: T[]; next(): void; back(): void; goTo(index: number): void;
  isFirst: boolean; isLast: boolean; furthest: number; canAdvance: boolean;
  isReachable(index: number): boolean; progress: number;
}
export function useWizard<T>(steps: T[], options?: { validate?: (index: number) => boolean; onComplete?: () => void }): WizardState<T>;
/** Public carrier; invoke its members as ordinary React hooks inside a component. */
export const FormHooks: { useDirtyState: typeof useDirtyState; useAsyncValidation: typeof useAsyncValidation; useWizard: typeof useWizard };
