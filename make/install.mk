# =============================================================================
# Installation & Docker
# =============================================================================

.PHONY: quickstart install install-docker install-native setup-config check-deps \
        docker docker-build docker-up docker-down docker-logs docker-restart \
        docker-clean docker-monitoring

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
