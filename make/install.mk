# =============================================================================
# Installation & Docker
# =============================================================================

.PHONY: quickstart install install-docker install-native setup-config check-deps \
        docker docker-build docker-up docker-down docker-logs docker-restart \
        docker-clean docker-monitoring

# Compose model. Teardown must name the same files the start command did: services and
# named volumes absent from the model passed to `down` are neither stopped nor removed, so
# tearing down with the base file alone would strand the monitoring containers and volumes.
COMPOSE_BASE := -f deploy/docker/docker-compose.yml
COMPOSE_ALL := $(COMPOSE_BASE) -f deploy/docker/docker-compose.monitoring.yml

# Compose interpolates every variable before running any command, including `down`, so the
# override's required Grafana credentials would block teardown for anyone who never set them.
# Teardown does not authenticate to Grafana; it only needs the file to parse, so satisfy the
# requirement with placeholders rather than weakening it to a default in the file itself.
COMPOSE_TEARDOWN_ENV := GF_SECURITY_ADMIN_USER=teardown GF_SECURITY_ADMIN_PASSWORD=teardown

quickstart: ## Zero-to-running setup for new contributors
	@echo ""
	@echo "$(BLUE)Meridian - Quick Start$(NC)"
	@echo "======================================"
	@echo ""
	@echo "$(BLUE)[1/5] Checking .NET 10 SDK...$(NC)"
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
	@python3 build/python/cli/buildctl.py test --project $(TEST_PROJECT) --configuration Release --verbosity quiet --queue
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
	@echo "     make run"
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
	docker compose $(COMPOSE_BASE) up -d
	@echo "$(GREEN)Container started!$(NC)"
	@echo "  API:       http://localhost:$(HTTP_PORT)"
	@echo "  Health:    http://localhost:$(HTTP_PORT)/health"
	@echo "  Metrics:   http://localhost:$(HTTP_PORT)/metrics"

docker-down: ## Stop Docker containers (application and monitoring)
	$(COMPOSE_TEARDOWN_ENV) docker compose $(COMPOSE_ALL) down --remove-orphans

docker-logs: ## View Docker logs
	docker compose $(COMPOSE_BASE) logs -f

docker-restart: ## Restart Docker container
	docker compose $(COMPOSE_BASE) restart

docker-clean: ## Remove Docker containers, volumes, and images (application and monitoring)
	$(COMPOSE_TEARDOWN_ENV) docker compose $(COMPOSE_ALL) down -v --remove-orphans
	docker rmi $(DOCKER_IMAGE) 2>/dev/null || true

docker-monitoring: ## Start with Prometheus and Grafana (requires GF_SECURITY_ADMIN_USER/PASSWORD)
	docker compose $(COMPOSE_ALL) up -d
	@echo "$(GREEN)Monitoring stack started!$(NC)"
	@echo "  Prometheus: http://localhost:9090"
	@echo "  Grafana:    http://localhost:3000 (sign in with $$GF_SECURITY_ADMIN_USER)"
