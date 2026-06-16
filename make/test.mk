# =============================================================================
# Tests
# =============================================================================

.PHONY: test test-unit test-fsharp test-integration test-scenario test-all test-coverage

test: ## Run unit tests (C# + F#)
	@echo "$(BLUE)Running tests...$(NC)"
	$(BUILDCTL) test --project $(TEST_PROJECT) --filter "Category!=Integration" --queue
	$(BUILDCTL) test --project tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --queue

test-unit: ## Run C# unit tests only (fastest)
	@echo "$(BLUE)Running C# unit tests...$(NC)"
	$(BUILDCTL) test --project $(TEST_PROJECT) --filter "Category!=Integration" --queue

test-fsharp: ## Run F# tests only
	@echo "$(BLUE)Running F# tests...$(NC)"
	$(BUILDCTL) test --project tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --queue

test-integration: ## Run integration tests
	@echo "$(BLUE)Running integration tests...$(NC)"
	$(BUILDCTL) test --project $(TEST_PROJECT) --filter "Category=Integration" --queue

test-scenario: ## Run operator-style scenario tests only (multi-step workflow exercises)
	@echo "$(BLUE)Running scenario tests...$(NC)"
	$(BUILDCTL) test --project Meridian.sln --filter "Category=Scenario" --queue

test-all: ## Run all tests with coverage report (including scenario tests)
	@echo "$(BLUE)Running all tests with coverage...$(NC)"
	$(BUILDCTL) test --project Meridian.sln \
		--collect "XPlat Code Coverage" \
		--results-directory ./TestResults \
		--settings tests/coverlet.runsettings \
		--filter "Category!=Integration&Category!=Performance" \
		--queue
	@echo "$(GREEN)Coverage reports at ./TestResults/$(NC)"

test-coverage: ## Run tests with coverage (alias for test-all)
	@$(MAKE) test-all
