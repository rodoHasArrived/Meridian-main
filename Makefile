# =============================================================================
# Meridian - Makefile (index)
# =============================================================================
#
# This file is the entry point / help layer only.
# All targets are defined in make/*.mk -- edit those files, not this one.
#
# Usage:
#   make help           Show available commands
#   make install        Interactive installation
#   make docker         Build and start Docker container
#   make run            Run the application locally
#   make test           Run tests
#
# =============================================================================

# Load all module files
include make/install.mk
include make/build.mk
include make/test.mk
include make/docs.mk
include make/desktop.mk
include make/ai.mk
include make/diagnostics.mk

# Default target
.DEFAULT_GOAL := help

# Project settings
PROJECT := src/Meridian/Meridian.csproj
UI_PROJECT := src/Meridian.Ui/Meridian.Ui.csproj
TEST_PROJECT := tests/Meridian.Tests/Meridian.Tests.csproj
BENCHMARK_PROJECT := benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj
DOCGEN_PROJECT := build/dotnet/DocGenerator/DocGenerator.csproj
DOCKER_IMAGE := meridian:latest
HTTP_PORT ?= 8080
BUILDCTL := python3 build/python/cli/buildctl.py
BUILD_VERBOSITY ?= normal
ifeq ($(V),0)
BUILD_VERBOSITY := quiet
endif
ifeq ($(V),2)
BUILD_VERBOSITY := verbose
endif
ifeq ($(V),3)
BUILD_VERBOSITY := debug
endif

# Colors
GREEN := \033[0;32m
YELLOW := \033[1;33m
BLUE := \033[0;34m
RED := \033[0;31m
NC := \033[0m # No Color

# =============================================================================
# Help
# =============================================================================

.PHONY: help

help: ## Show this help message
	@echo ""
	@echo "\u250c\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2510"
	@echo "\u2502              Meridian - Make Commands                   \u2502"
	@echo "\u2514\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2518"
	@echo ""
	@echo "$(BLUE)Installation:$(NC)"
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'install|setup' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Docker:$(NC)"
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'docker' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Development:$(NC)"
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'run|build|test|clean|bench|lint|watch|setup-dev|format' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Documentation:$(NC)"
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'docs|verify-adr|verify-contract|verify-tooling-metadata|gen-context|gen-interface|gen-structure|gen-provider|gen-workflow|update-claude' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Publishing:$(NC)"
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'publish' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Pre-PR & Quality:$(NC)"
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'pre-pr|ai-audit|ai-verify|ai-docs|ai-report|ai-arch|ai-maintenance' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-28s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Skills:$(NC)"
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'skill-' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-24s$(NC) %s\n", $$1, $$2}'
	@echo ""
	@echo "$(BLUE)Diagnostics:$(NC)"
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | grep -E 'doctor|diagnose|verify-setup|collect-debug|build-profile|build-binlog|build-graph|fingerprint|env-|impact|bisect|metrics|history|validate-data|analyze-errors' | awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""
