# =============================================================================
# Dev Container – Dockerfile
# =============================================================================
# Extends the official .NET 10 devcontainer base image with Node 24 and Python
# 3.  Heavy tooling (dotnet restore, npm ci) is deferred to the
# onCreateCommand in devcontainer.json so the image layer stays cacheable.
# =============================================================================

FROM mcr.microsoft.com/devcontainers/dotnet:10.0

# Match the Node.js major used by the browser CI lane.
ARG NODE_VERSION=24
RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && nvm install ${NODE_VERSION} && nvm alias default ${NODE_VERSION}" 2>&1

# Install Python 3 + pip (slim, system-managed)
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    python3-venv \
    postgresql-client \
    && rm -rf /var/lib/apt/lists/*
