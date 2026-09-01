# Build context is the repo root (see infrastructure/compose/docker-compose.yml) so this can COPY
# the packages/ project references the .csproj's relative ProjectReference paths resolve against.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY packages/dotnet-service-defaults/ packages/dotnet-service-defaults/
COPY packages/event-contracts/ packages/event-contracts/
COPY services/event-collector/ services/event-collector/

WORKDIR /src/services/event-collector
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Telumera.Services.EventCollector.Api.dll"]
