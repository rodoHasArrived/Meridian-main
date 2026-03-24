# =============================================================================
# Meridian - Makefile
# =============================================================================
#
# Common development and deployment tasks
#
# Usage:
#   make help           Show available commands
#   make install        Interactive installation
#   make docker         Build and start Docker container
#   make run            Run the application locally
#   make test           Run tests
#
# =============================================================================

.PHONY: help quickstart install docker docker-build docker-up docker-down docker-logs \
        run run-ui run-backfill test test-unit test-integration test-fsharp test-all build build-quick \
        publish clean check-deps watch watch-build \
        setup-config setup-dev lint format-check benchmark bench-quick bench-filter \
        pre-pr pre-pr-full \
        docs verify-adrs verify-contracts verify-tooling-metadata gen-context \
        gen-interfaces gen-structure gen-providers gen-workflows update-claude-md docs-all \
        doctor doctor-ci doctor-quick doctor-fix diagnose diagnose-build \
        verify-setup \
        collect-debug collect-debug-minimal build-profile build-binlog validate-data analyze-errors \
        build-graph fingerprint env-capture env-diff impact bisect metrics history app-metrics \
        icons desktop desktop-publish install-hooks \
        build-wpf test-desktop-services desktop-dev-bootstrap \
        ai-audit ai-audit-code ai-audit-docs ai-audit-tests ai-audit-ai-docs ai-verify ai-report \
        ai-maintenance-light ai-maintenance-full \
        ai-arch-check ai-arch-check-summary ai-arch-check-json \
        ai-docs-freshness ai-docs-drift ai-docs-sync-report ai-docs-archive ai-docs-archive-execute \
        skill-list skill-resources skill-scripts skill-chains skill-resource \
        skill-run skill-chain skill-run-chain skill-validate skill-run-eval \
        skill-benchmark skill-discover

# Default target
.DEFAULT_GOAL := help

# Project settings
PROJECT := src/Meridian/Meridian.csproj
UI_PROJECT := src/Meridian.Ui/Meridian.Ui.csproj
WPF_PROJECT := src/Meridian.Wpf/Meridian.Wpf.csproj
DESKTOP_PROJECT := $(WPF_PROJECT)
TEST_PROJECT := tests/Meridian.Tests/Meridian.Tests.csproj
BENCHMARK_PROJECT := benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj
DOCGEN_PROJECT := build/dotnet/DocGenerator/DocGenerator.csproj
DOCKER_IMAGE := meridian:latest
HTTP_PORT ?= 8080
BUILDCTL := python3 build/python/cli/buildctl.py
BUILD_VERBOSITY ?= normal
APPINSTALLER_URI ?=
SIGNING_CERT_PFX ?=
SIGNING_CERT_PASSWORD ?=
DESKTOP_PUBLISH_READYTORUN ?= false

ifeq ($(V),0)
	BUILD_VERBOSITY := quiet
endif
ifeq ($(V),2)
	BUILD_VERBOSITY := verbose
endif
ifeq ($(V),3)
	BUILD_VERBOSITY := debug
endif

MSIX_APPINSTALLER_FLAGS :=
MSIX_SIGNING_FLAGS :=
DESKTOP_READYTORUN_FLAGS := -p:PublishReadyToRun=$(DESKTOP_PUBLISH_READYTORUN)
ifneq ($(strip $(APPINSTALLER_URI)),)
	MSIX_APPINSTALLER_FLAGS := -p:GenerateAppInstallerFile=true -p:AppInstallerUri=$(APPINSTALLER_URI) -p:AppInstallerCheckForUpdateFrequency=OnApplicationRun -p:AppInstallerUpdateFrequency=1
endif
ifneq ($(strip $(SIGNING_CERT_PFX)),)
	MSIX_SIGNING_FLAGS := -p:PackageCertificateKeyFile=$(SIGNING_CERT_PFX) -p:PackageCertificatePassword=$(SIGNING_CERT_PASSWORD)
