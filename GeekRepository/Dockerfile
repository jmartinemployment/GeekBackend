# Build from GeekBackend repo root (same clone logic as Dockerfile.repository):
#   docker build -f GeekRepository/Dockerfile -t geek-repository .
#
# Prefer Dockerfile.repository for Railway (see railway.json dockerfilePath).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
    test -f GeekSeo.Persistence/GeekSeo.Persistence.csproj
COPY GeekApplication/ GeekBackend/GeekApplication/
COPY GeekSa2Read/ GeekBackend/GeekSa2Read/
COPY GeekRepository/ GeekBackend/GeekRepository/
RUN dotnet restore GeekBackend/GeekRepository/GeekRepository.csproj \
    && dotnet publish GeekBackend/GeekRepository/GeekRepository.csproj \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        -o /app/publish

FROM mcr.microsoft.com/playwright/dotnet:v1.51.0-noble
WORKDIR /app
COPY --from=build /app/publish .
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN chmod +x ./GeekRepository
ENV PORT=5050
ENV ASPNETCORE_URLS=http://0.0.0.0:5050
EXPOSE 5050
ENTRYPOINT ["./GeekRepository"]
