// Resource-group-scope module deployed by ../deploy.bicep, on top of ../platform.bicep's shared layer.
// Deploys "the control plane" (gateway, identity-workspace, site-registry — everything M00.4 actually
// built) plus what they need to run for real: a PostgreSQL Flexible Server with one database+role per
// context (docs/adr/0005-context-level-data-isolation.md), an Azure Service Bus namespace/topic backing
// Dapr's pubsub component (docs/adr/0002-dapr-pubsub-abstraction.md's Azure row), and three Container
// Apps wired with Dapr enabled, matching infrastructure/compose/docker-compose.yml's local topology
// (app-id per service, appPort only on the two that receive Dapr-invoked calls, gateway is the only one
// with external ingress).
//
// Deliberately pulls images from GHCR (ghcr.io/<repo>-<service>:latest, already published by
// .github/workflows/container-build.yml) rather than mirroring them into the ACR the baseline
// provisions — that registry stays available for a future in-Azure build pipeline, but duplicating
// already-working, already-tested images into it for this deployment has no functional benefit.
//
// Database roles/passwords are NOT created by this module — ARM has no Postgres role/user resource
// type. See scripts/init-postgres-databases.sh, run once against the server this module creates,
// mirroring infrastructure/compose/db-init/postgres/00-init-context-databases.sh's exact roles
// (svc_access, svc_sites) so the isolation convention matches local dev exactly, per ADR 0005's own
// explicit call for this to be "scripted ... as part of ... the Azure Bicep templates."

@description('Short environment name used in resource naming and tagging (e.g. dev, staging).')
param environmentName string

@description('Azure region for every resource.')
param location string

@description('Tags applied to every resource in this module.')
param tags object

@description('Name of the Container Apps environment from platform.bicep.')
param containerAppsEnvironmentName string

@description('Resource ID of the user-assigned managed identity from platform.bicep.')
param managedIdentityId string

@description('Principal (object) ID of the managed identity — for role assignments.')
param managedIdentityPrincipalId string

@description('Client ID of the managed identity — for Dapr\'s Azure AD component auth.')
param managedIdentityClientId string

@description('Key Vault name from platform.bicep — service secrets are written here.')
param keyVaultName string

@description('GHCR image repository prefix, e.g. ghcr.io/anthony-air-03/telumera.')
param ghcrImagePrefix string

@description('GHCR username for pulling private images (a GitHub account, not an email).')
param ghcrUsername string

@secure()
@description('GitHub personal access token with read:packages scope, for pulling private GHCR images. Never committed — pass at deploy time.')
param ghcrToken string

@description('Microsoft Entra tenant ID (AZURE_AD_TENANT_ID in infrastructure/compose/.env) — account-specific, not a secret, but not a committed default either.')
param entraTenantId string

@description('Telumera API app registration client ID (AZURE_AD_API_CLIENT_ID) — account-specific, not a secret.')
param entraApiClientId string

@description('CORS_ALLOWED_ORIGINS equivalent for the deployed gateway (e.g. the dashboard-web dev server or its own future deployed origin).')
param corsAllowedOrigins string = ''

@secure()
@description('PostgreSQL Flexible Server administrator password. Never committed — pass at deploy time.')
param postgresAdminPassword string

@description('PostgreSQL Flexible Server administrator username.')
param postgresAdminUsername string = 'telumera_admin'

var uniqueSuffix = uniqueString(resourceGroup().id)
var postgresServerName = 'psql-telumera-${environmentName}-${uniqueSuffix}'
var serviceBusNamespaceName = 'sb-telumera-${environmentName}-${uniqueSuffix}'

// Azure Service Bus Data Sender — the topic already exists via this module (siteEventsTopic below), so
// Dapr's component only needs to publish, not manage entities.
var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// --- PostgreSQL --------------------------------------------------------------------------------------

resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminUsername
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
  }
}

// Container Apps (Consumption plan) has no fixed outbound IP without VNet integration, which this dev
// baseline deliberately doesn't add — this well-known sentinel range (0.0.0.0-0.0.0.0) is Azure's
// documented "allow trusted Azure services" firewall exception, not a literal open-to-the-internet rule.
// A real (non-throwaway) deployment should add VNet integration + a private endpoint instead.
resource postgresFirewallAllowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgresServer
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource identityWorkspaceDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgresServer
  name: 'telumera_access'
}

resource siteRegistryDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgresServer
  name: 'telumera_sites'
}

// --- Service Bus (Dapr pubsub backend, per ADR 0002's Azure row) -------------------------------------

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: serviceBusNamespaceName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}

// Matches site-registry/OutboxPublisher.cs's Topic constant ("site-events") — one topic per context,
// not per event type, per services/site-registry/README.md.
resource siteEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBusNamespace
  name: 'site-events'
}

