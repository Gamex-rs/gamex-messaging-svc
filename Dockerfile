# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore first so the layer is cached until a project or package version changes.
# .editorconfig carries analyzer settings; without it warnings-as-errors fails the publish.
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props ./
COPY src/Gamex.Messaging.Domain/*.csproj src/Gamex.Messaging.Domain/
COPY src/Gamex.Messaging.Application/*.csproj src/Gamex.Messaging.Application/
COPY src/Gamex.Messaging.Infrastructure/*.csproj src/Gamex.Messaging.Infrastructure/
COPY src/Gamex.Messaging.Api/*.csproj src/Gamex.Messaging.Api/
RUN dotnet restore src/Gamex.Messaging.Api/Gamex.Messaging.Api.csproj

COPY src/ src/
RUN dotnet publish src/Gamex.Messaging.Api/Gamex.Messaging.Api.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gamex.Messaging.Api.dll"]
