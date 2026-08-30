// Non-secret parameters for deploy.bicep's dev environment. ghcrToken and postgresAdminPassword have
// no defaults and are NOT set here — pass them on the command line at deploy time
// (--parameters ghcrToken=... postgresAdminPassword=...), never committed.
using 'deploy.bicep'

param environmentName = 'dev'
param location = 'northeurope'

// This repo's own actual GHCR namespace (already public — .github/workflows/container-build.yml
// publishes here) — not a secret, but change both if deploying a fork under a different account.
param ghcrImagePrefix = 'ghcr.io/anthony-air-03/telumera'
param ghcrUsername = 'anthony-air-03'
param entraTenantId = readEnvironmentVariable('AZURE_AD_TENANT_ID', '')
param entraApiClientId = readEnvironmentVariable('AZURE_AD_API_CLIENT_ID', '')
