# Build context is the repo root (see infrastructure/compose/docker-compose.yml) so this can COPY
# the packages/ project references the .csproj's relative ProjectReference paths resolve against.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY packages/dotnet-service-defaults/ packages/dotnet-service-defaults/
COPY packages/event-contracts/ packages/event-contracts/
COPY packages/outbox/ packages/outbox/
COPY services/site-registry/ services/site-registry/

WORKDIR /src/services/site-registry
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Telumera.Services.SiteRegistry.Api.dll"]
