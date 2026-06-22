import { X } from "lucide-react";
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export type ToastTone = "success" | "warning" | "danger" | "info";

export interface ToastOptions {
  detail?: ReactNode;
  durationMs?: number;
  id?: string;
  title: ReactNode;
  tone?: ToastTone;
}

interface ToastEntry extends Required<Pick<ToastOptions, "id" | "tone">> {
  detail?: ReactNode;
  title: ReactNode;
}

export interface ToastApi {
  dismiss: (id: string) => void;
  show: (toast: ToastOptions) => string;
  success: (title: ReactNode, detail?: ReactNode) => string;
  warning: (title: ReactNode, detail?: ReactNode) => string;
  danger: (title: ReactNode, detail?: ReactNode) => string;
  info: (title: ReactNode, detail?: ReactNode) => string;
}

const ToastContext = createContext<ToastApi | null>(null);

const toneClasses: Record<ToastTone, string> = {
  success: "border-l-success",
  warning: "border-l-warning",
  danger: "border-l-danger",
  info: "border-l-primary"
};

export function ToastProvider({ children, maxToasts = 5 }: { children?: ReactNode; maxToasts?: number }) {
  const [toasts, setToasts] = useState<ToastEntry[]>([]);

  const dismiss = useCallback((id: string) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const show = useCallback((toast: ToastOptions) => {
    const id = toast.id ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    const entry: ToastEntry = {
      detail: toast.detail,
      id,
      title: toast.title,
      tone: toast.tone ?? "info"
    };

    setToasts((current) => [...current.slice(-(maxToasts - 1)), entry]);

    if (toast.durationMs !== 0 && typeof window !== "undefined") {
      window.setTimeout(() => dismiss(id), toast.durationMs ?? 4000);
    }

    return id;
  }, [dismiss, maxToasts]);

  const api = useMemo<ToastApi>(() => ({
    danger: (title, detail) => show({ detail, title, tone: "danger" }),
    dismiss,
    info: (title, detail) => show({ detail, title, tone: "info" }),
    show,
    success: (title, detail) => show({ detail, title, tone: "success" }),
    warning: (title, detail) => show({ detail, title, tone: "warning" })
  }), [dismiss, show]);

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="fixed bottom-5 right-5 z-50 flex max-w-[calc(100vw-2.5rem)] flex-col-reverse gap-2" role="region" aria-label="Notifications">
        {toasts.map((toast) => (
          <Toast key={toast.id} toast={toast} onDismiss={() => dismiss(toast.id)} />
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function Toast({ className, onDismiss, toast }: { className?: string; onDismiss?: () => void; toast: ToastEntry }) {
  return (
    <div
      className={cn(
        "flex min-w-72 max-w-md items-start gap-3 rounded-[var(--radius-card,0.5rem)] border border-l-4 border-border bg-popover px-3.5 py-3 text-popover-foreground shadow-[var(--shadow-float)]",
        toneClasses[toast.tone],
        className
      )}
      role="status"
    >
      <div className="min-w-0 flex-1">
        <div className="text-sm font-semibold">{toast.title}</div>
        {toast.detail ? <div className="mt-0.5 font-mono text-xs leading-5 text-muted-foreground">{toast.detail}</div> : null}
      </div>
      {onDismiss ? (
        <button
          type="button"
          aria-label="Dismiss notification"
          className="text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          onClick={onDismiss}
        >
          <X className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
      ) : null}
    </div>
  );
}

export function useToast() {
  return useContext(ToastContext) ?? noopToastApi;
}

const noopToastApi: ToastApi = {
  danger: () => "",
  dismiss: () => undefined,
  info: () => "",
  show: () => "",
  success: () => "",
  warning: () => ""
};
