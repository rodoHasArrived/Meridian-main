Dashed drop zone — drag files onto it or click to browse. Renders its own selected-file list underneath, each with a remove button.

```jsx
<FileUpload label="Statement import" accept=".csv,.xlsx" onFilesSelected={setFiles} />
<FileUpload label="Single attachment" multiple={false} onFilesSelected={([f]) => setFile(f)} />
```

The drop-zone icon is an inline stroke SVG matching `EmptyState`'s icon set — never an emoji. `onFilesSelected` always receives the **full current array** (not just the delta), so treat it as the new source of truth rather than diffing it yourself.
