# Documentation Rules Report

*Generated: 2026-03-22 03:10:50 UTC*

## Summary

| Metric | Count |
|--------|-------|
| Rules loaded | 13 |
| Total file checks | 415 |
| Passed | 392 |
| Failed | 23 |
| Errors | 3 |
| Warnings | 10 |
| Info | 10 |

**Result: FAIL** (3 error(s) found)

## Violations

| Severity | File | Rule | Suggestion |
|----------|------|------|------------|
| **ERROR** | `CLAUDE.md` | CLAUDE.md has quick commands | Missing required section heading: `Repository Structure` |
| **ERROR** | `docs\adr\ADR-015-platform-restructuring.md` | ADR has status field | File should contain: `Status:` |
| **ERROR** | `docs\adr\README.md` | ADR has status field | File should contain: `Status:` |
| **WARNING** | `docs\adr\ADR-015-platform-restructuring.md` | ADR has date | File should have a line matching: `Date:|date:` |
| **WARNING** | `docs\adr\README.md` | ADR has date | File should have a line matching: `Date:|date:` |
| **WARNING** | `docs\adr\README.md` | ADR has context section | Missing required section heading: `Context` |
| **WARNING** | `docs\adr\README.md` | ADR has decision section | Missing required section heading: `Decision` |
| **WARNING** | `docs\generated\documentation-coverage.md` | Generated docs have auto-generated notice | File should contain: `auto-generated` |
| **WARNING** | `docs\generated\interfaces.md` | Generated docs have auto-generated notice | File should contain: `auto-generated` |
| **WARNING** | `docs\generated\project-context.md` | Generated docs have auto-generated notice | File should contain: `auto-generated` |
| **WARNING** | `docs\operations\operator-runbook.md` | Operator runbook has troubleshooting section | Missing required section heading: `Troubleshooting` |
| **WARNING** | `docs\providers\alpaca-setup.md` | Provider docs mention setup steps | Missing required section heading: `Prerequisites` |
| **WARNING** | `docs\providers\interactive-brokers-setup.md` | Provider docs mention setup steps | Missing required section heading: `Configuration` |
| **INFO** | `docs\HELP.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\development\adding-custom-rules.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\development\otlp-trace-visualization.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\docfx\README.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\evaluations\desktop-platform-improvements-implementation-guide.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\getting-started\README.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\operations\deployment.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\operations\operator-runbook.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\reference\api-reference.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |
| **INFO** | `docs\reference\environment-variables.md` | No hardcoded localhost URLs in docs | File should NOT contain: `http://localhost` |

## Passed Checks

