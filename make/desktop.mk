# =============================================================================
# Desktop (WPF) Build Support
# =============================================================================
# Targets for building and testing the Meridian.Wpf desktop application.
# The WPF project requires Windows or EnableWindowsTargeting=true.
# See: src/Meridian.Wpf/README.md and docs/development/wpf-implementation-notes.md

.PHONY: desktop-build desktop-test desktop-run desktop-workflow desktop-manual desktop-screenshots

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

desktop-run: ## Launch the WPF desktop app and start the local host automatically (Windows)
	@echo "$(BLUE)Launching Meridian desktop with local host...$(NC)"
	@pwsh ./scripts/dev/run-desktop.ps1

desktop-workflow: ## Launch the WPF desktop app and walk the default debug workflow (Windows)
	@echo "$(BLUE)Running the default desktop debug workflow...$(NC)"
	@pwsh ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup

desktop-manual: ## Capture workflow screenshots and generate a desktop user manual (Windows)
	@echo "$(BLUE)Generating the desktop user manual...$(NC)"
	@pwsh ./scripts/dev/generate-desktop-user-manual.ps1

desktop-screenshots: ## Refresh the WPF screenshot catalog used by docs (Windows)
	@echo "$(BLUE)Refreshing desktop screenshots...$(NC)"
	@pwsh ./scripts/dev/capture-desktop-screenshots.ps1
