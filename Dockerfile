# syntax=docker/dockerfile:1
# Multi-stage build for the LumenCue Cloud API (ChurchProjection.Api).
# Build context must be the ChurchProjection/ root so Directory.Build.props
# and the referenced Core project are available.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching): copy shared props + only the csprojs.
COPY Directory.Build.props ./
COPY src/ChurchProjection.Core/ChurchProjection.Core.csproj src/ChurchProjection.Core/
COPY src/ChurchProjection.Api/ChurchProjection.Api.csproj src/ChurchProjection.Api/
RUN dotnet restore src/ChurchProjection.Api/ChurchProjection.Api.csproj

# Copy the rest of the source these two projects need, then publish.
COPY src/ChurchProjection.Core/ src/ChurchProjection.Core/
COPY src/ChurchProjection.Api/ src/ChurchProjection.Api/
RUN dotnet publish src/ChurchProjection.Api/ChurchProjection.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
COPY workers/api/container-entrypoint.sh /container-entrypoint.sh
RUN chmod +x /container-entrypoint.sh

# Hosted containers exhaust inotify limits if config reload watchers are enabled.
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV PORT=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["/container-entrypoint.sh"]
