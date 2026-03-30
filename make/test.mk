# =============================================================================
# Tests
# =============================================================================

.PHONY: test test-unit test-fsharp test-integration test-all test-coverage

test: ## Run unit tests (C# + F#)
	@echo "$(BLUE)Running tests...$(NC)"
	dotnet test $(TEST_PROJECT) --logger "console;verbosity=normal" --filter "Category!=Integration"
	dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"

test-unit: ## Run C# unit tests only (fastest)
	@echo "$(BLUE)Running C# unit tests...$(NC)"
	dotnet test $(TEST_PROJECT) --filter "Category!=Integration" --logger "console;verbosity=normal"

test-fsharp: ## Run F# tests only
	@echo "$(BLUE)Running F# tests...$(NC)"
	dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"

test-integration: ## Run integration tests
	@echo "$(BLUE)Running integration tests...$(NC)"
	dotnet test $(TEST_PROJECT) --filter "Category=Integration" --logger "console;verbosity=normal"

test-all: ## Run all tests with coverage report
	@echo "$(BLUE)Running all tests with coverage...$(NC)"
	dotnet test Meridian.sln \
		--collect:"XPlat Code Coverage" \
		--results-directory ./TestResults \
		--settings tests/coverlet.runsettings \
		--logger "console;verbosity=normal" \
		--filter "Category!=Integration" \
		/p:EnableWindowsTargeting=true
	@echo "$(GREEN)Coverage reports at ./TestResults/$(NC)"

test-coverage: ## Run tests with coverage (alias for test-all)
	@$(MAKE) test-all
