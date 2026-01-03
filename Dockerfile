# Multi-stage Dockerfile for TradingBot
# Optimized for Raspberry Pi 4 (ARM64) and x64 architectures

# Stage 1: Build
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy solution and project files first (better layer caching)
COPY TradingBot.sln ./
COPY ComplexBot/ComplexBot.csproj ComplexBot/
COPY ComplexBot.Tests/ComplexBot.Tests.csproj ComplexBot.Tests/
COPY ComplexBot.Integration/ComplexBot.Integration.csproj ComplexBot.Integration/

# Restore dependencies
RUN dotnet restore ComplexBot/ComplexBot.csproj -a $TARGETARCH

# Copy source code
COPY ComplexBot/ ComplexBot/

# Build and publish
RUN dotnet publish ComplexBot/ComplexBot.csproj \
    -c Release \
    -a $TARGETARCH \
    --no-restore \
    -o /app/publish \
    /p:PublishSingleFile=false \
    /p:PublishTrimmed=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r tradingbot && useradd -r -g tradingbot tradingbot

# Create directories for data persistence
RUN mkdir -p /app/HistoricalData /app/logs && \
    chown -R tradingbot:tradingbot /app

# Copy published application
COPY --from=build --chown=tradingbot:tradingbot /app/publish .

# Copy configuration files
COPY --chown=tradingbot:tradingbot ComplexBot/appsettings.json .

# Switch to non-root user
USER tradingbot

# Environment variables (can be overridden)
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV TZ=UTC

# Healthcheck - verify the app can start
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD pgrep -f ComplexBot || exit 1

# Entry point
ENTRYPOINT ["dotnet", "ComplexBot.dll"]
