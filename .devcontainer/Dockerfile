# =============================================================================
# Dev Container – Dockerfile
# =============================================================================
# Extends the official .NET 9 devcontainer base image with Node 20 and Python
# 3.  Heavy tooling (dotnet restore, npm ci) is deferred to the
# onCreateCommand in devcontainer.json so the image layer stays cacheable.
# =============================================================================

FROM mcr.microsoft.com/devcontainers/dotnet:9.0

# Install Node.js 20.x (LTS)
ARG NODE_VERSION=20
RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && nvm install ${NODE_VERSION}" 2>&1

# Install Python 3 + pip (slim, system-managed)
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    python3-venv \
    postgresql-client \
    && rm -rf /var/lib/apt/lists/*
