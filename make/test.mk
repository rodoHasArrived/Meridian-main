# =============================================================================
# Tests
# =============================================================================

.PHONY: test test-unit test-fsharp test-integration test-scenario test-all test-coverage

# Keep the historical C# aggregate when using the default project. A caller's
# TEST_PROJECT override remains a focused run. Recursive expansion is deliberate:
# the root Makefile sets TEST_PROJECT after loading this module.
CSHARP_UNIT_TEST_PROJECTS = $(TEST_PROJECT) $(if $(filter tests/Meridian.Tests/Meridian.Tests.csproj,$(TEST_PROJECT)),tests/Meridian.Contracts.UnitTests/Meridian.Contracts.UnitTests.csproj tests/Meridian.Core.UnitTests/Meridian.Core.UnitTests.csproj)

test: ## Run unit tests (C# + F#)
	@echo "$(BLUE)Running tests...$(NC)"
	@$(MAKE) --no-print-directory test-unit
	$(BUILDCTL) test --project tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --queue

test-unit: ## Run C# unit tests only (fastest)
	@echo "$(BLUE)Running C# unit tests...$(NC)"
	@set -e; for project in $(CSHARP_UNIT_TEST_PROJECTS); do \
		$(BUILDCTL) test --project "$$project" --filter "Category!=Integration" --queue; \
	done

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
