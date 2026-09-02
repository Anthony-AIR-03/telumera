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

# M01.8: build the browser SDK bundle so the collector can serve it at /telumera.js — one public
# origin for both the SDK script and the /v1/events endpoint the install snippet points at.
FROM node:22-alpine AS sdk-build
WORKDIR /repo
COPY package.json package-lock.json ./
COPY apps/dashboard-web/package.json apps/dashboard-web/package.json
COPY packages/browser-sdk/ packages/browser-sdk/
RUN npm ci -w @telumera/browser-sdk
RUN npm run build:js -w @telumera/browser-sdk

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=sdk-build /repo/packages/browser-sdk/dist/browser-sdk.global.js ./sdk/browser-sdk.global.js
ENV Sdk__BundlePath=/app/sdk/browser-sdk.global.js

ENTRYPOINT ["dotnet", "Telumera.Services.EventCollector.Api.dll"]
