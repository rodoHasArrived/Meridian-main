# =============================================================================
# Desktop (WPF) Build Support
# =============================================================================
# Targets for building and testing the Meridian.Wpf desktop application.
# The WPF project requires Windows or EnableWindowsTargeting=true.
# See: src/Meridian.Wpf/README.md and docs/development/wpf-implementation-notes.md

.PHONY: desktop-build desktop-test

desktop-build: ## Build the WPF desktop project (requires Windows or EnableWindowsTargeting)
	@echo "$(BLUE)Building Meridian.Wpf...$(NC)"
	@python3 build/python/cli/buildctl.py build --project src/Meridian.Wpf/Meridian.Wpf.csproj --configuration Release --verbosity quiet --full-wpf-build
	@echo "$(GREEN)Desktop build complete$(NC)"

desktop-test: ## Run WPF desktop tests
	@echo "$(BLUE)Running desktop tests...$(NC)"
	@dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj \
		--logger "console;verbosity=normal" \
		/p:EnableWindowsTargeting=true
