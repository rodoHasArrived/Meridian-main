# =============================================================================
# Documentation
# =============================================================================

.PHONY: docs gen-context verify-adrs verify-contracts verify-tooling-metadata \
        gen-structure gen-providers gen-workflows \
        update-claude-md docs-all

docs: gen-context verify-adrs ## Generate all documentation from code
	@echo "$(GREEN)Documentation generated and verified$(NC)"

gen-context: ## Generate project-context.md from code annotations
	@echo "$(BLUE)Generating project context from code...$(NC)"
	@dotnet build $(DOCGEN_PROJECT) -c Release -v q
	@dotnet run --project $(DOCGEN_PROJECT) --no-build -c Release -- context \
		--src src/Meridian \
		--output docs/generated/project-context.md \
		--xml-docs src/Meridian/bin/Release/net9.0/Meridian.xml
	@echo "$(GREEN)Generated docs/generated/project-context.md$(NC)"

verify-adrs: ## Verify ADR implementation links are valid
	@echo "$(BLUE)Verifying ADR implementation links...$(NC)"
	@dotnet build $(DOCGEN_PROJECT) -c Release -v q
	@dotnet run --project $(DOCGEN_PROJECT) --no-build -c Release -- verify-adrs \
		--adr-dir docs/adr \
		--src-dir .
	@echo "$(GREEN)ADR verification complete$(NC)"

verify-contracts: build ## Verify runtime contracts at startup
	@echo "$(BLUE)Verifying contracts...$(NC)"
	dotnet run --project $(PROJECT) --no-build -c Release -- --verify-contracts

verify-tooling-metadata: ## Validate Makefile/package/dependabot path references
	@python3 build/scripts/validate-tooling-metadata.py

gen-structure: ## Generate repository structure documentation
	@echo "$(BLUE)Generating repository structure documentation...$(NC)"
	@mkdir -p docs/generated
	@python3 build/scripts/docs/generate-structure-docs.py \
		--output docs/generated/repository-structure.md
	@echo "$(GREEN)Generated docs/generated/repository-structure.md$(NC)"

gen-providers: ## Generate provider registry documentation
	@echo "$(BLUE)Generating provider registry documentation...$(NC)"
	@mkdir -p docs/generated
	@python3 build/scripts/docs/generate-structure-docs.py \
		--output docs/generated/provider-registry.md \
		--providers-only \
		--extract-attributes
	@echo "$(GREEN)Generated docs/generated/provider-registry.md$(NC)"

gen-workflows: ## Generate workflows overview documentation
	@echo "$(BLUE)Generating workflows overview documentation...$(NC)"
	@mkdir -p docs/generated
	@python3 build/scripts/docs/generate-structure-docs.py \
		--output docs/generated/workflows-overview.md \
		--workflows-only
	@echo "$(GREEN)Generated docs/generated/workflows-overview.md$(NC)"

update-claude-md: gen-structure ## Update CLAUDE.md repository structure
	@echo "$(BLUE)Updating CLAUDE.md repository structure...$(NC)"
	@python3 build/scripts/docs/update-claude-md.py \
		--claude-md CLAUDE.md \
		--structure-source docs/generated/repository-structure.md
	@echo "$(GREEN)Updated CLAUDE.md$(NC)"

docs-all: gen-context gen-structure gen-providers gen-workflows verify-adrs ## Generate all documentation
	@echo "$(GREEN)All documentation generated$(NC)"
