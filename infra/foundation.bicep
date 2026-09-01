targetScope = 'resourceGroup'

@minLength(2)
@maxLength(12)
@description('Short deployment environment name, such as prod.')
param environmentName string

@description('Azure region selected during production qualification.')
param location string = resourceGroup().location

@description('Object ID of the GitHub OIDC deployment service principal.')
@secure()
param deploymentPrincipalObjectId string

@description('Email address for the production Azure Monitor action group.')
@secure()
param alertEmail string

@allowed([
  'Basic'
  'Standard'
  'Premium'
])
@description('Azure Container Registry SKU selected during production qualification.')
param containerRegistrySkuName string

@description('PostgreSQL compute SKU selected during production qualification.')
param postgresSkuName string

@allowed([
  'Burstable'
  'GeneralPurpose'
  'MemoryOptimized'
])
@description('PostgreSQL compute tier selected during production qualification.')
param postgresTier string

@allowed([
  'Disabled'
  'SameZone'
  'ZoneRedundant'
])
@description('PostgreSQL HA mode selected from the accepted RTO and RPO.')
param postgresHighAvailability string

@minValue(7)
@maxValue(35)
param postgresBackupRetentionDays int

@allowed([
  'Disabled'
  'Enabled'
])
param postgresGeoRedundantBackup string

@minValue(32)
param postgresStorageSizeGB int

@description('Maintenance day, where 0 is Sunday.')
@minValue(0)
@maxValue(6)
param postgresMaintenanceDay int

@minValue(0)
@maxValue(23)
param postgresMaintenanceHour int

param virtualNetworkAddressPrefix string = '10.42.0.0/16'
param containerAppsSubnetPrefix string = '10.42.0.0/23'
param postgresSubnetPrefix string = '10.42.4.0/28'
param privateEndpointSubnetPrefix string = '10.42.5.0/27'
param tags object = {}

var normalizedEnvironment = toLower(replace(environmentName, '_', '-'))
var suffix = take(uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName), 8)
var baseName = 'll-${normalizedEnvironment}-${suffix}'
var registryName = 'll${uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName, 'registry')}'
var storageName = 'll${uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName, 'storage')}'
var commonTags = union(tags, {
  application: 'logic-lab'
  environment: environmentName
  'managed-by': 'bicep'
})
var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)
var acrPushRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '8311e382-0749-4cb8-b61a-304f252e45ec'
)
var blobContributorRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
)
var monitoringMetricsPublisherRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '3913510d-42f4-4e42-8a64-420c390055eb'
)

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2025-05-01' = {
  name: '${baseName}-vnet'
  location: location
  tags: commonTags
  properties: {
    addressSpace: {
      addressPrefixes: [
        virtualNetworkAddressPrefix
      ]
    }
    subnets: [
      {
        name: 'container-apps'
        properties: {
          addressPrefix: containerAppsSubnetPrefix
          delegations: [
            {
              name: 'container-apps'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'postgres'
        properties: {
          addressPrefix: postgresSubnetPrefix
          delegations: [
            {
              name: 'postgres'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource containerAppsSubnet 'Microsoft.Network/virtualNetworks/subnets@2025-05-01' existing = {
  parent: virtualNetwork
  name: 'container-apps'
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2025-05-01' existing = {
  parent: virtualNetwork
  name: 'postgres'
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2025-05-01' existing = {
  parent: virtualNetwork
  name: 'private-endpoints'
}

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: '${baseName}-logs'
  location: location
  tags: commonTags
  properties: {
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${baseName}-insights'
  location: location
  kind: 'web'
  tags: commonTags
  properties: {
    Application_Type: 'web'
    DisableIpMasking: false
    DisableLocalAuth: true
    IngestionMode: 'LogAnalytics'
    RetentionInDays: 30
    WorkspaceResourceId: logWorkspace.id
  }
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${baseName}-operations'
  location: 'global'
  tags: commonTags
  properties: {
    enabled: true
    groupShortName: take('ll-${normalizedEnvironment}', 12)
    emailReceivers: [
      {
        name: 'production-operations'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: registryName
  location: location
  tags: commonTags
  sku: {
    name: containerRegistrySkuName
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    dataEndpointEnabled: false
    policies: {
      azureADAuthenticationAsArmPolicy: {
        status: 'enabled'
      }
      exportPolicy: {
        status: 'disabled'
      }
    }
    publicNetworkAccess: 'Enabled'
  }
}

resource webIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${baseName}-web'
  location: location
  tags: commonTags
}

resource migratorIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${baseName}-migrator'
  location: location
  tags: commonTags
}

resource databaseAdminIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${baseName}-db-admin'
  location: location
  tags: commonTags
}

resource deploymentAcrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, deploymentPrincipalObjectId, acrPushRoleDefinitionId)
  scope: containerRegistry
  properties: {
    principalId: deploymentPrincipalObjectId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPushRoleDefinitionId
  }
}

resource webAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, webIdentity.id, acrPullRoleDefinitionId)
  scope: containerRegistry
  properties: {
    principalId: webIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

resource webTelemetryPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(applicationInsights.id, webIdentity.id, monitoringMetricsPublisherRoleDefinitionId)
  scope: applicationInsights
  properties: {
    principalId: webIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: monitoringMetricsPublisherRoleDefinitionId
  }
}

resource migratorAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, migratorIdentity.id, acrPullRoleDefinitionId)
  scope: containerRegistry
  properties: {
    principalId: migratorIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

resource databaseAdminAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, databaseAdminIdentity.id, acrPullRoleDefinitionId)
  scope: containerRegistry
  properties: {
    principalId: databaseAdminIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

resource dataProtectionStorage 'Microsoft.Storage/storageAccounts@2025-08-01' = {
  name: storageName
  location: location
  tags: commonTags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_ZRS'
  }
  properties: {
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    encryption: {
      keySource: 'Microsoft.Storage'
      requireInfrastructureEncryption: true
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
      }
    }
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
    }
    publicNetworkAccess: 'Disabled'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-08-01' = {
  parent: dataProtectionStorage
  name: 'default'
  properties: {
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    deleteRetentionPolicy: {
      enabled: true
      days: 30
    }
    isVersioningEnabled: true
  }
}

resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-08-01' = {
  parent: blobService
  name: 'data-protection'
  properties: {
    defaultEncryptionScope: '$account-encryption-key'
    denyEncryptionScopeOverride: true
    publicAccess: 'None'
  }
}

resource webDataProtectionAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionContainer.id, webIdentity.id, blobContributorRoleDefinitionId)
  scope: dataProtectionContainer
  properties: {
    principalId: webIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: blobContributorRoleDefinitionId
  }
}

resource blobPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.blob.${environment().suffixes.storage}'
  location: 'global'
  tags: commonTags
}

