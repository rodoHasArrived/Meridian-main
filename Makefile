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
	@python build/scripts/docs/render-make-help.py $(MAKEFILE_LIST)
