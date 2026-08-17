# Build context is the repo root (see infrastructure/compose/docker-compose.yml) so this can COPY
# the packages/ project references the .csproj's relative ProjectReference paths resolve against.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY packages/dotnet-service-defaults/ packages/dotnet-service-defaults/
COPY services/identity-workspace/ services/identity-workspace/

WORKDIR /src/services/identity-workspace
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Telumera.Services.IdentityWorkspace.Api.dll"]