resource serviceBusDataSenderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, managedIdentityPrincipalId, serviceBusDataSenderRoleId)
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Dapr's Azure AD auth mode (namespaceName + azureClientId) instead of a connection string — no Service
// Bus secret anywhere, matching the managed-identity-first approach the rest of this baseline already
// uses for ACR/Key Vault. Scoped to site-registry only — it's the only current publisher (see
// services/site-registry/README.md's "No consumer yet"), matching the local pubsub-rabbitmq.yaml
// component's own scoping via resiliency.yaml.
resource pubsubComponent 'Microsoft.App/managedEnvironments/daprComponents@2024-03-01' = {
  parent: containerAppsEnvironment
  name: 'pubsub'
  properties: {
    componentType: 'pubsub.azure.servicebus.topics'
    version: 'v1'
    metadata: [
      {
        name: 'namespaceName'
        value: '${serviceBusNamespace.name}.servicebus.windows.net'
      }
      {
        name: 'azureClientId'
        value: managedIdentityClientId
      }
    ]
    scopes: [
      'site-registry'
    ]
  }
  dependsOn: [
    serviceBusDataSenderAssignment
  ]
}

// --- Secrets (written by the deployer's own identity, read back by the managed identity at container
// startup via Key Vault references below — Key Vault Secrets User, granted in platform.bicep, is
// read-only, so these Secret resources are written by whoever runs `az deployment`, not by the apps) ---

resource identityWorkspaceDbSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'identity-workspace-db-connection-string'
  properties: {
    value: 'Host=${postgresServer.properties.fullyQualifiedDomainName};Port=5432;Database=telumera_access;Username=${postgresAdminUsername};Password=${postgresAdminPassword};Ssl Mode=Require'
  }
}

resource siteRegistryDbSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'site-registry-db-connection-string'
  properties: {
    value: 'Host=${postgresServer.properties.fullyQualifiedDomainName};Port=5432;Database=telumera_sites;Username=${postgresAdminUsername};Password=${postgresAdminPassword};Ssl Mode=Require'
  }
}

// --- Container Apps ------------------------------------------------------------------------------------

resource identityWorkspaceApp 'Microsoft.App/containerApps@2024-03-01' = {
  // Container App names are capped at 32 chars — "identity-workspace" alone is 19, so this drops the
  // "telumera-" prefix other resources use (the resource group is already telumera-scoped).
  name: 'ca-identity-workspace-${environmentName}'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: 'ghcr.io'
          username: ghcrUsername
          passwordSecretRef: 'ghcr-token'
        }
      ]
      secrets: [
        {
          name: 'ghcr-token'
          value: ghcrToken
        }
        {
          name: 'db-connection-string'
          keyVaultUrl: identityWorkspaceDbSecret.properties.secretUri
          identity: managedIdentityId
        }
      ]
      dapr: {
        enabled: true
        appId: 'identity-workspace'
        appPort: 8080
        appProtocol: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'identity-workspace'
          image: '${ghcrImagePrefix}-identity-workspace:latest'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ConnectionStrings__IdentityWorkspace', secretRef: 'db-connection-string' }
            { name: 'AzureAd__TenantId', value: entraTenantId }
            { name: 'AzureAd__ClientId', value: entraApiClientId }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource siteRegistryApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-site-registry-${environmentName}'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: 'ghcr.io'
          username: ghcrUsername
          passwordSecretRef: 'ghcr-token'
        }
      ]
      secrets: [
        {
          name: 'ghcr-token'
          value: ghcrToken
        }
        {
          name: 'db-connection-string'
          keyVaultUrl: siteRegistryDbSecret.properties.secretUri
          identity: managedIdentityId
        }
      ]
      dapr: {
        enabled: true
        appId: 'site-registry'
        appPort: 8080
        appProtocol: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'site-registry'
          image: '${ghcrImagePrefix}-site-registry:latest'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ConnectionStrings__SiteRegistry', secretRef: 'db-connection-string' }
            { name: 'AzureAd__TenantId', value: entraTenantId }
            { name: 'AzureAd__ClientId', value: entraApiClientId }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    identityWorkspaceApp // MembershipClient calls identity-workspace via Dapr invoke — needs to exist first
  ]
}

resource gatewayApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-gateway-${environmentName}'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: 'ghcr.io'
          username: ghcrUsername
          passwordSecretRef: 'ghcr-token'
        }
      ]
      secrets: [
        {
          name: 'ghcr-token'
          value: ghcrToken
        }
      ]
      dapr: {
        enabled: true
        appId: 'gateway'
        appProtocol: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'gateway'
          image: '${ghcrImagePrefix}-gateway:latest'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'AzureAd__TenantId', value: entraTenantId }
            { name: 'AzureAd__ClientId', value: entraApiClientId }
            { name: 'Cors__AllowedOrigins', value: corsAllowedOrigins }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    identityWorkspaceApp
    siteRegistryApp
  ]
}

output postgresServerFqdn string = postgresServer.properties.fullyQualifiedDomainName
output serviceBusNamespaceName string = serviceBusNamespace.name
output gatewayFqdn string = gatewayApp.properties.configuration.ingress.fqdn
