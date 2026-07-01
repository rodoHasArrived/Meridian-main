import { type ReactNode, useRef, useState } from "react";
import { Upload, X } from "lucide-react";
import { cn } from "@/lib/utils";

export interface FileUploadProps {
  label?: ReactNode;
  onFilesSelected?: (files: File[]) => void;
  multiple?: boolean;
  accept?: string;
  disabled?: boolean;
  className?: string;
}

/**
 * Drag-and-drop file input (also click-to-browse). Concrete: flat dashed hairline drop zone
 * that shifts to the accent on drag, 2px corners; selected files list as hairline-bordered
 * rows with a dim-red remove control.
 */
export function FileUpload({
  label,
  onFilesSelected,
  multiple = true,
  accept = "*",
  disabled = false,
  className
}: FileUploadProps) {
  const [files, setFiles] = useState<File[]>([]);
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const emit = (next: File[]) => {
    setFiles(next);
    onFilesSelected?.(next);
  };

  const addFiles = (list: FileList | null) => {
    if (!list) {
      return;
    }
    const incoming = Array.from(list);
    emit(multiple ? [...files, ...incoming] : incoming);
  };

  const removeFile = (index: number) => {
    emit(files.filter((_, i) => i !== index));
  };

  return (
    <div className={cn("w-full", className)}>
      {label ? (
        <span className="mb-1.5 block font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {label}
        </span>
      ) : null}
      <button
        type="button"
        disabled={disabled}
        onClick={() => inputRef.current?.click()}
        onDrop={(event) => {
          event.preventDefault();
          setDragging(false);
          if (!disabled) {
            addFiles(event.dataTransfer.files);
          }
        }}
        onDragOver={(event) => {
          event.preventDefault();
          if (!disabled) {
            setDragging(true);
          }
        }}
        onDragLeave={() => setDragging(false)}
        className={cn(
          "flex w-full flex-col items-center gap-2 rounded-[2px] border-2 border-dashed px-5 py-5 text-center transition-colors",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          dragging ? "border-primary bg-primary/10" : "border-border bg-[#F3F6F9] hover:border-[#ADB8C4]",
          disabled && "cursor-not-allowed opacity-55"
        )}
      >
        <Upload className="h-6 w-6 text-muted-foreground" aria-hidden="true" />
        <span className="text-xs text-muted-foreground">Drag files here or click to browse</span>
        <span className="font-mono text-[10px] text-muted-foreground">
          {accept !== "*" ? `Accepts: ${accept}` : "Any file type"}
        </span>
      </button>
      <input
        ref={inputRef}
        type="file"
        multiple={multiple}
        accept={accept}
        disabled={disabled}
        className="hidden"
        onChange={(event) => addFiles(event.target.files)}
      />
      {files.length > 0 ? (
        <ul className="mt-2 space-y-1">
          {files.map((file, index) => (
            <li
              key={`${file.name}-${index}`}
              className="flex items-center gap-2 rounded-[2px] border border-border bg-card px-2 py-1.5 text-xs"
            >
              <span className="min-w-0 flex-1 truncate font-mono text-foreground">{file.name}</span>
              <button
                type="button"
                aria-label={`Remove ${file.name}`}
                onClick={() => removeFile(index)}
                className="text-danger transition-colors hover:text-danger/80 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              >
                <X className="h-3.5 w-3.5" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
