// Meridian FileUpload — drag-drop file input, multiple files, functional only.
import React from "react";
const { useState, useRef } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-upload-wrap{display:block;width:100%;}
.mds-upload-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);margin-bottom:5px;}
.mds-upload-zone{border:2px dashed var(--border,#D7DCE2);padding:20px;text-align:center;
  background:var(--bg-light,#fff);cursor:pointer;}
.mds-upload-zone--drag{border-color:var(--accent,#2F6F8F);background:var(--bg-active,#E6EEF5);}
.mds-upload-icon{color:var(--text-disabled,#889099);margin-bottom:8px;display:flex;align-items:center;justify-content:center;}
.mds-upload-text{font-family:var(--font-body);font-size:12px;color:var(--text-secondary,#4D5967);}
.mds-upload-hint{font-family:var(--font-body);font-size:10px;color:var(--text-muted,#59636F);margin-top:4px;}
.mds-upload-input{display:none;}
.mds-file-list{list-style:none;padding:0;margin:8px 0 0;}
.mds-file-item{display:flex;align-items:center;gap:8px;padding:6px 8px;
  border:1px solid var(--border,#D7DCE2);margin-top:4px;font-size:12px;}
.mds-file-name{flex:1;overflow:hidden;text-overflow:ellipsis;}
.mds-file-remove{cursor:pointer;color:var(--red,#BA3F55);font-weight:600;border:none;
  background:transparent;padding:0;font-size:12px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "fileupload");
  el.textContent = css;
  document.head.appendChild(el);
}

export function FileUpload({ label, onFilesSelected, multiple = true, accept = "*" }) {
  inject();
  const [files, setFiles] = useState([]);
  const [isDrag, setIsDrag] = useState(false);
  const inputRef = useRef(null);

  const handleFiles = (fileList) => {
    const newFiles = Array.from(fileList);
    const updated = multiple ? [...files, ...newFiles] : newFiles;
    setFiles(updated);
    onFilesSelected?.(updated);
  };

  const handleDrop = (e) => {
    e.preventDefault();
    setIsDrag(false);
    handleFiles(e.dataTransfer.files);
  };

  const removeFile = (idx) => {
    const updated = files.filter((_, i) => i !== idx);
    setFiles(updated);
    onFilesSelected?.(updated);
  };

  return (
    <div className="mds-upload-wrap">
      {label && <label className="mds-upload-label">{label}</label>}
      <div
        className={`mds-upload-zone${isDrag ? " mds-upload-zone--drag" : ""}`}
        onDrop={handleDrop}
        onDragOver={(e) => { e.preventDefault(); setIsDrag(true); }}
        onDragLeave={() => setIsDrag(false)}
        onClick={() => inputRef.current?.click()}
      >
        <div className="mds-upload-icon" aria-hidden="true">
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 3v12" /><path d="M7 8l5-5 5 5" /><path d="M4 15v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3" />
          </svg>
        </div>
        <div className="mds-upload-text">Drag files here or click to browse</div>
        <div className="mds-upload-hint">{accept !== "*" ? `Accepts: ${accept}` : "Any file type"}</div>
      </div>
      <input
        ref={inputRef}
        type="file"
        className="mds-upload-input"
        multiple={multiple}
        accept={accept}
        onChange={(e) => handleFiles(e.target.files)}
      />
      {files.length > 0 && (
        <ul className="mds-file-list">
          {files.map((f, i) => (
            <li key={i} className="mds-file-item">
              <span className="mds-file-name">{f.name}</span>
              <button className="mds-file-remove" onClick={() => removeFile(i)}>✕</button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
