// Subscription-scope entry point for Telumera's Azure development environment (M00.5).
//
// Scope deliberately matches the "Bicep baseline" backlog task, not the full Azure portability table in
// planning/Telumera_Modular_Project_Plan.md §3.4: Container Apps environment, container registry,
// observability, and secret resources only. No PostgreSQL, Service Bus, Blob Storage, or actual container
// apps here — those need a real service to deploy before they're worth provisioning (continuous cost for
// nothing to use them), and belong to the separate, still-open "Create Azure development deployment"
// backlog task ("Deploy the control plane and one sample service").
//
// No account-specific values anywhere: everything below is a parameter with a generic default, same
// portability rule docs/architecture/nas-deployment-profile.md already follows. Deploy with your own
// logged-in `az` session — nothing here assumes a specific subscription, tenant, or resource names.
//
// Usage:
//   az login
//   az deployment sub create \
//     --location westeurope \
//     --template-file main.bicep \
//     --parameters main.bicepparam
targetScope = 'subscription'

@description('Short environment name used in resource naming and tagging (e.g. dev, staging).')
@minLength(1)
@maxLength(12)
param environmentName string = 'dev'

@description('Azure region for every resource.')
param location string = 'westeurope'

@description('Resource group name. Defaults to a name derived from environmentName so most deployments never need to set this explicitly.')
param resourceGroupName string = 'rg-telumera-${environmentName}'

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

output resourceGroupName string = resourceGroup.name
output containerAppsEnvironmentId string = platform.outputs.containerAppsEnvironmentId
output containerAppsEnvironmentDefaultDomain string = platform.outputs.containerAppsEnvironmentDefaultDomain
output containerRegistryLoginServer string = platform.outputs.containerRegistryLoginServer
output keyVaultUri string = platform.outputs.keyVaultUri
output logAnalyticsWorkspaceId string = platform.outputs.logAnalyticsWorkspaceId
output appInsightsConnectionString string = platform.outputs.appInsightsConnectionString
output managedIdentityId string = platform.outputs.managedIdentityId
output managedIdentityClientId string = platform.outputs.managedIdentityClientId
