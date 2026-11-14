repo: rodoHasArrived/Meridian-main
branch: main
path: Meridian Design System

## Last sync
date: 2026-11-02T22:34:00Z

### Updated in this project
- Checked upstream `main` › `Meridian Design System/`: its CHANGELOG tops out at **2.0.0 (2026-09-22)** — the warm-graphite/copper (`#A85436`) identity, platform-only fonts, flat `--bg-ambient`, catalogue restyle (`colors_and_type.css`, `preview/`, `ui_kits/`)
- This project is **ahead** on a separate line: release.json 4.3.0 stable (2026-09-10), Programmed Institutionalism **v2.1** tokens (terracotta `#C56343` signal + blue interaction family), `components/decision/`, `api/`, 334 exports, 28 templates
- Nothing pulled: every upstream 2.0.0 change is superseded here (v2.1 ⊃ v2.0 palette) or is catalogue tooling this port deliberately omits (`preview/`, `ui_kits/`, `scripts/`, `tests/*.py`). No screens rebuilt.
- Open question for the maintainer: push this 4.3.0 tree back to the repo, or treat upstream's 2.0.0 folder as the canonical line going forward

## Sync history
### 2026-08-11
- Ported the packaged Meridian Design System v3.1.0 in full from the attached local folder `Meridian_Design_System_3.1.0/` — tokens, 224 exported components across 7 domains, 97 Design System cards, 28 templates, guidelines, docs, brand marks and the 48-icon line set
- Rewired the compiled bundle namespace to this project (`window.MeridianDesignSystem_205d17`) across every card, template, screen and `ds-base.js`
- Moved the 21 production screens from upstream's `meridian-screens/` tier into `templates/` (their `@screen` markers became `@template`) so the template picker actually indexes them
- Left the upstream Python governance/QA tooling (`scripts/`, `tests/`, `qa/`) out — CI machinery, not design content

## Screen map
| Project screen | Repo files |
| --- | --- |
| templates/* (7 archetypes) | Meridian Design System/templates/* |
| templates/* (21 production screens) | Meridian Design System/meridian-screens/* |
| components/{core,data,charts,accounting,trading,operations,shell} | Meridian Design System/components/* |
| components/decision | (local only — not in upstream) |
| tokens/, styles.css | Meridian Design System/tokens/, styles.css |
| guidelines/, docs/ cards | Meridian Design System/{guidelines,docs}/ |
| assets/brand, assets/icons | Meridian Design System/assets/ |

Canonical visual source in the monorepo: `src/Meridian.Wpf/Styles/` (ThemeTokens.xaml, ThemeTypography.xaml, ThemeSurfaces.xaml, ThemeControls.xaml) and `src/Meridian.Ui/dashboard/src/styles/index.css`. No commit sha recorded — the original port came from a packaged folder, and this check compared tree contents, not commits.
