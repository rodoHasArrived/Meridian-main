# =============================================================================
# Build, Run, Format, Watch & Publish
# =============================================================================

.PHONY: build build-quick run run-backfill run-selftest \
        lint format format-check \
        watch watch-build \
        install-hooks setup-dev \
        clean \
        benchmark bench-quick bench-filter \
        publish publish-linux publish-windows publish-macos \
        pre-pr pre-pr-full

build: ## Build the project (Release)
	@echo "$(BLUE)Building with observability...$(NC)"
	@BUILD_VERBOSITY=$(BUILD_VERBOSITY) $(BUILDCTL) build --project $(PROJECT) --configuration Release

build-quick: ## Fast incremental build (Debug, no analyzers)
	@python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Debug --verbosity quiet

run: setup-config ## Run the collector
	@echo "$(BLUE)Running collector...$(NC)"
	dotnet run --project $(PROJECT) -- --http-port $(HTTP_PORT) --watch-config

run-backfill: setup-config ## Run historical backfill
	@echo "$(BLUE)Running backfill...$(NC)"
	@if [ -z "$(SYMBOLS)" ]; then \
		dotnet run --project $(PROJECT) -- --backfill; \
	else \
		dotnet run --project $(PROJECT) -- --backfill --backfill-symbols $(SYMBOLS); \
	fi

run-selftest: ## Run self-tests
	dotnet run --project $(PROJECT) -- --selftest

lint: ## Check code formatting (solution-wide)
	dotnet format Meridian.sln --verify-no-changes --verbosity normal

format: ## Auto-fix code formatting
	dotnet format Meridian.sln
	@echo "$(GREEN)Formatting applied$(NC)"

format-check: ## Check formatting and show diff of needed changes
	@dotnet format Meridian.sln --verify-no-changes --verbosity diagnostic 2>&1 || \
		{ echo ""; echo "$(YELLOW)Run 'make format' to auto-fix these issues.$(NC)"; exit 1; }

watch: ## Watch for changes and re-run tests (C# unit tests)
	@echo "$(BLUE)Watching for changes... (Ctrl+C to stop)$(NC)"
	dotnet watch test --project $(TEST_PROJECT) -- --filter "Category!=Integration" --verbosity quiet --nologo

watch-build: ## Watch for changes and rebuild
	@echo "$(BLUE)Watching for changes... (Ctrl+C to stop)$(NC)"
	dotnet watch build --project $(PROJECT)

install-hooks: ## Install git pre-commit and commit-msg hooks
	@./build/scripts/hooks/install-hooks.sh

setup-dev: install-hooks setup-config ## Full local dev setup (hooks, config, restore, build)
	@echo "$(BLUE)Setting up development environment...$(NC)"
	@echo ""
	@echo "$(BLUE)[1/4] Checking prerequisites...$(NC)"
	@command -v dotnet >/dev/null 2>&1 || { echo "$(YELLOW)ERROR: .NET SDK not found. Install from https://dot.net/download$(NC)"; exit 1; }
	@echo "  .NET SDK $$(dotnet --version)"
	@command -v git >/dev/null 2>&1 || { echo "$(YELLOW)ERROR: git not found$(NC)"; exit 1; }
	@echo "  git $$(git --version | cut -d' ' -f3)"
	@echo ""
	@echo "$(BLUE)[2/4] Restoring packages and building sequentially...$(NC)"
	@python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Debug --verbosity quiet
	@echo "  $(GREEN)Restore + build succeeded$(NC)"
	@echo ""
	@echo "$(BLUE)[3/4] Running quick test...$(NC)"
	@dotnet test $(TEST_PROJECT) --verbosity quiet --nologo --no-build -c Debug --filter "Category!=Integration" 2>&1 | tail -3
	@echo ""
	@echo "$(BLUE)[4/4] Shared build workflow configured...$(NC)"
	@echo "  $(GREEN)Sequential restore/build is now the default setup path$(NC)"
	@echo ""
	@echo "$(GREEN)Development environment ready!$(NC)"
	@echo "  Run 'make watch' to start test-on-save mode"
	@echo "  Run 'make run' to start the desktop-local backend"

clean: ## Clean build artifacts
	@echo "$(BLUE)Cleaning...$(NC)"
	dotnet clean --verbosity quiet
	rm -rf bin/ obj/ publish/ TestResults/
	@echo "$(GREEN)Clean complete$(NC)"

benchmark: ## Run full benchmarks
	@echo "$(BLUE)Running benchmarks...$(NC)"
	dotnet run --project $(BENCHMARK_PROJECT) -c Release

bench-quick: ## Run quick bottleneck benchmarks (~10 min)
	@echo "$(BLUE)Running quick bottleneck benchmarks...$(NC)"
	@if [ -x benchmarks/run-bottleneck-benchmarks.sh ]; then \
		./benchmarks/run-bottleneck-benchmarks.sh --quick; \
	else \
		dotnet run --project $(BENCHMARK_PROJECT) -c Release -- --filter "*EndToEnd*|*Pipeline*" --job short; \
	fi

bench-filter: ## Run specific benchmarks (FILTER required, e.g. FILTER=*Collector*)
	@echo "$(BLUE)Running benchmarks matching $(FILTER)...$(NC)"
	dotnet run --project $(BENCHMARK_PROJECT) -c Release -- --filter "$(FILTER)" --memory --job short

publish: ## Publish for all platforms
	@echo "$(BLUE)Publishing for all platforms...$(NC)"
	./build/scripts/publish/publish.sh

publish-linux: ## Publish for Linux x64
	./build/scripts/publish/publish.sh linux-x64

publish-windows: ## Publish for Windows x64
	./build/scripts/publish/publish.sh win-x64

publish-macos: ## Publish for macOS x64
	./build/scripts/publish/publish.sh osx-x64

pre-pr: lint test ## Run pre-PR checks (format + tests) — run before pushing
	@echo ""
	@echo "$(GREEN)All pre-PR checks passed!$(NC)"
	@echo "Ready to push and open a pull request."

pre-pr-full: lint test-all ## Full pre-PR validation (format + all tests with coverage)
	@echo ""
	@echo "$(GREEN)Full pre-PR validation passed!$(NC)"
