# =============================================================================
# AI Repository Updater & Skills
# =============================================================================

.PHONY: ai-audit ai-audit-code ai-audit-docs ai-audit-tests ai-audit-ai-docs \
        ai-verify ai-report \
        ai-maintenance-light ai-maintenance-full \
        ai-arch-check ai-arch-check-summary ai-arch-check-json \
        ai-docs-freshness ai-docs-drift ai-docs-sync-report \
        ai-docs-archive ai-docs-archive-execute \
        skill-list skill-resources skill-scripts skill-chains skill-resource \
        skill-run skill-chain skill-run-chain skill-validate skill-run-eval \
        skill-benchmark skill-discover

AI_UPDATER := python3 build/scripts/ai-repo-updater.py
AI_ARCH_CHECK := python3 build/scripts/ai-architecture-check.py
AI_DOCS := python3 build/scripts/docs/ai-docs-maintenance.py

SKILLS_CLI := python3 .claude/skills/skills_provider.py
SKILL      ?= meridian-code-review
SCRIPT     ?=
RESOURCE   ?=
CHAIN      ?=
SCRIPTS    ?=
PARAMS     ?=
WORKSPACE  ?=
RUNS       ?= 3

ai-audit: ## Run full AI repository audit (all analysers)
	@echo "$(BLUE)Running full repository audit...$(NC)"
	@$(AI_UPDATER) audit --summary

ai-audit-code: ## Run AI code conventions audit
	@echo "$(BLUE)Auditing code conventions...$(NC)"
	@$(AI_UPDATER) audit-code --summary

ai-audit-docs: ## Run AI documentation quality audit
	@echo "$(BLUE)Auditing documentation quality...$(NC)"
	@$(AI_UPDATER) audit-docs --summary

ai-audit-tests: ## Run AI test coverage gap audit
	@echo "$(BLUE)Auditing test coverage gaps...$(NC)"
	@$(AI_UPDATER) audit-tests --summary

ai-verify: ## Run build + test + lint verification
	@echo "$(BLUE)Running verification (build + test + lint)...$(NC)"
	@$(AI_UPDATER) verify

ai-maintenance-light: ## Run fast maintenance lane and write .ai status artifacts
	@echo "$(BLUE)Running light maintenance lane...$(NC)"
	@bash scripts/ai/maintenance-light.sh

ai-maintenance-full: ## Run full maintenance lane and write .ai status artifacts
	@echo "$(BLUE)Running full maintenance lane...$(NC)"
	@bash scripts/ai/maintenance-full.sh

ai-audit-ai-docs: ## Run AI documentation freshness and drift audit
	@echo "$(BLUE)Auditing AI documentation health...$(NC)"
	@$(AI_UPDATER) audit-ai-docs --summary

ai-report: ## Generate AI improvement report
	@echo "$(BLUE)Generating improvement report...$(NC)"
	@$(AI_UPDATER) report --output docs/generated/improvement-report.md
	@echo "$(GREEN)Report written to docs/generated/improvement-report.md$(NC)"

ai-arch-check: ## Run AI architecture compliance checker (CPM, deps, ADRs, channels, sinks, JSON)
	@echo "$(BLUE)Running architecture compliance checks...$(NC)"
	@$(AI_ARCH_CHECK) --src src/ check
	@echo "$(GREEN)Architecture check complete$(NC)"

ai-arch-check-summary: ## One-line architecture compliance summary (clean / violations)
	@-$(AI_ARCH_CHECK) --src src/ summary

ai-arch-check-json: ## Architecture compliance check with JSON output (for CI / tooling)
	@$(AI_ARCH_CHECK) --src src/ --json check

ai-docs-freshness: ## Check staleness of AI documentation files
	@echo "$(BLUE)Checking AI doc freshness...$(NC)"
	@$(AI_DOCS) freshness --summary

