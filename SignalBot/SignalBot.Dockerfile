# Multi-stage Dockerfile for SignalBot
# Optimized for Raspberry Pi 4 (ARM64) and x64 architectures

# Stage 1: Build
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy project files first (better layer caching)
COPY SignalBot/SignalBot.csproj SignalBot/
COPY TradingBot.Core/TradingBot.Core.csproj TradingBot.Core/
COPY TradingBot.Binance/TradingBot.Binance.csproj TradingBot.Binance/

# Restore dependencies
RUN dotnet restore SignalBot/SignalBot.csproj -a $TARGETARCH

# Copy source code
COPY SignalBot/ SignalBot/
COPY TradingBot.Core/ TradingBot.Core/
COPY TradingBot.Binance/ TradingBot.Binance/

# Build and publish
RUN dotnet publish SignalBot/SignalBot.csproj \
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
RUN groupadd -r signalbot && useradd -r -g signalbot signalbot

# Create directories for data persistence
RUN mkdir -p /app/logs /app/data/state /app/data/telegram && \
    chown -R signalbot:signalbot /app

# Copy published application
COPY --from=build --chown=signalbot:signalbot /app/publish .

# Copy configuration files
COPY --chown=signalbot:signalbot SignalBot/appsettings.json .

# Switch to non-root user
USER signalbot

# Environment variables (can be overridden)
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV TZ=UTC

# Healthcheck - verify the app can start
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD pgrep -f SignalBot || exit 1

# Entry point
ENTRYPOINT ["dotnet", "SignalBot.dll"]
