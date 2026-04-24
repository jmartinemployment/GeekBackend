FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy both projects
COPY GeekBackend.Api/ ./GeekBackend.Api/
COPY GeekBackend.Data/ ./GeekBackend.Data/

# Restore and publish
RUN dotnet restore "GeekBackend.Api/GeekBackend.Api.csproj"
RUN dotnet publish "GeekBackend.Api/GeekBackend.Api.csproj" \
    -c Release \
    --no-restore \
    -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GeekBackend.Api.dll"]