| File | Rule | Severity |
|------|------|----------|
| `README.md` | README has project description | error |
| `docs\DEPENDENCIES.md` | No hardcoded API keys in docs | error |
| `docs\DEPENDENCIES.md` | No hardcoded localhost URLs in docs | info |
| `docs\HELP.md` | No hardcoded API keys in docs | error |
| `docs\README.md` | No hardcoded API keys in docs | error |
| `docs\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\001-provider-abstraction.md` | ADR has context section | warning |
| `docs\adr\001-provider-abstraction.md` | ADR has date | warning |
| `docs\adr\001-provider-abstraction.md` | ADR has decision section | warning |
| `docs\adr\001-provider-abstraction.md` | ADR has status field | error |
| `docs\adr\001-provider-abstraction.md` | No hardcoded API keys in docs | error |
| `docs\adr\001-provider-abstraction.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\002-tiered-storage-architecture.md` | ADR has context section | warning |
| `docs\adr\002-tiered-storage-architecture.md` | ADR has date | warning |
| `docs\adr\002-tiered-storage-architecture.md` | ADR has decision section | warning |
| `docs\adr\002-tiered-storage-architecture.md` | ADR has status field | error |
| `docs\adr\002-tiered-storage-architecture.md` | No hardcoded API keys in docs | error |
| `docs\adr\002-tiered-storage-architecture.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\003-microservices-decomposition.md` | ADR has context section | warning |
| `docs\adr\003-microservices-decomposition.md` | ADR has date | warning |
| `docs\adr\003-microservices-decomposition.md` | ADR has decision section | warning |
| `docs\adr\003-microservices-decomposition.md` | ADR has status field | error |
| `docs\adr\003-microservices-decomposition.md` | No hardcoded API keys in docs | error |
| `docs\adr\003-microservices-decomposition.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\004-async-streaming-patterns.md` | ADR has context section | warning |
| `docs\adr\004-async-streaming-patterns.md` | ADR has date | warning |
| `docs\adr\004-async-streaming-patterns.md` | ADR has decision section | warning |
| `docs\adr\004-async-streaming-patterns.md` | ADR has status field | error |
| `docs\adr\004-async-streaming-patterns.md` | No hardcoded API keys in docs | error |
| `docs\adr\004-async-streaming-patterns.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\005-attribute-based-discovery.md` | ADR has context section | warning |
| `docs\adr\005-attribute-based-discovery.md` | ADR has date | warning |
| `docs\adr\005-attribute-based-discovery.md` | ADR has decision section | warning |
| `docs\adr\005-attribute-based-discovery.md` | ADR has status field | error |
| `docs\adr\005-attribute-based-discovery.md` | No hardcoded API keys in docs | error |
| `docs\adr\005-attribute-based-discovery.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\006-domain-events-polymorphic-payload.md` | ADR has context section | warning |
| `docs\adr\006-domain-events-polymorphic-payload.md` | ADR has date | warning |
| `docs\adr\006-domain-events-polymorphic-payload.md` | ADR has decision section | warning |
| `docs\adr\006-domain-events-polymorphic-payload.md` | ADR has status field | error |
| `docs\adr\006-domain-events-polymorphic-payload.md` | No hardcoded API keys in docs | error |
| `docs\adr\006-domain-events-polymorphic-payload.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\007-write-ahead-log-durability.md` | ADR has context section | warning |
| `docs\adr\007-write-ahead-log-durability.md` | ADR has date | warning |
| `docs\adr\007-write-ahead-log-durability.md` | ADR has decision section | warning |
| `docs\adr\007-write-ahead-log-durability.md` | ADR has status field | error |
| `docs\adr\007-write-ahead-log-durability.md` | No hardcoded API keys in docs | error |
| `docs\adr\007-write-ahead-log-durability.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\008-multi-format-composite-storage.md` | ADR has context section | warning |
| `docs\adr\008-multi-format-composite-storage.md` | ADR has date | warning |
| `docs\adr\008-multi-format-composite-storage.md` | ADR has decision section | warning |
| `docs\adr\008-multi-format-composite-storage.md` | ADR has status field | error |
| `docs\adr\008-multi-format-composite-storage.md` | No hardcoded API keys in docs | error |
| `docs\adr\008-multi-format-composite-storage.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\009-fsharp-interop.md` | ADR has context section | warning |
| `docs\adr\009-fsharp-interop.md` | ADR has date | warning |
| `docs\adr\009-fsharp-interop.md` | ADR has decision section | warning |
| `docs\adr\009-fsharp-interop.md` | ADR has status field | error |
| `docs\adr\009-fsharp-interop.md` | No hardcoded API keys in docs | error |
| `docs\adr\009-fsharp-interop.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\010-httpclient-factory.md` | ADR has context section | warning |
| `docs\adr\010-httpclient-factory.md` | ADR has date | warning |
| `docs\adr\010-httpclient-factory.md` | ADR has decision section | warning |
| `docs\adr\010-httpclient-factory.md` | ADR has status field | error |
| `docs\adr\010-httpclient-factory.md` | No hardcoded API keys in docs | error |
| `docs\adr\010-httpclient-factory.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\011-centralized-configuration-and-credentials.md` | ADR has context section | warning |
| `docs\adr\011-centralized-configuration-and-credentials.md` | ADR has date | warning |
| `docs\adr\011-centralized-configuration-and-credentials.md` | ADR has decision section | warning |
| `docs\adr\011-centralized-configuration-and-credentials.md` | ADR has status field | error |
| `docs\adr\011-centralized-configuration-and-credentials.md` | No hardcoded API keys in docs | error |
| `docs\adr\011-centralized-configuration-and-credentials.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\012-monitoring-and-alerting-pipeline.md` | ADR has context section | warning |
| `docs\adr\012-monitoring-and-alerting-pipeline.md` | ADR has date | warning |
| `docs\adr\012-monitoring-and-alerting-pipeline.md` | ADR has decision section | warning |
| `docs\adr\012-monitoring-and-alerting-pipeline.md` | ADR has status field | error |
| `docs\adr\012-monitoring-and-alerting-pipeline.md` | No hardcoded API keys in docs | error |
| `docs\adr\012-monitoring-and-alerting-pipeline.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\013-bounded-channel-policy.md` | ADR has context section | warning |
| `docs\adr\013-bounded-channel-policy.md` | ADR has date | warning |
| `docs\adr\013-bounded-channel-policy.md` | ADR has decision section | warning |
| `docs\adr\013-bounded-channel-policy.md` | ADR has status field | error |
| `docs\adr\013-bounded-channel-policy.md` | No hardcoded API keys in docs | error |
| `docs\adr\013-bounded-channel-policy.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\014-json-source-generators.md` | ADR has context section | warning |
| `docs\adr\014-json-source-generators.md` | ADR has date | warning |
| `docs\adr\014-json-source-generators.md` | ADR has decision section | warning |
| `docs\adr\014-json-source-generators.md` | ADR has status field | error |
| `docs\adr\014-json-source-generators.md` | No hardcoded API keys in docs | error |
| `docs\adr\014-json-source-generators.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\015-strategy-execution-contract.md` | ADR has context section | warning |
| `docs\adr\015-strategy-execution-contract.md` | ADR has date | warning |
| `docs\adr\015-strategy-execution-contract.md` | ADR has decision section | warning |
| `docs\adr\015-strategy-execution-contract.md` | ADR has status field | error |
| `docs\adr\015-strategy-execution-contract.md` | No hardcoded API keys in docs | error |
| `docs\adr\015-strategy-execution-contract.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\016-platform-architecture-migration.md` | ADR has context section | warning |
| `docs\adr\016-platform-architecture-migration.md` | ADR has date | warning |
| `docs\adr\016-platform-architecture-migration.md` | ADR has decision section | warning |
| `docs\adr\016-platform-architecture-migration.md` | ADR has status field | error |
| `docs\adr\016-platform-architecture-migration.md` | No hardcoded API keys in docs | error |
| `docs\adr\016-platform-architecture-migration.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\ADR-015-platform-restructuring.md` | ADR has context section | warning |
| `docs\adr\ADR-015-platform-restructuring.md` | ADR has decision section | warning |
| `docs\adr\ADR-015-platform-restructuring.md` | No hardcoded API keys in docs | error |
| `docs\adr\ADR-015-platform-restructuring.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\README.md` | No hardcoded API keys in docs | error |
| `docs\adr\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\adr\_template.md` | ADR has context section | warning |
| `docs\adr\_template.md` | ADR has date | warning |
| `docs\adr\_template.md` | ADR has decision section | warning |
| `docs\adr\_template.md` | ADR has status field | error |
| `docs\adr\_template.md` | No hardcoded API keys in docs | error |
| `docs\adr\_template.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\README.md` | No hardcoded API keys in docs | error |
| `docs\ai\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\agents\README.md` | No hardcoded API keys in docs | error |
| `docs\ai\agents\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\ai-known-errors.md` | No hardcoded API keys in docs | error |
| `docs\ai\ai-known-errors.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\claude\CLAUDE.actions.md` | AI guides reference the project name | info |
| `docs\ai\claude\CLAUDE.actions.md` | No hardcoded API keys in docs | error |
| `docs\ai\claude\CLAUDE.actions.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\claude\CLAUDE.api.md` | AI guides reference the project name | info |
| `docs\ai\claude\CLAUDE.api.md` | No hardcoded API keys in docs | error |
| `docs\ai\claude\CLAUDE.api.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\claude\CLAUDE.fsharp.md` | AI guides reference the project name | info |
| `docs\ai\claude\CLAUDE.fsharp.md` | No hardcoded API keys in docs | error |
| `docs\ai\claude\CLAUDE.fsharp.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\claude\CLAUDE.providers.md` | AI guides reference the project name | info |
| `docs\ai\claude\CLAUDE.providers.md` | No hardcoded API keys in docs | error |
| `docs\ai\claude\CLAUDE.providers.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\claude\CLAUDE.repo-updater.md` | AI guides reference the project name | info |
| `docs\ai\claude\CLAUDE.repo-updater.md` | No hardcoded API keys in docs | error |
| `docs\ai\claude\CLAUDE.repo-updater.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\claude\CLAUDE.storage.md` | AI guides reference the project name | info |
| `docs\ai\claude\CLAUDE.storage.md` | No hardcoded API keys in docs | error |
| `docs\ai\claude\CLAUDE.storage.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\claude\CLAUDE.structure.md` | AI guides reference the project name | info |
| `docs\ai\claude\CLAUDE.structure.md` | No hardcoded API keys in docs | error |
| `docs\ai\claude\CLAUDE.structure.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\claude\CLAUDE.testing.md` | AI guides reference the project name | info |
| `docs\ai\claude\CLAUDE.testing.md` | No hardcoded API keys in docs | error |
| `docs\ai\claude\CLAUDE.testing.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\copilot\ai-sync-workflow.md` | No hardcoded API keys in docs | error |
| `docs\ai\copilot\ai-sync-workflow.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\copilot\instructions.md` | No hardcoded API keys in docs | error |
| `docs\ai\copilot\instructions.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\instructions\README.md` | No hardcoded API keys in docs | error |
| `docs\ai\instructions\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\prompts\README.md` | No hardcoded API keys in docs | error |
| `docs\ai\prompts\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\ai\skills\README.md` | No hardcoded API keys in docs | error |
| `docs\ai\skills\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\README.md` | No hardcoded API keys in docs | error |
| `docs\architecture\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\c4-diagrams.md` | No hardcoded API keys in docs | error |
| `docs\architecture\c4-diagrams.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\crystallized-storage-format.md` | No hardcoded API keys in docs | error |
| `docs\architecture\crystallized-storage-format.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\desktop-layers.md` | No hardcoded API keys in docs | error |
| `docs\architecture\desktop-layers.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\deterministic-canonicalization.md` | No hardcoded API keys in docs | error |
| `docs\architecture\deterministic-canonicalization.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\domains.md` | No hardcoded API keys in docs | error |
| `docs\architecture\domains.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\layer-boundaries.md` | No hardcoded API keys in docs | error |
| `docs\architecture\layer-boundaries.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\overview.md` | No hardcoded API keys in docs | error |
| `docs\architecture\overview.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\provider-management.md` | No hardcoded API keys in docs | error |
| `docs\architecture\provider-management.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\storage-design.md` | No hardcoded API keys in docs | error |
| `docs\architecture\storage-design.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\ui-redesign.md` | No hardcoded API keys in docs | error |
| `docs\architecture\ui-redesign.md` | No hardcoded localhost URLs in docs | info |
| `docs\architecture\why-this-architecture.md` | No hardcoded API keys in docs | error |
| `docs\architecture\why-this-architecture.md` | No hardcoded localhost URLs in docs | info |
| `docs\audits\AUDIT_REPORT.md` | No hardcoded API keys in docs | error |
| `docs\audits\AUDIT_REPORT.md` | No hardcoded localhost URLs in docs | info |
| `docs\audits\CODE_REVIEW_2026-03-16.md` | No hardcoded API keys in docs | error |
| `docs\audits\CODE_REVIEW_2026-03-16.md` | No hardcoded localhost URLs in docs | info |
| `docs\audits\FURTHER_SIMPLIFICATION_OPPORTUNITIES.md` | No hardcoded API keys in docs | error |
| `docs\audits\FURTHER_SIMPLIFICATION_OPPORTUNITIES.md` | No hardcoded localhost URLs in docs | info |
| `docs\audits\README.md` | No hardcoded API keys in docs | error |
| `docs\audits\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\README.md` | No hardcoded API keys in docs | error |
| `docs\development\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\adding-custom-rules.md` | No hardcoded API keys in docs | error |
| `docs\development\build-observability.md` | No hardcoded API keys in docs | error |
| `docs\development\build-observability.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\central-package-management.md` | No hardcoded API keys in docs | error |
| `docs\development\central-package-management.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\desktop-testing-guide.md` | No hardcoded API keys in docs | error |
| `docs\development\desktop-testing-guide.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\documentation-automation.md` | No hardcoded API keys in docs | error |
| `docs\development\documentation-automation.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\documentation-contribution-guide.md` | No hardcoded API keys in docs | error |
| `docs\development\documentation-contribution-guide.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\expanding-scripts.md` | No hardcoded API keys in docs | error |
| `docs\development\expanding-scripts.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\fsharp-decision-rule.md` | No hardcoded API keys in docs | error |
| `docs\development\fsharp-decision-rule.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\github-actions-summary.md` | No hardcoded API keys in docs | error |
| `docs\development\github-actions-summary.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\github-actions-testing.md` | No hardcoded API keys in docs | error |
| `docs\development\github-actions-testing.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\otlp-trace-visualization.md` | No hardcoded API keys in docs | error |
| `docs\development\policies\desktop-support-policy.md` | No hardcoded API keys in docs | error |
| `docs\development\policies\desktop-support-policy.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\provider-implementation.md` | No hardcoded API keys in docs | error |
| `docs\development\provider-implementation.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\refactor-map.md` | No hardcoded API keys in docs | error |
| `docs\development\refactor-map.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\repository-organization-guide.md` | No hardcoded API keys in docs | error |
| `docs\development\repository-organization-guide.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\tooling-workflow-backlog.md` | No hardcoded API keys in docs | error |
| `docs\development\tooling-workflow-backlog.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\ui-fixture-mode-guide.md` | No hardcoded API keys in docs | error |
| `docs\development\ui-fixture-mode-guide.md` | No hardcoded localhost URLs in docs | info |
| `docs\development\wpf-implementation-notes.md` | No hardcoded API keys in docs | error |
| `docs\development\wpf-implementation-notes.md` | No hardcoded localhost URLs in docs | info |
| `docs\diagrams\README.md` | No hardcoded API keys in docs | error |
| `docs\diagrams\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\diagrams\uml\README.md` | No hardcoded API keys in docs | error |
| `docs\diagrams\uml\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\docfx\README.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\2026-03-brainstorm-next-frontier.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\2026-03-brainstorm-next-frontier.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\README.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\assembly-performance-opportunities.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\assembly-performance-opportunities.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\data-quality-monitoring-evaluation.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\data-quality-monitoring-evaluation.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\desktop-improvements-executive-summary.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\desktop-improvements-executive-summary.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\desktop-platform-improvements-implementation-guide.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\high-impact-improvement-brainstorm-2026-03.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\high-impact-improvement-brainstorm-2026-03.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\high-value-low-cost-improvements-brainstorm.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\high-value-low-cost-improvements-brainstorm.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\historical-data-providers-evaluation.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\historical-data-providers-evaluation.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\ingestion-orchestration-evaluation.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\ingestion-orchestration-evaluation.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\nautilus-inspired-restructuring-proposal.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\nautilus-inspired-restructuring-proposal.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\operational-readiness-evaluation.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\operational-readiness-evaluation.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\quant-script-blueprint-brainstorm.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\quant-script-blueprint-brainstorm.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\realtime-streaming-architecture-evaluation.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\realtime-streaming-architecture-evaluation.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\storage-architecture-evaluation.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\storage-architecture-evaluation.md` | No hardcoded localhost URLs in docs | info |
| `docs\evaluations\windows-desktop-provider-configurability-assessment.md` | No hardcoded API keys in docs | error |
| `docs\evaluations\windows-desktop-provider-configurability-assessment.md` | No hardcoded localhost URLs in docs | info |
| `docs\examples\provider-template\README.md` | No hardcoded API keys in docs | error |
| `docs\examples\provider-template\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\README.md` | Generated docs have auto-generated notice | warning |
| `docs\generated\README.md` | No hardcoded API keys in docs | error |
| `docs\generated\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\adr-index.md` | Generated docs have auto-generated notice | warning |
| `docs\generated\adr-index.md` | No hardcoded API keys in docs | error |
| `docs\generated\adr-index.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\configuration-schema.md` | Generated docs have auto-generated notice | warning |
| `docs\generated\configuration-schema.md` | No hardcoded API keys in docs | error |
| `docs\generated\configuration-schema.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\documentation-coverage.md` | No hardcoded API keys in docs | error |
| `docs\generated\documentation-coverage.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\interfaces.md` | No hardcoded API keys in docs | error |
| `docs\generated\interfaces.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\project-context.md` | No hardcoded API keys in docs | error |
| `docs\generated\project-context.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\provider-registry.md` | Generated docs have auto-generated notice | warning |
| `docs\generated\provider-registry.md` | No hardcoded API keys in docs | error |
| `docs\generated\provider-registry.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\repository-structure.md` | Generated docs have auto-generated notice | warning |
| `docs\generated\repository-structure.md` | No hardcoded API keys in docs | error |
| `docs\generated\repository-structure.md` | No hardcoded localhost URLs in docs | info |
| `docs\generated\workflows-overview.md` | Generated docs have auto-generated notice | warning |
| `docs\generated\workflows-overview.md` | No hardcoded API keys in docs | error |
| `docs\generated\workflows-overview.md` | No hardcoded localhost URLs in docs | info |
| `docs\getting-started\README.md` | No hardcoded API keys in docs | error |
| `docs\integrations\README.md` | No hardcoded API keys in docs | error |
| `docs\integrations\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\integrations\fsharp-integration.md` | No hardcoded API keys in docs | error |
| `docs\integrations\fsharp-integration.md` | No hardcoded localhost URLs in docs | info |
| `docs\integrations\language-strategy.md` | No hardcoded API keys in docs | error |
| `docs\integrations\language-strategy.md` | No hardcoded localhost URLs in docs | info |
| `docs\integrations\lean-integration.md` | No hardcoded API keys in docs | error |
| `docs\integrations\lean-integration.md` | No hardcoded localhost URLs in docs | info |
| `docs\operations\README.md` | No hardcoded API keys in docs | error |
| `docs\operations\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\operations\deployment.md` | No hardcoded API keys in docs | error |
| `docs\operations\high-availability.md` | No hardcoded API keys in docs | error |
| `docs\operations\high-availability.md` | No hardcoded localhost URLs in docs | info |
| `docs\operations\msix-packaging.md` | No hardcoded API keys in docs | error |
| `docs\operations\msix-packaging.md` | No hardcoded localhost URLs in docs | info |
| `docs\operations\operator-runbook.md` | No hardcoded API keys in docs | error |
| `docs\operations\performance-tuning.md` | No hardcoded API keys in docs | error |
| `docs\operations\performance-tuning.md` | No hardcoded localhost URLs in docs | info |
| `docs\operations\portable-data-packager.md` | No hardcoded API keys in docs | error |
| `docs\operations\portable-data-packager.md` | No hardcoded localhost URLs in docs | info |
| `docs\operations\service-level-objectives.md` | No hardcoded API keys in docs | error |
| `docs\operations\service-level-objectives.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\assembly-performance-roadmap.md` | No hardcoded API keys in docs | error |
| `docs\plans\assembly-performance-roadmap.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\codebase-audit-cleanup-roadmap.md` | No hardcoded API keys in docs | error |
| `docs\plans\codebase-audit-cleanup-roadmap.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\fund-management-product-vision-and-capability-matrix.md` | No hardcoded API keys in docs | error |
| `docs\plans\fund-management-product-vision-and-capability-matrix.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\governance-fund-ops-blueprint.md` | No hardcoded API keys in docs | error |
| `docs\plans\governance-fund-ops-blueprint.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\l3-inference-implementation-plan.md` | No hardcoded API keys in docs | error |
| `docs\plans\l3-inference-implementation-plan.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\meridian-6-week-roadmap.md` | No hardcoded API keys in docs | error |
| `docs\plans\meridian-6-week-roadmap.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\meridian-database-blueprint.md` | No hardcoded API keys in docs | error |
| `docs\plans\meridian-database-blueprint.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\quant-script-environment-blueprint.md` | No hardcoded API keys in docs | error |
| `docs\plans\quant-script-environment-blueprint.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\readability-refactor-baseline.md` | No hardcoded API keys in docs | error |
| `docs\plans\readability-refactor-baseline.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\readability-refactor-roadmap.md` | No hardcoded API keys in docs | error |
| `docs\plans\readability-refactor-roadmap.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\readability-refactor-technical-design-pack.md` | No hardcoded API keys in docs | error |
| `docs\plans\readability-refactor-technical-design-pack.md` | No hardcoded localhost URLs in docs | info |
| `docs\plans\trading-workstation-migration-blueprint.md` | No hardcoded API keys in docs | error |
| `docs\plans\trading-workstation-migration-blueprint.md` | No hardcoded localhost URLs in docs | info |
| `docs\providers\README.md` | No hardcoded API keys in docs | error |
| `docs\providers\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\providers\alpaca-setup.md` | No hardcoded API keys in docs | error |
| `docs\providers\alpaca-setup.md` | No hardcoded localhost URLs in docs | info |
| `docs\providers\backfill-guide.md` | No hardcoded API keys in docs | error |
| `docs\providers\backfill-guide.md` | No hardcoded localhost URLs in docs | info |
| `docs\providers\data-sources.md` | No hardcoded API keys in docs | error |
| `docs\providers\data-sources.md` | No hardcoded localhost URLs in docs | info |
| `docs\providers\interactive-brokers-free-equity-reference.md` | No hardcoded API keys in docs | error |
| `docs\providers\interactive-brokers-free-equity-reference.md` | No hardcoded localhost URLs in docs | info |
| `docs\providers\interactive-brokers-setup.md` | No hardcoded API keys in docs | error |
| `docs\providers\interactive-brokers-setup.md` | No hardcoded localhost URLs in docs | info |
| `docs\providers\provider-comparison.md` | No hardcoded API keys in docs | error |
| `docs\providers\provider-comparison.md` | No hardcoded localhost URLs in docs | info |
| `docs\providers\stocksharp-connectors.md` | No hardcoded API keys in docs | error |
| `docs\providers\stocksharp-connectors.md` | No hardcoded localhost URLs in docs | info |
| `docs\reference\README.md` | No hardcoded API keys in docs | error |
| `docs\reference\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\reference\api-reference.md` | No hardcoded API keys in docs | error |
| `docs\reference\data-dictionary.md` | No hardcoded API keys in docs | error |
| `docs\reference\data-dictionary.md` | No hardcoded localhost URLs in docs | info |
| `docs\reference\data-uniformity.md` | No hardcoded API keys in docs | error |
| `docs\reference\data-uniformity.md` | No hardcoded localhost URLs in docs | info |
| `docs\reference\design-review-memo.md` | No hardcoded API keys in docs | error |
| `docs\reference\design-review-memo.md` | No hardcoded localhost URLs in docs | info |
| `docs\reference\environment-variables.md` | No hardcoded API keys in docs | error |
| `docs\reference\open-source-references.md` | No hardcoded API keys in docs | error |
| `docs\reference\open-source-references.md` | No hardcoded localhost URLs in docs | info |
| `docs\security\README.md` | No hardcoded API keys in docs | error |
| `docs\security\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\security\known-vulnerabilities.md` | No hardcoded API keys in docs | error |
| `docs\security\known-vulnerabilities.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\CHANGELOG.md` | CHANGELOG exists and has entries | warning |
| `docs\status\CHANGELOG.md` | No hardcoded API keys in docs | error |
| `docs\status\CHANGELOG.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\DOCUMENTATION_TRIAGE_2026_03_21.md` | No hardcoded API keys in docs | error |
| `docs\status\DOCUMENTATION_TRIAGE_2026_03_21.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\EVALUATIONS_AND_AUDITS.md` | No hardcoded API keys in docs | error |
| `docs\status\EVALUATIONS_AND_AUDITS.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\FEATURE_INVENTORY.md` | No hardcoded API keys in docs | error |
| `docs\status\FEATURE_INVENTORY.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\FULL_IMPLEMENTATION_TODO_2026_03_20.md` | No hardcoded API keys in docs | error |
| `docs\status\FULL_IMPLEMENTATION_TODO_2026_03_20.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\IMPROVEMENTS.md` | No hardcoded API keys in docs | error |
| `docs\status\IMPROVEMENTS.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\README.md` | No hardcoded API keys in docs | error |
| `docs\status\README.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\ROADMAP.md` | No hardcoded API keys in docs | error |
| `docs\status\ROADMAP.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\TODO.md` | No hardcoded API keys in docs | error |
| `docs\status\TODO.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\coverage-report.md` | No hardcoded API keys in docs | error |
| `docs\status\coverage-report.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\example-validation.md` | No hardcoded API keys in docs | error |
| `docs\status\example-validation.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\health-dashboard.md` | No hardcoded API keys in docs | error |
| `docs\status\health-dashboard.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\link-repair-report.md` | No hardcoded API keys in docs | error |
| `docs\status\link-repair-report.md` | No hardcoded localhost URLs in docs | info |
| `docs\status\production-status.md` | No hardcoded API keys in docs | error |
| `docs\status\production-status.md` | No hardcoded localhost URLs in docs | info |
