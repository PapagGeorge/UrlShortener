# syntax=docker/dockerfile:1
# Write Service.
# Build context is the REPOSITORY ROOT: docker build -f src/Services/WriteService/Dockerfile .
# Images are built by GitHub Actions (linux/amd64) and never on the dev Mac.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Build configuration and project files first, so the restore layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Shared/UrlShortener.Domain/UrlShortener.Domain.csproj src/Shared/UrlShortener.Domain/
COPY src/Shared/UrlShortener.BuildingBlocks/UrlShortener.BuildingBlocks.csproj src/Shared/UrlShortener.BuildingBlocks/
COPY src/Shared/UrlShortener.ServiceDefaults/UrlShortener.ServiceDefaults.csproj src/Shared/UrlShortener.ServiceDefaults/
COPY src/Services/WriteService/UrlShortener.WriteService.Application/UrlShortener.WriteService.Application.csproj src/Services/WriteService/UrlShortener.WriteService.Application/
COPY src/Services/WriteService/UrlShortener.WriteService.Infrastructure/UrlShortener.WriteService.Infrastructure.csproj src/Services/WriteService/UrlShortener.WriteService.Infrastructure/
COPY src/Services/WriteService/UrlShortener.WriteService.Api/UrlShortener.WriteService.Api.csproj src/Services/WriteService/UrlShortener.WriteService.Api/
RUN dotnet restore src/Services/WriteService/UrlShortener.WriteService.Api/UrlShortener.WriteService.Api.csproj

COPY src ./src
RUN dotnet publish src/Services/WriteService/UrlShortener.WriteService.Api/UrlShortener.WriteService.Api.csproj -c Release --no-restore -p:UseAppHost=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Port 8080 (the default of the .NET container images), non-root user provided by the base image.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "UrlShortener.WriteService.Api.dll"]
