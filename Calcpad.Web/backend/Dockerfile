# ========== Build stage ==========
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the entire CalcpadVM directory to access all project dependencies
COPY . .

# Restore and build
WORKDIR /src/Calcpad.Web/backend
RUN dotnet restore "Calcpad.Server.csproj"
RUN dotnet build "Calcpad.Server.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "Calcpad.Server.csproj" -c Release -o /app/publish

# Strip non-linux platform runtimes (SkiaSharp, etc.)
RUN find /app/publish/runtimes -mindepth 1 -maxdepth 1 -type d \
    ! -name 'linux-x64' -exec rm -rf {} + 2>/dev/null || true

# ========== Runtime stage ==========
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install Chromium and headless browser dependencies (no Node.js)
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        chromium \
        fonts-liberation \
        libasound2t64 \
        libatk-bridge2.0-0 \
        libdrm2 \
        libgtk-3-0 \
        libnspr4 \
        libnss3 \
        libxcomposite1 \
        libxdamage1 \
        libxrandr2 \
        xvfb && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Copy published application (already stripped to linux-x64 only)
COPY --from=publish /app/publish .

# Copy Calcpad resources
COPY --from=build /src/Calcpad.Cli/doc ./doc
COPY --from=build /src/Calcpad.Cli/Fonts ./Fonts

# Environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV CALCPAD_PORT=8080
ENV CALCPAD_HOST=*
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV XDG_CONFIG_HOME=/tmp/.chromium
ENV XDG_CACHE_HOME=/tmp/.chromium
ENV BROWSER_PATH=/usr/bin/chromium

# Create non-root user and directories
RUN groupadd -r calcpad && useradd -r -g calcpad calcpad
RUN mkdir -p /tmp/chrome-crashpad /tmp/.chromium /app/data && \
    chown -R calcpad:calcpad /tmp/chrome-crashpad /tmp/.chromium /app/data /app
USER calcpad

EXPOSE 8080

# Single process entrypoint
ENTRYPOINT ["dotnet", "Calcpad.Server.dll"]
