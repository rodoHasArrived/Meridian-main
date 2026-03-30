# =============================================================================
# Meridian - Production Docker Image
# =============================================================================
# Multi-stage build for optimized production deployment
#
# Build:   docker build -t meridian:latest .
# Run:     docker run -p 8080:8080 -v ./data:/app/data meridian:latest
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Build
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /src

# Note: Build context should be the repo root (docker build -f deploy/docker/Dockerfile .)
# Copy solution and project files first for layer caching
COPY Meridian.sln Directory.Build.props Directory.Packages.props global.json ./
COPY src/Meridian/Meridian.csproj src/Meridian/
COPY src/Meridian.Application/Meridian.Application.csproj src/Meridian.Application/
COPY src/Meridian.Contracts/Meridian.Contracts.csproj src/Meridian.Contracts/
COPY src/Meridian.Core/Meridian.Core.csproj src/Meridian.Core/
COPY src/Meridian.Domain/Meridian.Domain.csproj src/Meridian.Domain/
COPY src/Meridian.Infrastructure/Meridian.Infrastructure.csproj src/Meridian.Infrastructure/
COPY src/Meridian.ProviderSdk/Meridian.ProviderSdk.csproj src/Meridian.ProviderSdk/
COPY src/Meridian.Storage/Meridian.Storage.csproj src/Meridian.Storage/
COPY src/Meridian.Ui/Meridian.Ui.csproj src/Meridian.Ui/
COPY src/Meridian.Ui.Services/Meridian.Ui.Services.csproj src/Meridian.Ui.Services/
COPY src/Meridian.Ui.Shared/Meridian.Ui.Shared.csproj src/Meridian.Ui.Shared/
COPY src/Meridian.FSharp/Meridian.FSharp.fsproj src/Meridian.FSharp/
COPY tests/Meridian.Tests/Meridian.Tests.csproj tests/Meridian.Tests/
COPY tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj tests/Meridian.FSharp.Tests/
COPY benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj benchmarks/Meridian.Benchmarks/

# Restore dependencies (target linux-musl-x64 to match the Alpine runtime image)
RUN dotnet restore src/Meridian/Meridian.csproj --runtime linux-musl-x64

# Copy source code
COPY src/ src/
COPY tests/ tests/
COPY benchmarks/ benchmarks/

# Build and publish
RUN dotnet publish src/Meridian/Meridian.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    --runtime linux-musl-x64 \
    -p:PublishSingleFile=false \
    -p:SelfContained=false \
    -p:PublishTrimmed=false

# -----------------------------------------------------------------------------
# Stage 2: Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

# Install required packages
RUN apk add --no-cache \
    tzdata \
    curl \
    && rm -rf /var/cache/apk/*

# Create non-root user for security
RUN addgroup -g 1000 meridian && \
    adduser -u 1000 -G meridian -s /bin/sh -D meridian

WORKDIR /app

# Copy published application
COPY --from=build /app/publish ./

# Copy configuration template
COPY config/appsettings.sample.json ./appsettings.sample.json

# Create data directory with proper permissions
RUN mkdir -p /app/data /app/logs && \
    chown -R meridian:meridian /app

# Switch to non-root user
USER meridian

# Environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV TZ=UTC
ENV MDC_DATA_ROOT=/app/data

# Expose ports
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Default entrypoint - web dashboard mode
ENTRYPOINT ["dotnet", "Meridian.dll"]
CMD ["--ui", "--http-port", "8080", "--watch-config"]

# =============================================================================
# Usage Examples:
# =============================================================================
#
# Build the image:
#   docker build -t meridian:latest .
#
# Run web dashboard:
#   docker run -d -p 8080:8080 \
#     -v $(pwd)/data:/app/data \
#     -v $(pwd)/appsettings.json:/app/appsettings.json:ro \
#     --name meridian meridian:latest
#
# Run with custom port:
#   docker run -d -p 9000:8080 \
#     -e ASPNETCORE_URLS=http://+:8080 \
#     meridian:latest
#
# Run backfill:
#   docker run --rm \
#     -v $(pwd)/data:/app/data \
#     meridian:latest \
#     --backfill --backfill-symbols AAPL,MSFT --backfill-from 2024-01-01
#
# Run with Alpaca credentials:
#   docker run -d -p 8080:8080 \
#     -e ALPACA__KEYID=your-key-id \
#     -e ALPACA__SECRETKEY=your-secret-key \
#     -v $(pwd)/data:/app/data \
#     meridian:latest
#
# View logs:
#   docker logs -f meridian
#
# =============================================================================
