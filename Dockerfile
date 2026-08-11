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
    test -f GeekSeo.Application/GeekSeo.Application.csproj
# Mirror local monorepo layout so ../../Geek-SEO resolves from GeekBackend/* projects.
COPY GeekApplication/ GeekBackend/GeekApplication/
COPY GeekSa2Read/ GeekBackend/GeekSa2Read/
COPY GeekAPI/ GeekBackend/GeekAPI/
RUN dotnet restore GeekBackend/GeekAPI/GeekAPI.csproj \
    && dotnet publish GeekBackend/GeekAPI/GeekAPI.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GeekAPI.dll"]
