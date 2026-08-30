// Subscription-scope entry point for the "Azure development deployment" backlog task — deploys the
// full control plane (gateway, identity-workspace, site-registry) on top of the platform baseline
// (main.bicep/modules/platform.bicep), for a one-time, deliberately throwaway test-and-tear-down
// validation, not a persistent environment. See modules/services.bicep's header for the full picture.
//
// Composes platform.bicep as a nested module rather than duplicating it, so main.bicep stays exactly
// what the separate "Bicep baseline" backlog task delivered.
//
// Secrets (ghcrToken, postgresAdminPassword) have no defaults and are never written to a committed
// .bicepparam file — supply them on the command line at deploy time:
//
//   az login
//   az deployment sub create \
//     --location northeurope \
//     --template-file deploy.bicep \
//     --parameters deploy.bicepparam \
//     --parameters ghcrToken=$env:GHCR_TOKEN postgresAdminPassword=$env:PG_ADMIN_PASSWORD
targetScope = 'subscription'

// Capped at 9, tighter than main.bicep's 12 — this template also creates Container Apps (32-char name
// limit), and "ca-identity-workspace-<env>" is the longest name in modules/services.bicep: 32 - "ca-"
// (3) - "identity-workspace" (19) - "-" (1) = 9 chars of budget left for environmentName.
@description('Short environment name used in resource naming and tagging (e.g. dev, staging).')
@minLength(1)
@maxLength(9)
param environmentName string = 'dev'

@description('Azure region for every resource. See main.bicep/README.md for why northeurope, not westeurope.')
param location string = 'northeurope'

@description('Resource group name. Defaults to the same name main.bicep uses, so this can deploy into the same group.')
param resourceGroupName string = 'rg-telumera-${environmentName}'

@description('GHCR image repository prefix, e.g. ghcr.io/anthony-air-03/telumera.')
param ghcrImagePrefix string

@description('GHCR username for pulling private images.')
param ghcrUsername string

@secure()
param ghcrToken string

@description('Microsoft Entra tenant ID (AZURE_AD_TENANT_ID in infrastructure/compose/.env).')
param entraTenantId string

@description('Telumera API app registration client ID (AZURE_AD_API_CLIENT_ID).')
param entraApiClientId string

@description('CORS_ALLOWED_ORIGINS equivalent for the deployed gateway.')
param corsAllowedOrigins string = ''

@secure()
param postgresAdminPassword string

@description('PostgreSQL Flexible Server administrator username.')
param postgresAdminUsername string = 'telumera_admin'

var tags = {
  project: 'telumera'
  environment: environmentName
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module platform 'modules/platform.bicep' = {
  name: 'telumera-platform'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module services 'modules/services.bicep' = {
  name: 'telumera-services'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    containerAppsEnvironmentName: platform.outputs.containerAppsEnvironmentName
    managedIdentityId: platform.outputs.managedIdentityId
    managedIdentityPrincipalId: platform.outputs.managedIdentityPrincipalId
    managedIdentityClientId: platform.outputs.managedIdentityClientId
    keyVaultName: platform.outputs.keyVaultName
    ghcrImagePrefix: ghcrImagePrefix
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    entraTenantId: entraTenantId
    entraApiClientId: entraApiClientId
    corsAllowedOrigins: corsAllowedOrigins
    postgresAdminPassword: postgresAdminPassword
    postgresAdminUsername: postgresAdminUsername
  }
}

output resourceGroupName string = resourceGroup.name
output gatewayFqdn string = services.outputs.gatewayFqdn
output postgresServerFqdn string = services.outputs.postgresServerFqdn
output serviceBusNamespaceName string = services.outputs.serviceBusNamespaceName
