FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY GEEKBACKEND.slnx ./
COPY GeekAPI/GeekAPI.csproj GeekAPI/
COPY GeekApplication/GeekApplication.csproj GeekApplication/
COPY GeekRepository/GeekRepository.csproj GeekRepository/

# Restore dependencies
RUN dotnet restore GeekAPI/GeekAPI.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish GeekAPI/GeekAPI.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GeekAPI.dll"]
