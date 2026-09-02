# NAS-only image (infrastructure/compose/docker-compose.nas.yml) — the Vue app runs on the host via
# Vite for local dev. Build context is the repo root (npm workspace lock lives there).
FROM node:22-alpine AS build
WORKDIR /repo

COPY package.json package-lock.json ./
COPY apps/dashboard-web/ apps/dashboard-web/
COPY packages/browser-sdk/package.json packages/browser-sdk/package.json
RUN npm ci -w dashboard-web

WORKDIR /repo/apps/dashboard-web

# Vite inlines these at build time, so they must be present before `npm run build`.
ARG VITE_API_BASE_URL
ARG VITE_HUBS_ORIGIN
ARG VITE_COLLECTOR_ORIGIN
ARG VITE_AZURE_AD_TENANT_ID
ARG VITE_AZURE_AD_CLIENT_ID
ARG VITE_AZURE_AD_API_SCOPE
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_HUBS_ORIGIN=$VITE_HUBS_ORIGIN \
    VITE_COLLECTOR_ORIGIN=$VITE_COLLECTOR_ORIGIN \
    VITE_AZURE_AD_TENANT_ID=$VITE_AZURE_AD_TENANT_ID \
    VITE_AZURE_AD_CLIENT_ID=$VITE_AZURE_AD_CLIENT_ID \
    VITE_AZURE_AD_API_SCOPE=$VITE_AZURE_AD_API_SCOPE
RUN npm run build

FROM nginx:1.27-alpine AS final
COPY apps/dashboard-web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /repo/apps/dashboard-web/dist /usr/share/nginx/html
