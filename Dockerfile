# syntax=docker/dockerfile:1.7

# ---------- Stage: dev (SDK image for running dotnet commands & hot reload) ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dev
WORKDIR /workspace
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_USE_POLLING_FILE_WATCHER=1
# Source is bind-mounted in compose, so nothing is copied here.
CMD ["dotnet", "watch", "run", "--project", "src/HookRelay", "--urls", "http://0.0.0.0:8080"]

# ---------- Stage: build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Restore first for better layer caching
COPY global.json Directory.Build.props .editorconfig ./
COPY src/HookRelay/HookRelay.csproj src/HookRelay/
RUN dotnet restore src/HookRelay/HookRelay.csproj

COPY src/HookRelay/ src/HookRelay/
RUN dotnet publish src/HookRelay/HookRelay.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Stage: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user (the aspnet image ships an 'app' user since .NET 8)
COPY --from=build --chown=app:app /app/publish .
USER app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimit=0x18000000

# Render injects $PORT; default to 8080 locally.
# Shell form so ${PORT} is expanded at runtime.
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet HookRelay.dll"]