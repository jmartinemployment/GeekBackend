FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY Geek-SEO.commit .
RUN set -eu; \
    REF="$(tr -d '[:space:]' < Geek-SEO.commit | grep -v '^#' | head -1)"; \
    git clone --filter=blob:none --no-checkout https://github.com/jmartinemployment/Geek-SEO.git Geek-SEO; \
    cd Geek-SEO; \
    git fetch --depth 1 origin "${REF}"; \
    git checkout FETCH_HEAD; \
    test -f GeekSeo.Application/GeekSeo.Application.csproj
# Mirror local monorepo layout so ../../Geek-SEO resolves from GeekBackend/* projects.
COPY GeekApplication/ GeekBackend/GeekApplication/
COPY GeekSa2Read/ GeekBackend/GeekSa2Read/
COPY GeekAPI/ GeekBackend/GeekAPI/
RUN dotnet restore GeekBackend/GeekAPI/GeekAPI.csproj \
    && dotnet publish GeekBackend/GeekAPI/GeekAPI.csproj -c Release -o /app/publish

# Mobile Playwright hierarchy crawl (Content Creator) — Chromium ships in this
# base image. Matches Dockerfile.repository, and the tag tracks the
# Microsoft.Playwright package version in GeekAPI.csproj (1.51.0): bump both together.
#
# Previously this built from dotnet/aspnet and installed Chromium by hand after
# COPYing the publish output. That put ~28 MB of apt indexes, PowerShell and a
# full browser install below a layer invalidated by every code change, so each
# deploy re-downloaded the lot — 3 minutes when archive.ubuntu.com was healthy,
# 28 minutes when it was not.
FROM mcr.microsoft.com/playwright/dotnet:v1.51.0-noble AS final
WORKDIR /app

ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "GeekAPI.dll"]
