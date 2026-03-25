# =============================================================================
# Desktop (WPF) Build Support
# =============================================================================
# Targets for building and testing the Meridian.Wpf desktop application.
# The WPF project requires Windows or EnableWindowsTargeting=true.
# See: src/Meridian.Wpf/README.md and docs/development/wpf-implementation-notes.md

.PHONY: desktop-build desktop-test

desktop-build: ## Build the WPF desktop project (requires Windows or EnableWindowsTargeting)
	@echo "$(BLUE)Building Meridian.Wpf...$(NC)"
	@dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --verbosity quiet --nologo \
		/p:EnableWindowsTargeting=true
	@echo "$(GREEN)Desktop build complete$(NC)"

desktop-test: ## Run WPF desktop tests
	@echo "$(BLUE)Running desktop tests...$(NC)"
	@dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj \
		--logger "console;verbosity=normal" \
		/p:EnableWindowsTargeting=true