else
	MSIX_SIGNING_FLAGS := -p:GenerateTemporaryStoreCertificate=true
endif

# Colors
GREEN := \033[0;32m
YELLOW := \033[1;33m
BLUE := \033[0;34m
NC := \033[0m # No Color

# =============================================================================
# Help
# =============================================================================

help: ## Show this help message
	@echo ""
	@echo "╔══════════════════════════════════════════════════════════════════════╗"
	@echo "║              Meridian - Make Commands                   ║"
	@echo "╚══════════════════════════════════════════════════════════════════════╝"
	@echo ""
	@echo "$(BLUE)Installation:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'install|setup' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Docker:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'docker' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Development:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'run|build|test|clean|bench|lint|watch|setup-dev|format' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Documentation:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'docs|verify-adr|verify-contract|verify-tooling-metadata|gen-context|gen-interface|gen-structure|gen-provider|gen-workflow|update-claude' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Publishing:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'publish' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Desktop App:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'icons|desktop' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Pre-PR & Quality:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'pre-pr|ai-audit|ai-verify|ai-docs|ai-report|ai-arch|ai-maintenance' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-28s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Skills:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'skill-' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-24s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Diagnostics:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'doctor|diagnose|verify-setup|collect-debug|build-profile|build-binlog|build-graph|fingerprint|env-|impact|bisect|metrics|history|validate-data|analyze-errors' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""

# =============================================================================
# Quick Start
# =============================================================================

quickstart: ## Zero-to-running setup for new contributors
	@echo ""
	@echo "$(BLUE)Meridian - Quick Start$(NC)"
	@echo "======================================"
	@echo ""
	@echo "$(BLUE)[1/5] Checking .NET 9 SDK...$(NC)"
	@dotnet --version > /dev/null 2>&1 || { echo "$(YELLOW)ERROR: .NET SDK not found. Install from https://dot.net/download$(NC)"; exit 1; }
	@echo "  .NET SDK $$(dotnet --version) found"
	@echo ""
	@echo "$(BLUE)[2/5] Setting up configuration...$(NC)"
	@if [ ! -f config/appsettings.json ]; then \
		cp config/appsettings.sample.json config/appsettings.json; \
		echo "  $(GREEN)Created config/appsettings.json from template$(NC)"; \
	else \
		echo "  config/appsettings.json already exists"; \
	fi
	@mkdir -p data logs
	@echo ""
	@echo "$(BLUE)[3/5] Restoring packages...$(NC)"
	@dotnet restore --verbosity quiet
	@echo "  $(GREEN)Packages restored$(NC)"
	@echo ""
	@echo "$(BLUE)[4/5] Building...$(NC)"
	@dotnet build -c Release --verbosity quiet --nologo
	@echo "  $(GREEN)Build succeeded$(NC)"
	@echo ""
	@echo "$(BLUE)[5/5] Running quick tests...$(NC)"
	@dotnet test $(TEST_PROJECT) --verbosity quiet --nologo --no-build -c Release 2>&1 | tail -3
	@echo ""
	@echo "$(GREEN)Setup complete!$(NC)"
	@echo ""
	@echo "Next steps:"
	@echo "  1. Set API credentials as environment variables:"
	@echo "     export ALPACA__KEYID=your-key-id"
	@echo "     export ALPACA__SECRETKEY=your-secret-key"
	@echo "  2. Run the interactive setup wizard:"
	@echo "     dotnet run --project $(PROJECT) -- --wizard"
	@echo "  3. Or start collecting immediately:"
	@echo "     make run-ui"
	@echo ""

# =============================================================================
# Installation
# =============================================================================

install: ## Interactive installation (Docker or Native)
	@./build/scripts/install/install.sh

install-docker: ## Docker-based installation
	@./build/scripts/install/install.sh --docker

install-native: ## Native .NET installation
	@./build/scripts/install/install.sh --native

setup-config: ## Create appsettings.json from template
	@if [ ! -f config/appsettings.json ]; then \
		cp config/appsettings.sample.json config/appsettings.json; \
		echo "$(GREEN)Created config/appsettings.json$(NC)"; \
		echo "$(YELLOW)Remember to edit with your API credentials$(NC)"; \
	else \
		echo "$(YELLOW)config/appsettings.json already exists$(NC)"; \
	fi
	@mkdir -p data logs

check-deps: ## Check prerequisites
	@./build/scripts/install/install.sh --check

# =============================================================================
# Docker
# =============================================================================

docker: ## Build and start Docker container
	@./build/scripts/install/install.sh --docker

docker-build: ## Build Docker image
	@echo "$(BLUE)Building Docker image...$(NC)"
	docker build -f deploy/docker/Dockerfile -t $(DOCKER_IMAGE) .

docker-up: setup-config ## Start Docker container
	@echo "$(BLUE)Starting Docker container...$(NC)"
	docker compose -f deploy/docker/docker-compose.yml up -d
	@echo "$(GREEN)Container started!$(NC)"
	@echo "  Dashboard: http://localhost:$(HTTP_PORT)"
	@echo "  Health:    http://localhost:$(HTTP_PORT)/health"
	@echo "  Metrics:   http://localhost:$(HTTP_PORT)/metrics"

docker-down: ## Stop Docker container
	docker compose -f deploy/docker/docker-compose.yml down

docker-logs: ## View Docker logs
	docker compose -f deploy/docker/docker-compose.yml logs -f

docker-restart: ## Restart Docker container
	docker compose -f deploy/docker/docker-compose.yml restart

docker-clean: ## Remove Docker containers and images
	docker compose -f deploy/docker/docker-compose.yml down -v
	docker rmi $(DOCKER_IMAGE) 2>/dev/null || true

docker-monitoring: ## Start with Prometheus and Grafana
	docker compose -f deploy/docker/docker-compose.yml --profile monitoring up -d
	@echo "$(GREEN)Monitoring stack started!$(NC)"
	@echo "  Prometheus: http://localhost:9090"
	@echo "  Grafana:    http://localhost:3000 (admin/admin)"

# =============================================================================
# Development
# =============================================================================

build: ## Build the project (Release)
	@echo "$(BLUE)Building with observability...$(NC)"
	@BUILD_VERBOSITY=$(BUILD_VERBOSITY) $(BUILDCTL) build --project $(PROJECT) --configuration Release

build-quick: ## Fast incremental build (Debug, no analyzers)
	@dotnet build Meridian.sln -c Debug --verbosity quiet --nologo /p:EnableWindowsTargeting=true

run: setup-config ## Run the collector
	@echo "$(BLUE)Running collector...$(NC)"
	dotnet run --project $(PROJECT) -- --http-port $(HTTP_PORT) --watch-config

run-ui: setup-config ## Run with web dashboard
	@echo "$(BLUE)Starting web dashboard on port $(HTTP_PORT)...$(NC)"
	dotnet run --project $(PROJECT) -- --ui --http-port $(HTTP_PORT)

run-backfill: setup-config ## Run historical backfill
	@echo "$(BLUE)Running backfill...$(NC)"
	@if [ -z "$(SYMBOLS)" ]; then \
		dotnet run --project $(PROJECT) -- --backfill; \
	else \
		dotnet run --project $(PROJECT) -- --backfill --backfill-symbols $(SYMBOLS); \
	fi

run-selftest: ## Run self-tests
	dotnet run --project $(PROJECT) -- --selftest

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
	@echo "$(BLUE)[2/4] Restoring packages...$(NC)"
	@dotnet restore Meridian.sln /p:EnableWindowsTargeting=true --verbosity quiet
	@echo "  $(GREEN)Packages restored$(NC)"
	@echo ""
	@echo "$(BLUE)[3/4] Building (Debug)...$(NC)"
	@dotnet build Meridian.sln -c Debug --verbosity quiet --nologo /p:EnableWindowsTargeting=true
	@echo "  $(GREEN)Build succeeded$(NC)"
	@echo ""
	@echo "$(BLUE)[4/4] Running quick test...$(NC)"
	@dotnet test $(TEST_PROJECT) --verbosity quiet --nologo --no-build -c Debug --filter "Category!=Integration" 2>&1 | tail -3
	@echo ""
	@echo "$(GREEN)Development environment ready!$(NC)"
	@echo "  Run 'make watch' to start test-on-save mode"
	@echo "  Run 'make run-ui' to start the web dashboard"

clean: ## Clean build artifacts
	@echo "$(BLUE)Cleaning...$(NC)"
	dotnet clean --verbosity quiet
	rm -rf bin/ obj/ publish/ TestResults/
	@echo "$(GREEN)Clean complete$(NC)"

# =============================================================================
# Publishing
# =============================================================================

publish: ## Publish for all platforms
	@echo "$(BLUE)Publishing for all platforms...$(NC)"
	./build/scripts/publish/publish.sh

publish-linux: ## Publish for Linux x64
	./build/scripts/publish/publish.sh linux-x64

publish-windows: ## Publish for Windows x64
	./build/scripts/publish/publish.sh win-x64

publish-macos: ## Publish for macOS x64
	./build/scripts/publish/publish.sh osx-x64

# =============================================================================
# Utilities
# =============================================================================

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

# =============================================================================
# Documentation
# =============================================================================

docs: gen-context verify-adrs ## Generate all documentation from code
	@echo "$(GREEN)Documentation generated and verified$(NC)"

gen-context: ## Generate project-context.md from code annotations
	@echo "$(BLUE)Generating project context from code...$(NC)"
	@dotnet build $(DOCGEN_PROJECT) -c Release -v q
	@dotnet run --project $(DOCGEN_PROJECT) --no-build -c Release -- context \
		--src src/Meridian \
		--output docs/generated/project-context.md \
		--xml-docs src/Meridian/bin/Release/net9.0/Meridian.xml
	@echo "$(GREEN)Generated docs/generated/project-context.md$(NC)"

verify-adrs: ## Verify ADR implementation links are valid
	@echo "$(BLUE)Verifying ADR implementation links...$(NC)"
	@dotnet build $(DOCGEN_PROJECT) -c Release -v q
	@dotnet run --project $(DOCGEN_PROJECT) --no-build -c Release -- verify-adrs \
		--adr-dir docs/adr \
		--src-dir .
	@echo "$(GREEN)ADR verification complete$(NC)"

verify-contracts: build ## Verify runtime contracts at startup
	@echo "$(BLUE)Verifying contracts...$(NC)"
	dotnet run --project $(PROJECT) --no-build -c Release -- --verify-contracts

gen-interfaces: ## Extract interface documentation from code
	@echo "$(BLUE)Extracting interface documentation...$(NC)"
	@dotnet build $(DOCGEN_PROJECT) -c Release -v q
	@dotnet run --project $(DOCGEN_PROJECT) --no-build -c Release -- interfaces \
		--src src/Meridian \
		--output docs/generated/interfaces.md
	@echo "$(GREEN)Generated docs/generated/interfaces.md$(NC)"

gen-structure: ## Generate repository structure documentation
	@echo "$(BLUE)Generating repository structure documentation...$(NC)"
	@mkdir -p docs/generated
	@python3 build/scripts/docs/generate-structure-docs.py \
		--output docs/generated/repository-structure.md
	@echo "$(GREEN)Generated docs/generated/repository-structure.md$(NC)"

gen-providers: ## Generate provider registry documentation
	@echo "$(BLUE)Generating provider registry documentation...$(NC)"
	@mkdir -p docs/generated
	@python3 build/scripts/docs/generate-structure-docs.py \
		--output docs/generated/provider-registry.md \
		--providers-only \
		--extract-attributes
	@echo "$(GREEN)Generated docs/generated/provider-registry.md$(NC)"

gen-workflows: ## Generate workflows overview documentation
	@echo "$(BLUE)Generating workflows overview documentation...$(NC)"
	@mkdir -p docs/generated
	@python3 build/scripts/docs/generate-structure-docs.py \
		--output docs/generated/workflows-overview.md \
		--workflows-only
	@echo "$(GREEN)Generated docs/generated/workflows-overview.md$(NC)"

update-claude-md: gen-structure ## Update CLAUDE.md repository structure
	@echo "$(BLUE)Updating CLAUDE.md repository structure...$(NC)"
	@python3 build/scripts/docs/update-claude-md.py \
		--claude-md CLAUDE.md \
		--structure-source docs/generated/repository-structure.md
	@echo "$(GREEN)Updated CLAUDE.md$(NC)"

docs-all: gen-context gen-interfaces gen-structure gen-providers gen-workflows verify-adrs ## Generate all documentation
	@echo "$(GREEN)All documentation generated$(NC)"

# =============================================================================
# Diagnostics
# =============================================================================

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

# =============================================================================
# Desktop App
# =============================================================================

verify-tooling-metadata: ## Validate Makefile/package/dependabot path references
	@python3 build/scripts/validate-tooling-metadata.py

icons: ## Generate desktop app icons from SVG
	@echo "$(BLUE)Generating desktop app icons...$(NC)"
	@npm ci --silent
	@npm run generate-icons
	@echo "$(GREEN)Icons generated$(NC)"

desktop: icons ## Build WPF desktop app (Windows only)
	@echo "$(BLUE)Building WPF desktop app...$(NC)"
ifeq ($(OS),Windows_NT)
	dotnet build $(WPF_PROJECT) -c Release -r win-x64 -p:EnableFullWpfBuild=true
else
	@echo "$(YELLOW)Desktop app build requires Windows. Use GitHub Actions for CI builds.$(NC)"
	@echo "Run on Windows: dotnet build $(WPF_PROJECT) -c Release -r win-x64 -p:EnableFullWpfBuild=true"
endif

desktop-publish: icons ## Publish WPF desktop app as MSIX (Windows only)
	@echo "$(BLUE)Publishing WPF desktop app...$(NC)"
ifeq ($(OS),Windows_NT)
	dotnet publish $(WPF_PROJECT) -c Release -r win-x64 --self-contained true \
		-p:EnableFullWpfBuild=true \
		$(DESKTOP_READYTORUN_FLAGS) \
		-p:WindowsPackageType=MSIX \
		-p:AppxPackageDir=publish/desktop/ \
		$(MSIX_APPINSTALLER_FLAGS) \
		$(MSIX_SIGNING_FLAGS)
	@echo "$(GREEN)Published MSIX to publish/desktop/$(NC)"
else
	@echo "$(YELLOW)Desktop app publish requires Windows.$(NC)"
	@echo "Use GitHub Actions workflow 'Desktop App Build' for CI builds."
endif

build-wpf: ## Build WPF desktop app (alias for desktop)
	@$(MAKE) desktop

test-desktop-services: ## Run desktop-focused regression tests
	@echo "$(BLUE)Running desktop-focused tests...$(NC)"
ifeq ($(OS),Windows_NT)
	@echo "Running WPF service tests..."
	dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release -p:EnableFullWpfBuild=true
	@echo "Running UI service tests..."
	dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj -c Release
	@echo "Running integration tests..."
	dotnet test $(TEST_PROJECT) -c Release --filter "FullyQualifiedName~ConfigurationUnificationTests|FullyQualifiedName~CliModeResolverTests"
else
	@echo "$(YELLOW)Desktop service tests require Windows. Skipping WPF and UI tests.$(NC)"
	@echo "Running available integration tests..."
	dotnet test $(TEST_PROJECT) -c Release --filter "FullyQualifiedName~ConfigurationUnificationTests|FullyQualifiedName~CliModeResolverTests"
endif

desktop-dev-bootstrap: ## Run desktop development bootstrap checks (PowerShell)
	@echo "$(BLUE)Running desktop development bootstrap checks...$(NC)"
	pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/desktop-dev.ps1

# =============================================================================
# Pre-PR Validation
# =============================================================================

pre-pr: lint test ## Run pre-PR checks (format + tests) — run before pushing
	@echo ""
	@echo "$(GREEN)All pre-PR checks passed!$(NC)"
	@echo "Ready to push and open a pull request."

pre-pr-full: lint test-all ## Full pre-PR validation (format + all tests with coverage)
	@echo ""
	@echo "$(GREEN)Full pre-PR validation passed!$(NC)"

# =============================================================================
# AI Repository Updater
# =============================================================================

AI_UPDATER := python3 build/scripts/ai-repo-updater.py

ai-audit: ## Run full AI repository audit (all analysers)
	@echo "$(BLUE)Running full repository audit...$(NC)"
	@$(AI_UPDATER) audit --summary

ai-audit-code: ## Run AI code conventions audit
	@echo "$(BLUE)Auditing code conventions...$(NC)"
	@$(AI_UPDATER) audit-code --summary

ai-audit-docs: ## Run AI documentation quality audit
	@echo "$(BLUE)Auditing documentation quality...$(NC)"
	@$(AI_UPDATER) audit-docs --summary

ai-audit-tests: ## Run AI test coverage gap audit
	@echo "$(BLUE)Auditing test coverage gaps...$(NC)"
	@$(AI_UPDATER) audit-tests --summary

ai-verify: ## Run build + test + lint verification
	@echo "$(BLUE)Running verification (build + test + lint)...$(NC)"
	@$(AI_UPDATER) verify

ai-maintenance-light: ## Run fast maintenance lane and write .ai status artifacts
	@echo "$(BLUE)Running light maintenance lane...$(NC)"
	@bash scripts/ai/maintenance-light.sh

ai-maintenance-full: ## Run full maintenance lane and write .ai status artifacts
	@echo "$(BLUE)Running full maintenance lane...$(NC)"
	@bash scripts/ai/maintenance-full.sh

ai-audit-ai-docs: ## Run AI documentation freshness and drift audit
	@echo "$(BLUE)Auditing AI documentation health...$(NC)"
	@$(AI_UPDATER) audit-ai-docs --summary

ai-report: ## Generate AI improvement report
	@echo "$(BLUE)Generating improvement report...$(NC)"
	@$(AI_UPDATER) report --output docs/generated/improvement-report.md
	@echo "$(GREEN)Report written to docs/generated/improvement-report.md$(NC)"

AI_ARCH_CHECK := python3 build/scripts/ai-architecture-check.py

ai-arch-check: ## Run AI architecture compliance checker (CPM, deps, ADRs, channels, sinks, JSON)
	@echo "$(BLUE)Running architecture compliance checks...$(NC)"
	@$(AI_ARCH_CHECK) --src src/ check
	@echo "$(GREEN)Architecture check complete$(NC)"

ai-arch-check-summary: ## One-line architecture compliance summary (clean / violations)
	@-$(AI_ARCH_CHECK) --src src/ summary

ai-arch-check-json: ## Architecture compliance check with JSON output (for CI / tooling)
	@$(AI_ARCH_CHECK) --src src/ --json check

# =============================================================================
# AI Documentation Maintenance
# =============================================================================

AI_DOCS := python3 build/scripts/docs/ai-docs-maintenance.py

ai-docs-freshness: ## Check staleness of AI documentation files
	@echo "$(BLUE)Checking AI doc freshness...$(NC)"
	@$(AI_DOCS) freshness --summary

ai-docs-drift: ## Detect AI documentation drift from code reality
	@echo "$(BLUE)Detecting AI doc drift...$(NC)"
	@$(AI_DOCS) drift --summary

ai-docs-sync-report: ## Generate AI docs sync report (markdown)
	@echo "$(BLUE)Generating AI docs sync report...$(NC)"
	@$(AI_DOCS) sync-report --output docs/generated/ai-docs-sync-report.md
	@echo "$(GREEN)Report written to docs/generated/ai-docs-sync-report.md$(NC)"

ai-docs-archive: ## Preview stale docs that could be archived
	@echo "$(BLUE)Scanning for archive candidates...$(NC)"
	@$(AI_DOCS) archive-stale --summary

ai-docs-archive-execute: ## Actually archive stale docs (moves files)
	@echo "$(YELLOW)Archiving stale documents...$(NC)"
	@$(AI_DOCS) archive-stale --execute --summary
	@echo "$(GREEN)Archive complete$(NC)"

# =============================================================================
# Skills Provider CLI
# =============================================================================

SKILLS_CLI := python3 .claude/skills/skills_provider.py
SKILL      ?= meridian-code-review
SCRIPT     ?=
RESOURCE   ?=
CHAIN      ?=
SCRIPTS    ?=
PARAMS     ?=
WORKSPACE  ?=
RUNS       ?= 3

skill-list: ## List all registered skills and their descriptions
	@echo "$(BLUE)Registered skills:$(NC)"
	@$(SKILLS_CLI) list

skill-resources: ## List resources for SKILL (default: meridian-code-review)
	@echo "$(BLUE)Resources for '$(SKILL)':$(NC)"
	@$(SKILLS_CLI) list-resources $(SKILL)

skill-scripts: ## List scripts for SKILL (default: meridian-code-review)
	@echo "$(BLUE)Scripts for '$(SKILL)':$(NC)"
	@$(SKILLS_CLI) list-scripts $(SKILL)

skill-chains: ## List predefined chains for SKILL (default: meridian-code-review)
	@echo "$(BLUE)Chains for '$(SKILL)':$(NC)"
	@$(SKILLS_CLI) list-chains $(SKILL)

skill-resource: ## Read a skill resource  (SKILL=… RESOURCE=project-stats)
	@$(SKILLS_CLI) read-resource $(SKILL) $(RESOURCE)

skill-run: ## Run a skill script  (SKILL=… SCRIPT=validate-skill [PARAMS="--param k=v"])
	@$(SKILLS_CLI) run-script $(SKILL) $(SCRIPT) $(PARAMS)

skill-chain: ## Run scripts in sequence  (SKILL=… SCRIPTS="validate-skill run-eval" [PARAMS="…"])
	@$(SKILLS_CLI) chain $(SKILL) $(SCRIPTS) $(PARAMS)

skill-run-chain: ## Run a named chain  (SKILL=… CHAIN=full-check)
	@$(SKILLS_CLI) run-chain $(SKILL) $(CHAIN)

skill-validate: ## Validate the meridian-code-review skill definition
	@echo "$(BLUE)Validating meridian-code-review skill...$(NC)"
	@$(SKILLS_CLI) run-script meridian-code-review validate-skill

skill-run-eval: ## Run the eval suite  (RUNS=3 to set runs_per_query)
	@echo "$(BLUE)Running eval suite (runs_per_query=$(RUNS))...$(NC)"
	@$(SKILLS_CLI) run-script meridian-code-review run-eval --param runs_per_query=$(RUNS)

skill-benchmark: ## Aggregate benchmark results  (WORKSPACE=<dir> required)
	@[ -n "$(WORKSPACE)" ] || { echo "$(YELLOW)ERROR: WORKSPACE is required. Usage: make skill-benchmark WORKSPACE=<dir>$(NC)"; exit 1; }
	@echo "$(BLUE)Aggregating benchmark results from '$(WORKSPACE)'...$(NC)"
	@$(SKILLS_CLI) run-script meridian-code-review aggregate-benchmark \
		--param workspace=$(WORKSPACE)

skill-discover: ## Discover all SKILL.md definitions in the repository
	@echo "$(BLUE)Discovering skills...$(NC)"
	@$(SKILLS_CLI) discover
