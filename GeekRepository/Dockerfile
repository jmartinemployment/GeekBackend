# Build from GeekBackend repo root:
#   docker build -f GeekRepository/Dockerfile -t geek-repository .
#
# Prefer Dockerfile.repository for Railway (see railway.json dockerfilePath).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Nothing is fetched from outside the build context: GeekRepository's project graph is
# GeekApplication and itself.
COPY GeekApplication/ GeekBackend/GeekApplication/
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
