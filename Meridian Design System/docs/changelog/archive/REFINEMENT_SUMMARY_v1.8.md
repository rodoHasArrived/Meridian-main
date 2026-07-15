# Meridian Design System — v1.8 Refinement Summary

> ⚠️ **SUPERSEDED.** The token values below — a soft `--shadow-card` with an inset highlight,
> 6–8px `--radius-card`, and the `#E8ECEF`/`#ECEFF3`-family light palette — were themselves
> replaced by the later **Concrete refresh**: flat surfaces (`--shadow-card: none`), a unified
> 2px radius across chips/controls/cards, and the current `#DEE3EA` canvas. See `readme.md` and
> `tokens/` for what's actually live today. Kept only as a historical record of the v1.8 pass —
> don't pull hex values or radii from this file.

**Bold systemic refresh** within institutional minimalism. Every token layer refined for sophistication, consistency, and personality.

---

## Typography Refinements

### Hierarchy Strengthened
- **Page title**: 22px → **24px** (clearer separation)
- **Section**: 15px → **16px** (tighter structure)
- **Card title**: 14px → **13px** + medium weight (refined compact)
- **Metric**: 24px → **28px** (operator focus, more prominent)
- **Data value**: 18px → **16px** (better proportion with metric)
- **Label**: 10px → **9px** (tighter small-caps, more defined)

### New tokens
- `--letter-spacing-label: 0.04em` — refined small-caps spacing
- `--letter-spacing-data: 0.01em` — tight tabular numerals
- `--weight-bold: 700` — for emphasis
- `--type-caption: 11px` (with line-height: 16px)

**Result**: Tighter, more intentional hierarchy. Labels breathe with refined spacing. Metrics command attention.

---

## Color Refinements

### Light Mode — Cooler sophistication
| Token | Old | New | Change |
|-------|-----|-----|--------|
| `--bg` | #ECEFF3 | #E8ECEF | Slightly cooler canvas |
| `--bg-light` | #FFFFFF | #FAFBFC | Off-white warmth (not clinical) |
| `--bg-medium` | #F5F7FA | #F3F5F7 | More subtle distinction |
| `--bg-hover` | #F1F4F7 | #EEF2F5 | Less jarring, sophisticated |
| `--bg-active` | #E6EEF5 | #E1EAF2 | Deeper engagement color |
| `--accent` | #2F6F8F | #2D5F7F | Deeper teal (more refined) |
| `--accent-dim` | #255B75 | #1F4A63 | Stronger pressed contrast |
| `--accent-hover` | #3B82A6 | #3A77A0 | Vibrant but controlled |
| `--text-primary` | #22272E | #1A1F26 | Deeper black (better contrast) |
| `--text-secondary` | #4D5967 | #434D57 | Refined muted |
| `--green` | #16885F | #1B7E5C | Deeper, more refined |
| `--red` | #BA3F55 | #A63D4A | Burgundy undertone |
| `--orange` | #B7791F | #A86F1A | Earthier amber |

### New semantic
- `--purple: #5B4BA3` — pending state (new)
- `--accent-ghost: #E8F0F7` — ultra-light ghost button background
- `--border-divider: #E5E9EE` — light section separator

### Dark Mode — Warm personality instead of flat flip
| Token | Old | New | Change |
|-------|-----|-----|--------|
| `--bg` | #0F1117 | #101316 | Slightly warm black (less blue) |
| `--bg-light` | #1C2128 | #1A1F26 | Warmer dark surface |
| `--text-primary` | #E6EAF0 | #E5EAEF | Warmer white |
| `--accent` | #58A6FF | #5B9FD9 | Cooler blue for night (better for eyes) |
| `--accent-hover` | #79C0FF | #6EB3F0 | Vibrant but refined |
| `--green` | #3FB950 | #4BAE82 | Teal-shifted (less neon) |
| `--red` | #F85149 | #E8636B | Burgundy instead of orange-red |
| `--orange` | #D29922 | #D4941D | Warmer, more refined |

**Result**: Dark mode has personality — warm backgrounds, cool accent for contrast. Not a flat brightness flip.

---

## Elevation & Spacing Refinements

### Radius — Micro refinement
| Token | Old | New | Change |
|-------|-----|-----|--------|
| `--radius-chip` | 4px | 3px | Tighter, more refined |
| `--radius-button` | 6px | 4px | Subtle refinement |
| `--radius-card` | 8px | 6px | Less rounded, more sophisticated |

### Spacing — Tighter rhythm
| Token | Old | New | Change |
|-------|-----|-----|--------|
| `--space-xs` | 4px | 3px | Tighter micro-spacing |
| `--space-sm` | 8px | 6px | Refined tight |
| New: `--space-2xl` | — | 32px | Major section breaks |

### Shadow — Refined layering
- `--shadow-card` now includes **inset light edge** (`inset 0 1px 0 rgba(255,255,255,0.4)`) for subtle depth
- `--shadow-menu` reduced from `0 4px 16px` to `0 4px 12px` (more restrained)
- **New**: `--shadow-inset: inset 0 1px 3px rgba(0,0,0,0.05)` for interior depth

**Result**: More intentional elevation. Cards feel layered, not flat or heavy.

---

## Borders — Refined language

### New token
- `--border-divider: #E5E9EE` — for light section separators

### Refined contrast
| Token | Old | New |
|-------|-----|-----|
| `--border` | #D7DCE2 | #CDD3DB |
| `--border-hover` | #B8C2CC | #A5B1BC |
| `--border-focus` | #2F6F8F | #2D5F7F (now tracks accent) |
| `--border-strong` | #AAB4BF | #9BA5B5 |

**Result**: Borders carry clearer intent — default whisper, hover clear, strong emphatic.

---

## Total Token Count

- **Before**: 204 tokens
- **After**: 217 tokens (+13 new)
- **New tokens**: `--accent-ghost`, `--purple`, `--border-divider`, `--space-2xl`, `--shadow-inset`, `--letter-spacing-label`, `--letter-spacing-data`, `--weight-bold`, `--type-caption` + dark equivalents

---

## Design Philosophy

**Refined institutional minimalism:**
- ✅ Deeper, more sophisticated palette (not brighter, smarter)
- ✅ Micro-refinements in radius & spacing (intentionality over defaults)
- ✅ Dark mode with personality (warm canvas, cool accent)
- ✅ Borders carry clear intent (whisper, hover, strong, divider)
- ✅ Shadows add subtle depth, not weight
- ✅ Typography hierarchy is tighter, more commanding

All changes maintain:
- ✅ Grid-aligned operator friendliness
- ✅ No new dependencies
- ✅ Full dark-mode + white-label tracking
- ✅ Component API stability (tokens only, no breaking changes)

---

## Files Modified

- `tokens/typography.css` — type ramp + new weights & letter-spacing
- `tokens/colors.css` — light mode palette + 4 new tokens
- `tokens/elevation.css` — refined radius/spacing + new shadow inset
- `tokens/colors-dark.css` — warm dark personality

---

## Next Steps for Consumers

1. **Update local copies** of the bundled system
2. **No breaking changes** — existing components work unchanged
3. **Optional**: Use new tokens (`--accent-ghost`, `--purple`, `--shadow-inset`) for new patterns
4. **Test dark mode** — personalities have changed, but tracking is automatic

The refinement is backward-compatible. All existing component code renders with the new tokens automatically.
