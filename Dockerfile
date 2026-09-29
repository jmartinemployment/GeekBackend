FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
# Nothing is fetched from outside the build context: GeekAPI's project graph is GeekApplication
# and itself.
COPY GeekApplication/ GeekBackend/GeekApplication/
COPY GeekAPI/ GeekBackend/GeekAPI/
RUN dotnet restore GeekBackend/GeekAPI/GeekAPI.csproj \
    && dotnet publish GeekBackend/GeekAPI/GeekAPI.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# Mobile Playwright hierarchy crawl (Content Creator) — Chromium + OS deps via Playwright CLI.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY --from=build /app/publish .

RUN apt-get update \
    && apt-get install -y --no-install-recommends wget ca-certificates \
    && wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends powershell \
    && pwsh ./playwright.ps1 install --with-deps chromium \
    && apt-get purge -y wget powershell \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/* /tmp/*

ENTRYPOINT ["dotnet", "GeekAPI.dll"]
