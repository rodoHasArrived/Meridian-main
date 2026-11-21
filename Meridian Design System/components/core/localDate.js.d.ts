export const pad: (value: number | string) => string;
export const toISO: (date: Date) => string;
export const fromISO: (value: unknown) => Date | null;
export const dayOfInstant: (value: Date | string | number) => string | null;
export const addDays: (date: Date, count: number) => Date;
export const addMonths: (date: Date, count: number) => Date;
export const sameDay: (left: Date | null | undefined, right: Date | null | undefined) => boolean;
export const clampDate: (date: Date, minimum?: Date | null, maximum?: Date | null) => Date;
export const parseLoose: (value: unknown) => Date | null;
/** Sunday-first labels used by the local-calendar pickers. */
export const DOW: string[];
export const dowLabels: (weekStartsOn?: number) => string[];
export const longDate: (date: Date) => string;
export const monthTitle: (date: Date) => string;
export const weeksFor: (cursor: Date, weekStartsOn?: number) => Date[][];
export interface DateRangePreset { id: string; label: string; start: string; end: string; }
export const rangePresets: (today?: Date) => DateRangePreset[];
