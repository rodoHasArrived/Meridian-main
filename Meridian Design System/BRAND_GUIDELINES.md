# Meridian Brand Guidelines

## Overview

Meridian is a professional .NET trading and fund operations platform. Our branding system combines geometric precision with modern aesthetics, reflecting the sophistication and reliability of institutional-grade financial software.

## Brand Assets

### Core Mark
- **meridian-mark.svg** - Primary brand mark (256×256px)
- **meridian-mark-monochrome.svg** - Single-color variant using `currentColor`
- **meridian-mark-light.svg** - Light theme variant for dark backgrounds
- **meridian-symbol.svg** - Simplified symbol optimized for small sizes and favicons

### Wordmarks
- **meridian-wordmark.svg** - Primary horizontal wordmark with descriptor
- **meridian-wordmark-stacked.svg** - Vertical stacked layout for narrow spaces

### Application Assets
- **meridian-tile.svg** - Square app icon with rounded corners (256×256px)
- **meridian-tile-256.png** - Raster version of app tile

### Background Assets
- **meridian-hero.svg** - Full-width hero background with data visualization theme

## Color Palette

### Primary Colors
- **Neutral Base**: `hsl(var(--foreground))` - Primary text and borders
- **Accent Cyan**: `var(--cyan-primary)` - Interactive elements and highlights
- **Data Green**: `var(--state-healthy-fg)` - Success states and signals

### Background Colors
- **Dark Background**: `hsl(var(--background))` - Primary background color
- **Deep Navy**: `hsl(var(--card))` - Secondary backgrounds
- **Grid Line**: `var(--border-color)` - Subtle structural elements

### Gradients
- **Signal Gradient**: Blue → Cyan → Green (representing data flow)
- **Beam Gradient**: Blue → Cyan → Green (representing energy/signal)
- **Glow Gradient**: Dark blue radial glow for depth

## Usage Guidelines

### Mark Usage
- **Minimum Size**: 48×48px for digital displays
- **Clear Space**: Maintain at least 10% of the mark's width as clear space on all sides
- **Backgrounds**: Use on neutral, dark, or branded backgrounds with sufficient contrast
- **Scaling**: Maintain 1:1 aspect ratio; never distort

### Wordmark Usage
- **Primary Application**: Use the full wordmark in main navigation and hero sections
- **Stacked Variant**: Use when horizontal space is constrained (sidebars, narrow cards)
- **Monochrome**: For print, single-color applications, and accessibility requirements
- **Light Variant**: For dark theme interfaces and low-light environments

### Application Tile
- **Icon Display**: Use for app icons, shortcuts, and platform branding
- **Sizes**: Provide at least 256×256px PNG and scalable SVG versions
- **Padding**: Add 8-16px padding around the mark when used as an app icon

### Hero Background
- **Full-Width**: Use for landing pages, dashboards, and prominent hero sections
- **Overlay**: Combine with content overlay (dark gradient) for text readability
- **Minimum Height**: 300px for mobile, 600px+ for desktop

## Color Combinations

### Recommended Pairings
| Use Case | Primary | Accent |
|----------|---------|--------|
| Default | `hsl(var(--foreground))` | `var(--cyan-primary)` |
| Success | `var(--state-healthy-fg)` | `var(--cyan-primary)` |
| Neutral | `hsl(var(--muted-foreground))` | `var(--border-color)` |
| Inverted | `hsl(var(--foreground))` | `var(--cyan-focus)` |

## Typography

### Font Stack
- **Display/Logo**: `'Space Grotesk', 'IBM Plex Sans', system-ui, sans-serif`
- **Body**: `'IBM Plex Sans', system-ui, sans-serif`
- **Monospace**: `'IBM Plex Mono', ui-monospace, monospace`

### Weight Recommendations
- **Headings**: 700 (bold)
- **Body**: 400 (regular)
- **Captions**: 400 (regular)
- **Logo**: 700 (bold)

## Do's and Don'ts

### Do
✓ Use the mark on contrasting backgrounds  
✓ Maintain proper clear space and aspect ratios  
✓ Apply consistent color usage across materials  
✓ Use the monochrome variant for accessibility  
✓ Scale assets proportionally  

### Don't
✗ Distort, rotate, or skew the mark  
✗ Add effects (shadows, glows) to the mark  
✗ Use unofficial colors without approval  
✗ Place the mark on insufficient contrast backgrounds  
✗ Reduce the mark below minimum sizes  

## Accessibility

- All SVG assets include descriptive `<title>` and `<desc>` elements
- The monochrome variant uses `currentColor` for dynamic theming
- Maintain minimum contrast ratios of 4.5:1 for body text
- Test color combinations with WCAG AA standards

## File Formats

- **SVG**: Preferred for all digital applications (scalable, accessible, small file size)
- **PNG**: Provided for raster formats (256×256px for app tiles)
- **Source**: All assets are vector-based and optimized for web

## Integration Tips

### Web
```html
<img src="meridian-mark.svg" alt="Meridian" />
<svg>
  <use href="meridian-mark.svg#logo" />
</svg>
```

### CSS Background
```css
.hero {
  background-image: url('meridian-hero.svg');
  background-size: cover;
  background-position: center;
}
```

### Theme Support
Use the monochrome variant with CSS to support light/dark modes:
```css
.dark .logo {
  color: hsl(var(--foreground));
}
.light .logo {
  color: hsl(var(--background));
}
```

---

**Last Updated**: 2026-05-10  
**Version**: 2.0  
**Status**: Active