ai-docs-drift: ## Detect AI documentation drift from code reality
	@echo "$(BLUE)Detecting AI doc drift...$(NC)"
	@$(AI_DOCS) drift --summary

ai-docs-sync-report: ## Generate AI docs sync report (markdown)
	@echo "$(BLUE)Generating AI docs sync report...$(NC)"
	@$(AI_DOCS) sync-report --output docs/generated/ai-docs-sync-report.md
	@echo "$(GREEN)Report written to docs/generated/ai-docs-sync-report.md$(NC)"

ai-docs-archive: ## Preview stale docs that could be archived
	@echo "$(BLUE)Scanning for archive candidates...$(NC)"
	@$(AI_DOCS) archive-stale --summary

ai-docs-archive-execute: ## Actually archive stale docs (moves files)
	@echo "$(YELLOW)Archiving stale documents...$(NC)"
	@$(AI_DOCS) archive-stale --execute --summary
	@echo "$(GREEN)Archive complete$(NC)"

# --- Skills Provider CLI ---

skill-list: ## List all registered skills and their descriptions
	@echo "$(BLUE)Registered skills:$(NC)"
	@$(SKILLS_CLI) list

skill-resources: ## List resources for SKILL (default: meridian-code-review)
	@echo "$(BLUE)Resources for '$(SKILL)':$(NC)"
	@$(SKILLS_CLI) list-resources $(SKILL)

skill-scripts: ## List scripts for SKILL (default: meridian-code-review)
	@echo "$(BLUE)Scripts for '$(SKILL)':$(NC)"
	@$(SKILLS_CLI) list-scripts $(SKILL)

skill-chains: ## List predefined chains for SKILL (default: meridian-code-review)
	@echo "$(BLUE)Chains for '$(SKILL)':$(NC)"
	@$(SKILLS_CLI) list-chains $(SKILL)

skill-resource: ## Read a skill resource  (SKILL=… RESOURCE=project-stats)
	@$(SKILLS_CLI) read-resource $(SKILL) $(RESOURCE)

skill-run: ## Run a skill script  (SKILL=… SCRIPT=validate-skill [PARAMS="--param k=v"])
	@$(SKILLS_CLI) run-script $(SKILL) $(SCRIPT) $(PARAMS)

skill-chain: ## Run scripts in sequence  (SKILL=… SCRIPTS="validate-skill run-eval" [PARAMS="…"])
	@$(SKILLS_CLI) chain $(SKILL) $(SCRIPTS) $(PARAMS)

skill-run-chain: ## Run a named chain  (SKILL=… CHAIN=full-check)
	@$(SKILLS_CLI) run-chain $(SKILL) $(CHAIN)

skill-validate: ## Validate the meridian-code-review skill definition
	@echo "$(BLUE)Validating meridian-code-review skill...$(NC)"
	@$(SKILLS_CLI) run-script meridian-code-review validate-skill

skill-run-eval: ## Run the eval suite  (RUNS=3 to set runs_per_query)
	@echo "$(BLUE)Running eval suite (runs_per_query=$(RUNS))...$(NC)"
	@$(SKILLS_CLI) run-script meridian-code-review run-eval --param runs_per_query=$(RUNS)

skill-benchmark: ## Aggregate benchmark results  (WORKSPACE=<dir> required)
	@[ -n "$(WORKSPACE)" ] || { echo "$(YELLOW)ERROR: WORKSPACE is required. Usage: make skill-benchmark WORKSPACE=<dir>$(NC)"; exit 1; }
	@echo "$(BLUE)Aggregating benchmark results from '$(WORKSPACE)'...$(NC)"
	@$(SKILLS_CLI) run-script meridian-code-review aggregate-benchmark \
		--param workspace=$(WORKSPACE)

skill-discover: ## Discover all SKILL.md definitions in the repository
	@echo "$(BLUE)Discovering skills...$(NC)"
	@$(SKILLS_CLI) discover
