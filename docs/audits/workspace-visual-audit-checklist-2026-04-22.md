# Workspace Visual Audit Checklist (2026-04-22)

## Scope
- Research workspace shell (`ResearchWorkspaceShellPage.xaml`)
- Trading workspace shell (`TradingWorkspaceShellPage.xaml`)
- Data Operations workspace shell (`DataOperationsWorkspaceShellPage.xaml`)
- Governance workspace shell (`GovernanceWorkspaceShellPage.xaml`)

## Checklist
- [x] Shared spacing/rhythm tokens are available for section gaps, card paddings, and divider margins.
- [x] Shared card composition patterns exist for header/body/footer alignment.
- [x] Badge semantics are mapped to Info / Warning / Success / Danger resources.
- [x] Summary tiles consistently use `label -> value -> trend/detail` ordering.
- [x] Workspace shell pages consume the standardized styles where practical.

## Outliers Identified for Follow-up Cleanup
1. **Research hero identity card** uses a bespoke dark treatment (`#12253A` / `#22486B`) that does not yet consume the semantic card/badge token set.
2. **Research informational callout in Run Studio** still uses custom accent border/background values and should migrate to a shared semantic callout style.
3. **Governance empty-state warning icon card** uses a custom border+icon composition rather than a reusable warning callout style.
4. **Trading context card and active run card** are bespoke card compositions; they are close to shared patterns but still manually define padding and internal spacing.

## Notes
- This pass normalizes the most repeated shell-level card/badge/tile patterns first.
- Outliers are intentionally preserved to avoid altering contextual visual hierarchy in one step.