resource blobDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: blobPrivateDnsZone
  name: '${baseName}-blob-link'
  location: 'global'
  tags: commonTags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}

resource blobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2025-05-01' = {
  name: '${baseName}-blob-pe'
  location: location
  tags: commonTags
  properties: {
    privateLinkServiceConnections: [
      {
        name: 'blob'
        properties: {
          groupIds: [
            'blob'
          ]
          privateLinkServiceId: dataProtectionStorage.id
        }
      }
    ]
    subnet: {
      id: privateEndpointSubnet.id
    }
  }
}

resource blobPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2025-05-01' = {
  parent: blobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blob'
        properties: {
          privateDnsZoneId: blobPrivateDnsZone.id
        }
      }
    ]
  }
}

resource postgresPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'private.postgres.database.azure.com'
  location: 'global'
  tags: commonTags
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresPrivateDnsZone
  name: '${baseName}-postgres-link'
  location: 'global'
  tags: commonTags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}

module postgres './modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    administratorName: databaseAdminIdentity.name
    administratorObjectId: databaseAdminIdentity.properties.principalId
    backupRetentionDays: postgresBackupRetentionDays
    delegatedSubnetId: postgresSubnet.id
    geoRedundantBackup: postgresGeoRedundantBackup
    highAvailability: postgresHighAvailability
    location: location
    logWorkspaceId: logWorkspace.id
    maintenanceDay: postgresMaintenanceDay
    maintenanceHour: postgresMaintenanceHour
    privateDnsZoneId: postgresPrivateDnsZone.id
    serverName: '${baseName}-postgres'
    skuName: postgresSkuName
    tier: postgresTier
    storageSizeGB: postgresStorageSizeGB
    tags: commonTags
  }
  dependsOn: [
    postgresDnsLink
  ]
}

resource blobDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: blobService
  properties: {
    workspaceId: logWorkspace.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2026-01-01' = {
  name: '${baseName}-aca'
  location: location
  tags: commonTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logWorkspace.properties.customerId
        sharedKey: logWorkspace.listKeys().primarySharedKey
      }
    }
    publicNetworkAccess: 'Enabled'
    ingressConfiguration: {
      terminationGracePeriodSeconds: 60
    }
    vnetConfiguration: {
      infrastructureSubnetId: containerAppsSubnet.id
      internal: false
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

output containerRegistryName string = containerRegistry.name
output containerRegistryLoginServer string = containerRegistry.properties.loginServer
output postgresServerName string = postgres.outputs.serverName
