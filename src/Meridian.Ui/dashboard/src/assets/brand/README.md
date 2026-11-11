# Meridian Brand Assets

Identity v1.0 (Datum + Field), copied from `Meridian Design System/assets/brand/`. The package
copy is canonical: `scripts/check_contrast.py` in the package fails when a file here differs
from it. Do not edit these files in place; change the package file and copy it over.

## Files

| File | Purpose | Size |
|------|---------|------|
| `meridian-mark.svg` | Primary mark on paper/light surfaces | 240×240 |
| `meridian-mark-light.svg` | Mark on graphite/dark surfaces (the workstation topbar) | 240×240 |
| `meridian-mark-monochrome.svg` | One-color mark | 240×240 |
| `meridian-symbol.svg` | Simplified symbol for small sizes | 256×256 |
| `meridian-wordmark.svg` | Wordmark only | 689×152 |
| `meridian-wordmark-stacked.svg` | Centered stacked lockup | 900×680 |
| `meridian-tile.svg` | App tile | 256×256 |
| `meridian-tile-256.png` | App tile, raster | 256×256 |
| `meridian-hero.svg` | Wide editorial identity composition | 1600×720 |

Wordmark lettering is stored as vector outlines, so no font file is needed to render a mark.
The few live `<text>` runs in the hero name IBM Plex Sans with an Arial fallback.

## Usage rules

1. Inside the workstation use the square mark alone, or no mark at all.
2. Use the `-light` variant on graphite and dark surfaces.
3. Terracotta (`#C56343`) is reserved for the displaced signal point and rare brand emphasis,
   never for positive/negative values.
4. The tile has opaque paper corners; do not place it on a graphite surface.
5. Import assets through `src/design-system/assets.ts`, not by path.
