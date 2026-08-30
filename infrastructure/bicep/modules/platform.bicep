// Resource-group-scope module deployed by ../main.bicep. Provisions the shared platform layer every
// later Container App will run against: a Container Apps environment, a container registry, an
// observability pair (Log Analytics + Application Insights), a secret store (Key Vault), and a managed
// identity wired with just enough RBAC to pull from the registry and read Key Vault secrets — an ACR
// nobody can pull from or a vault nobody can read isn't a usable baseline, so that wiring is included
// here rather than left for the next task to rediscover.
//
// No actual Container Apps (running services) are declared here — see main.bicep's header comment for
// why that's the separate "Azure development deployment" task's job, not this one's.

@description('Short environment name used in resource naming and tagging (e.g. dev, staging).')
param environmentName string

@description('Azure region for every resource.')
param location string

@description('Tags applied to every resource in this module.')
param tags object

// Global-uniqueness suffix for resources that need a globally unique name (ACR, Key Vault) — derived
// deterministically from the resource group, so it stays stable across redeployments instead of changing
// every run.
var uniqueSuffix = uniqueString(resourceGroup().id)

var logAnalyticsName = 'log-telumera-${environmentName}'
var appInsightsName = 'appi-telumera-${environmentName}'
var containerRegistryName = 'acrtelumera${environmentName}${uniqueSuffix}'
var keyVaultName = 'kv-telumera-${environmentName}-${uniqueSuffix}'
var containerAppsEnvironmentName = 'cae-telumera-${environmentName}'
var managedIdentityName = 'id-telumera-${environmentName}'

// Built-in role definition IDs (stable across all Azure tenants — these are not tenant-specific GUIDs).
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    // Dev environment — keep retention (and cost) low; revisit before this ever fronts real traffic.
    retentionInDays: 30
  }
}

// Workspace-based Application Insights (the modern mode — routes through the Log Analytics workspace
// above rather than its own classic storage). Provisioning the resource is this task's scope; wiring an
// actual OTel exporter to it (Azure Monitor doesn't accept raw OTLP the way infrastructure/observability/
// 's local collector does) is application-level work for the deployment task, not this baseline.
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  tags: tags
  sku: {
    // Dev-tier registry — Basic is enough for a handful of low-traffic services at low volume.
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    // RBAC over legacy access policies — matches how the managed identity below is granted access,
    // per current Azure guidance.
    enableRbacAuthorization: true
    enableSoftDelete: true
  }
}

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: managedIdentityName
  location: location
  tags: tags
}

resource acrPullAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, managedIdentity.id, acrPullRoleId)
  scope: containerRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource keyVaultSecretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, managedIdentity.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

output containerAppsEnvironmentId string = containerAppsEnvironment.id
output containerAppsEnvironmentDefaultDomain string = containerAppsEnvironment.properties.defaultDomain
output containerRegistryLoginServer string = containerRegistry.properties.loginServer
output keyVaultUri string = keyVault.properties.vaultUri
output logAnalyticsWorkspaceId string = logAnalytics.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output managedIdentityId string = managedIdentity.id
output managedIdentityClientId string = managedIdentity.properties.clientId
