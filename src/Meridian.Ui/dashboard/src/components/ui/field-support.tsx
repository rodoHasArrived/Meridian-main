import { cn } from "@/lib/utils";

export function joinDescribedByIds(...ids: Array<string | null | undefined | false>): string | undefined {
  const value = ids.filter((id): id is string => Boolean(id)).join(" ");
  return value.length > 0 ? value : undefined;
}

export interface FieldSupportTextProps {
  helpId?: string;
  helpText?: string | null;
  helpClassName?: string;
  disabledReason?: string | null;
  disabledReasonId?: string;
  disabledReasonClassName?: string;
}

export function FieldSupportText({
  helpId,
  helpText,
  helpClassName,
  disabledReason,
  disabledReasonId,
  disabledReasonClassName
}: FieldSupportTextProps) {
  return (
    <>
      {helpText ? (
        <span id={helpId} className={cn("text-xs leading-5 text-muted-foreground", helpClassName)}>
          {helpText}
        </span>
      ) : null}
      {disabledReason && disabledReasonId ? (
        <span id={disabledReasonId} className={cn("text-xs leading-5 text-warning", disabledReasonClassName)}>
          {disabledReason}
        </span>
      ) : null}
    </>
  );
}
