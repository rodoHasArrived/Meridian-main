# =============================================================================
# Desktop (WPF) Build Support
# =============================================================================
# Targets for building and testing the Meridian.Wpf desktop application.
# The WPF project requires Windows or EnableWindowsTargeting=true.
# See: src/Meridian.Wpf/README.md and docs/development/wpf-implementation-notes.md

.PHONY: desktop-build desktop-test desktop-test-position-blotter-route desktop-test-operator-inbox-route

desktop-build: ## Build the WPF desktop project (requires Windows or EnableWindowsTargeting)
	@echo "$(BLUE)Building Meridian.Wpf...$(NC)"
	@python3 build/python/cli/buildctl.py build --project src/Meridian.Wpf/Meridian.Wpf.csproj --configuration Release --verbosity quiet --full-wpf-build
	@echo "$(GREEN)Desktop build complete$(NC)"

desktop-test: ## Run WPF desktop tests
	@echo "$(BLUE)Running desktop tests...$(NC)"
	@dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj \
		--logger "console;verbosity=normal" \
		/p:EnableWindowsTargeting=true

desktop-test-position-blotter-route: ## Run the isolated WPF position blotter route validation slice
	@pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-position-blotter-route.ps1

desktop-test-operator-inbox-route: ## Run the isolated WPF operator inbox route validation slice
	@pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-operator-inbox-route.ps1
