#!/bin/bash
# create-meridian-workstation — scaffold a new consuming project in seconds
# Usage: bash create-workstation.sh my-workstation-name

set -e

PROJECT_NAME="${1:-my-workstation}"
PROJECT_DIR="./$PROJECT_NAME"

if [ -d "$PROJECT_DIR" ]; then
  echo "❌ Directory $PROJECT_DIR already exists"
  exit 1
fi

echo "📦 Creating Meridian workstation: $PROJECT_NAME"
mkdir -p "$PROJECT_DIR"

# Folder structure
mkdir -p "$PROJECT_DIR/screens"
mkdir -p "$PROJECT_DIR/components"
mkdir -p "$PROJECT_DIR/assets/icons"
mkdir -p "$PROJECT_DIR/assets/images"

# HTML entry point
cat > "$PROJECT_DIR/index.html" << 'ENDHTML'
<!DOCTYPE html>
<html lang="en" data-brand="indigo">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Meridian Workstation</title>
  <!-- Design system styles -->
  <link rel="stylesheet" href="_ds/styles.css">
  <style>
    body {
      margin: 0;
      padding: 0;
      font-family: var(--font-body, "Segoe UI Variable", system-ui, sans-serif);
      background: var(--bg, #ECEFF3);
      color: var(--text-primary, #22272E);
    }
    main { padding: 16px; }
  </style>
</head>
<body>
  <main id="app">
    <h1>Meridian Workstation</h1>
    <p>Edit <code>index.html</code> or <code>screens/</code> to get started.</p>
  </main>

  <!-- Design system bundle -->
  <script src="_ds/_ds_bundle.js"></script>
  <!-- Your app logic -->
  <script>
    const { Button, Input, Badge, Modal } = window.MeridianDesignSystem_4f61be;
    console.log("✅ Design system loaded", { Button, Input, Badge, Modal });
  </script>
</body>
</html>
ENDHTML

# Example screen component
cat > "$PROJECT_DIR/screens/ExampleScreen.html" << 'ENDSCREEN'
<!DOCTYPE html>
<html lang="en" data-brand="indigo">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Example Screen</title>
  <link rel="stylesheet" href="../_ds/styles.css">
  <style>
    body {
      margin: 0;
      padding: 16px;
      font-family: var(--font-body, "Segoe UI Variable", system-ui, sans-serif);
      background: var(--bg, #ECEFF3);
      color: var(--text-primary, #22272E);
    }
    .container { max-width: 1200px; margin: 0 auto; }
    .section { margin-bottom: 32px; }
    .section h2 { font-size: 18px; font-weight: 600; margin-bottom: 12px; }
    .controls { display: flex; gap: 12px; flex-wrap: wrap; }
    input, select { padding: 8px 12px; border: 1px solid var(--border, #D7DCE2); background: var(--bg-light, #fff); font-size: 13px; }
    input:focus, select:focus { outline: none; border-color: var(--border-focus, #2F6F8F); box-shadow: 0 0 0 2px rgba(47,111,143,.2); }
  </style>
</head>
<body>
  <div class="container">
    <h1>Example Screen</h1>
    <p>This demonstrates using 3–4 core design-system components.</p>

    <div class="section">
      <h2>Form Controls</h2>
      <div class="controls">
        <input type="text" placeholder="Text input">
        <select>
          <option>Option 1</option>
          <option>Option 2</option>
          <option>Option 3</option>
        </select>
        <button id="btn-primary">Primary Button</button>
        <button id="btn-secondary">Secondary Button</button>
        <button id="btn-ghost">Ghost Button</button>
      </div>
    </div>

    <div class="section">
      <h2>Data Display</h2>
      <table style="width:100%; border:1px solid var(--border); border-collapse:collapse; font-size:13px;">
        <thead style="background:var(--bg-medium,#F5F7FA);">
          <tr>
            <th style="padding:9px 12px; text-align:left; border-bottom:1px solid var(--border);">Name</th>
            <th style="padding:9px 12px; text-align:left; border-bottom:1px solid var(--border);">Value</th>
            <th style="padding:9px 12px; text-align:right; border-bottom:1px solid var(--border);">Amount</th>
          </tr>
        </thead>
        <tbody>
          <tr style="border-bottom:1px solid var(--border);">
            <td style="padding:10px 12px;">Item 1</td>
            <td style="padding:10px 12px;">Active</td>
            <td style="padding:10px 12px; text-align:right; font-family:var(--font-data);">$1,234.56</td>
          </tr>
          <tr style="border-bottom:1px solid var(--border);">
            <td style="padding:10px 12px;">Item 2</td>
            <td style="padding:10px 12px;">Pending</td>
            <td style="padding:10px 12px; text-align:right; font-family:var(--font-data);">$2,345.67</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="section">
      <h2>Status & Feedback</h2>
      <div style="display:flex; gap:12px; flex-wrap:wrap;">
        <span style="display:inline-flex; align-items:center; gap:6px; padding:4px 10px; background:var(--bg-light); border:1px solid var(--border); border-radius:4px; font-size:12px;">
          ✓ Success
        </span>
        <span style="display:inline-flex; align-items:center; gap:6px; padding:4px 10px; background:var(--bg-light); border:1px solid var(--border); border-radius:4px; font-size:12px; color:var(--red);">
          ⚠ Warning
        </span>
        <span style="display:inline-flex; align-items:center; gap:6px; padding:4px 10px; background:var(--bg-light); border:1px solid var(--border); border-radius:4px; font-size:12px; color:var(--amber);">
          ℹ Info
        </span>
      </div>
    </div>
  </div>

  <script src="../_ds/_ds_bundle.js"></script>
  <script>
    const { Button } = window.MeridianDesignSystem_4f61be;
    document.getElementById("btn-primary").style.background = "var(--accent, #2F6F8F)";
    document.getElementById("btn-primary").style.color = "white";
    document.getElementById("btn-primary").style.border = "none";
    document.getElementById("btn-primary").style.padding = "8px 16px";
    document.getElementById("btn-primary").style.cursor = "pointer";
    document.getElementById("btn-primary").addEventListener("click", () => alert("Primary button clicked!"));

    ["btn-secondary", "btn-ghost"].forEach(id => {
      const btn = document.getElementById(id);
      btn.style.padding = "8px 16px";
      btn.style.cursor = "pointer";
      btn.style.background = "var(--bg-light)";
      btn.style.border = "1px solid var(--border)";
      btn.addEventListener("click", () => alert(btn.textContent + " clicked!"));
    });
  </script>
</body>
</html>
ENDSCREEN

# README
cat > "$PROJECT_DIR/README.md" << 'ENDREADME'
# Meridian Workstation: $PROJECT_NAME

A consuming project built with the Meridian Design System.

## Quick Start

1. **Link the design system**: Copy or symlink the Meridian DS folder:
   ```bash
   ln -s /path/to/meridian-design-system _ds
   ```
   Or copy `_ds_bundle.js`, `styles.css`, and fonts manually.

2. **Open `index.html`** in a browser to see the example screen.

3. **Build your screens** in the `screens/` folder. Each screen can be a standalone `.html` file or a component mounted via React/Vue.

4. **Use design-system components**:
   ```js
   const { Button, Input, Modal } = window.MeridianDesignSystem_4f61be;
   ```

## Folder Structure

```
my-workstation/
├── index.html              # Entry point
├── screens/                # Screen components (.html or .jsx)
│   └── ExampleScreen.html
├── components/             # Custom app components
├── assets/                 # Images, icons, fonts
└── _ds/                    # Design system (linked or copied)
    ├── styles.css
    ├── _ds_bundle.js
    └── tokens/
```

## Component API

Full component documentation is in the Meridian Design System repo under `components/`. Each component has JSDoc comments and example cards.

**Common components:**
- `Button` — primary, secondary, ghost variants
- `Input` — text field with optional error state
- `Select` — single-select dropdown
- `Modal`, `ModalForm` — dialog + form modal
- `Badge` — status/category badge
- `DenseDataTable` — institutional data grid
- `Tabs`, `TabPanel` — tabbed interface

## Theming

Override the brand accent color:
```html
<html data-brand="emerald">  <!-- indigo | emerald | rose | slate | cyan | amber -->
```

Or density:
```html
<body data-theme-density="compact">  <!-- terminal | compact (default) | spacious -->
```

## Support

See `../readme.md` in the Meridian Design System for full API docs and usage patterns.
ENDREADME

# ds-base.js (for template-style loading)
cat > "$PROJECT_DIR/_ds-base.js" << 'ENDDS'
// ds-base.js — loads the design system bundle relative to this file
(function() {
  const base = "_ds";  // Adjust if _ds folder is in a different location

  // Load styles
  const link = document.createElement("link");
  link.rel = "stylesheet";
  link.href = base + "/styles.css";
  document.head.appendChild(link);

  // Load bundle
  const script = document.createElement("script");
  script.src = base + "/_ds_bundle.js";
  script.onerror = function() {
    console.error("Failed to load " + script.src + ". Make sure _ds folder is linked or copied.");
  };
  document.head.appendChild(script);
})();
ENDDS

# .gitignore
cat > "$PROJECT_DIR/.gitignore" << 'ENDGIT'
node_modules/
_ds/  # If _ds is linked, don't commit the symlink
dist/
build/
.DS_Store
*.log
ENDGIT

# package.json (optional, for build tooling)
cat > "$PROJECT_DIR/package.json" << 'ENDJSON'
{
  "name": "$PROJECT_NAME",
  "version": "1.0.0",
  "description": "A Meridian workstation",
  "scripts": {
    "dev": "npx http-server",
    "build": "echo 'Add your build script here'"
  },
  "keywords": ["meridian", "design-system", "workstation"],
  "author": "",
  "license": "MIT"
}
ENDJSON

echo ""
echo "✅ Project created: $PROJECT_DIR"
echo ""
echo "Next steps:"
echo "  1. Link the design system:"
echo "     cd $PROJECT_DIR && ln -s /path/to/meridian-design-system _ds"
echo "  2. Open index.html in your browser"
echo "  3. Edit screens/ and components/ to build your workstation"
echo ""
echo "👉 For a quick preview, open: $PROJECT_DIR/screens/ExampleScreen.html"
echo ""
ENDHTML
