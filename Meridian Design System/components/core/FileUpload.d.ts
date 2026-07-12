/** Drag-and-drop file input — dashed drop zone (click to browse instead), selected-file list with a
 * per-file remove button. Renders its own list; it does not render a preview thumbnail. */
export interface FileUploadProps {
  /** Small-caps label rendered above the drop zone. */
  label?: string;
  /** Fires with the full current file array after every add or remove. */
  onFilesSelected?: (files: File[]) => void;
  /** Allow selecting/dropping more than one file; new files append rather than replace. @default true */
  multiple?: boolean;
  /** HTML `accept` filter, e.g. `".csv,.xlsx"`. @default "*" (any file type) */
  accept?: string;
}
export declare function FileUpload(props: FileUploadProps): JSX.Element;
