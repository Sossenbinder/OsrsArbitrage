# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Publish with a single restore. NOTE: do NOT split into `dotnet restore <web.csproj>` +
# `dotnet publish --no-restore` — a targeted restore drops the framework static web assets
# (e.g. _framework/blazor.web.js) in a clean build, so Blazor never loads. Let publish restore.
COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/ArbitrageTracker.Web/ArbitrageTracker.Web.csproj \
    -c Release -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1
EXPOSE 8080

# SQLite DB lives here; mount a volume to persist it across deploys.
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "ArbitrageTracker.Web.dll"]
