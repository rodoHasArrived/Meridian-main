# =============================================================================
# Diagnostics & Observability
# =============================================================================

.PHONY: doctor doctor-ci doctor-quick doctor-fix verify-setup \
        doctor-unit-test doctor-browser doctor-desktop doctor-full-quality-gate \
        diagnose diagnose-build \
        collect-debug collect-debug-minimal \
        build-profile build-binlog \
        validate-data analyze-errors \
        build-graph fingerprint \
        env-capture env-diff impact bisect \
        metrics history \
        health status app-metrics version

doctor: ## Run environment health check (PROFILE selects workflow prerequisites)
	@$(BUILDCTL) doctor $(if $(PROFILE),--profile $(PROFILE),)

doctor-ci: ## Run environment health check for CI (warnings don't fail)
	@$(BUILDCTL) doctor --no-fail-on-warn $(if $(PROFILE),--profile $(PROFILE),)

doctor-quick: ## Run quick environment check
	@$(BUILDCTL) doctor --quick $(if $(PROFILE),--profile $(PROFILE),)

doctor-unit-test: ## Check .NET unit-test prerequisites without restoring or building
	@$(BUILDCTL) doctor --profile unit-test

doctor-browser: ## Check browser workstation prerequisites without restoring or building
	@$(BUILDCTL) doctor --profile browser

doctor-desktop: ## Check desktop workstation prerequisites without restoring or building
	@$(BUILDCTL) doctor --profile desktop

doctor-full-quality-gate: ## Check full CI quality-gate prerequisites without restoring or building
	@$(BUILDCTL) doctor --profile full-quality-gate

doctor-fix: ## Run environment check and auto-fix issues
	@echo "$(YELLOW)Auto-fix not yet implemented in buildctl doctor$(NC)"
	@$(BUILDCTL) doctor $(if $(PROFILE),--profile $(PROFILE),)

verify-setup: ## Verify the development environment is correctly set up
	@echo ""
	@echo "$(BLUE)Verifying development setup...$(NC)"
	@echo ""
	@$(BUILDCTL) doctor --profile unit-test
	@PASS=true; \
	printf "  Restoring dependencies... "; \
	if dotnet restore Meridian.sln /p:EnableWindowsTargeting=true --verbosity quiet 2>&1; then \
		echo "$(GREEN)✓ pass$(NC)"; \
	else \
		echo "$(RED)✗ FAIL$(NC)"; PASS=false; \
	fi; \
	printf "  Building solution...      "; \
	if dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true --verbosity quiet 2>&1; then \
		echo "$(GREEN)✓ pass$(NC)"; \
	else \
		echo "$(RED)✗ FAIL$(NC)"; PASS=false; \
	fi; \
	printf "  Running unit tests...     "; \
	if $(BUILDCTL) test --project tests/Meridian.Tests/Meridian.Tests.csproj --configuration Release --verbosity quiet --filter "Category!=Integration" --queue 2>&1; then \
		echo "$(GREEN)✓ pass$(NC)"; \
	else \
		echo "$(RED)✗ FAIL$(NC)"; PASS=false; \
	fi; \
	echo ""; \
	if [ "$$PASS" = "true" ]; then \
		echo "$(GREEN)Setup verified successfully.$(NC)"; \
	else \
		echo "$(RED)Setup verification FAILED. Check output above.$(NC)"; \
		exit 1; \
	fi

diagnose: ## Run build diagnostics (alias)
	@$(BUILDCTL) build --project $(PROJECT) --configuration Release

diagnose-build: ## Run full build diagnostics
	@$(BUILDCTL) build --project $(PROJECT) --configuration Release

collect-debug: ## Collect debug bundle for issue reporting
	@$(BUILDCTL) collect-debug --project $(PROJECT) --configuration Release

collect-debug-minimal: ## Collect minimal debug bundle (no config/logs)
	@$(BUILDCTL) collect-debug --project $(PROJECT) --configuration Release

build-profile: ## Build with timing information
	@$(BUILDCTL) build-profile

build-binlog: ## Build with MSBuild binary log for detailed analysis
	@echo "$(BLUE)Building with binary log...$(NC)"
	@dotnet build $(PROJECT) -c Release /bl:msbuild.binlog
	@echo ""
	@echo "$(GREEN)Binary log created: msbuild.binlog$(NC)"
	@echo "To analyze, install MSBuild Structured Log Viewer:"
	@echo "  dotnet tool install -g MSBuild.StructuredLogger"
	@echo "  structuredlogviewer msbuild.binlog"

validate-data: ## Validate JSONL data integrity
	@$(BUILDCTL) validate-data --directory data/

analyze-errors: ## Analyze build output for known error patterns
	@echo "$(BLUE)Building and analyzing for known errors...$(NC)"
	@dotnet build $(PROJECT) 2>&1 | $(BUILDCTL) analyze-errors

build-graph: ## Generate dependency graph
	@$(BUILDCTL) build-graph --project $(PROJECT)

fingerprint: ## Generate build fingerprint
	@$(BUILDCTL) fingerprint --configuration Release

env-capture: ## Capture environment snapshot (NAME required)
	@$(BUILDCTL) env-capture $(NAME)

env-diff: ## Compare two environment snapshots
	@$(BUILDCTL) env-diff $(ENV1) $(ENV2)

impact: ## Analyze build impact for a file (FILE required)
	@$(BUILDCTL) impact --file $(FILE)

bisect: ## Run build bisect (GOOD and BAD required)
	@$(BUILDCTL) bisect --good $(GOOD) --bad $(BAD)

metrics: ## Show build metrics summary
	@$(BUILDCTL) metrics

history: ## Show build history summary
	@$(BUILDCTL) history

health: ## Check application health
	@curl -s http://localhost:$(HTTP_PORT)/health | jq . 2>/dev/null || echo "Application not running or jq not installed"

status: ## Get application status
	@curl -s http://localhost:$(HTTP_PORT)/status | jq . 2>/dev/null || echo "Application not running or jq not installed"

app-metrics: ## Get Prometheus metrics from running app
	@curl -s http://localhost:$(HTTP_PORT)/metrics

version: ## Show version information
	@echo "Meridian v1.6.2"
	@dotnet --version 2>/dev/null && echo ".NET SDK: $$(dotnet --version)" || echo ".NET SDK: Not installed"
	@docker --version 2>/dev/null || echo "Docker: Not installed"
