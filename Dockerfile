# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer-cached on csproj changes only)
COPY Directory.Build.props ./
COPY src/ArbitrageTracker.Core/*.csproj            src/ArbitrageTracker.Core/
COPY src/ArbitrageTracker.Data/*.csproj            src/ArbitrageTracker.Data/
COPY src/ArbitrageTracker.Ingestion/*.csproj       src/ArbitrageTracker.Ingestion/
COPY src/ArbitrageTracker.Web/*.csproj             src/ArbitrageTracker.Web/
RUN dotnet restore src/ArbitrageTracker.Web/ArbitrageTracker.Web.csproj

# Build + publish
COPY src/ ./src/
RUN dotnet publish src/ArbitrageTracker.Web/ArbitrageTracker.Web.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

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
