# =============================================================================
# Diagnostics & Observability
# =============================================================================

.PHONY: doctor doctor-ci doctor-quick doctor-fix verify-setup \
        diagnose diagnose-build \
        collect-debug collect-debug-minimal \
        build-profile build-binlog \
        validate-data analyze-errors \
        build-graph fingerprint \
        env-capture env-diff impact bisect \
        metrics history \
        health status app-metrics version

doctor: ## Run environment health check
	@$(BUILDCTL) doctor

doctor-ci: ## Run environment health check for CI (warnings don't fail)
	@$(BUILDCTL) doctor --no-fail-on-warn

doctor-quick: ## Run quick environment check
	@$(BUILDCTL) doctor --quick

doctor-fix: ## Run environment check and auto-fix issues
	@echo "$(YELLOW)Auto-fix not yet implemented in buildctl doctor$(NC)"
	@$(BUILDCTL) doctor

verify-setup: ## Verify the development environment is correctly set up
	@echo ""
	@echo "$(BLUE)Verifying development setup...$(NC)"
	@echo ""
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
	if dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-build --verbosity quiet --filter "Category!=Integration" 2>&1; then \
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
