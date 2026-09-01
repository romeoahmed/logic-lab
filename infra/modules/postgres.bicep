targetScope = 'resourceGroup'

param administratorName string
param administratorObjectId string
param backupRetentionDays int
param delegatedSubnetId string
param geoRedundantBackup string
param highAvailability string
param location string
param logWorkspaceId string
param maintenanceDay int
param maintenanceHour int
param privateDnsZoneId string
param serverName string
param skuName string
param storageSizeGB int
param tags object

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' = {
  name: serverName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: skuName
    tier: 'GeneralPurpose'
  }
  properties: {
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: tenant().tenantId
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: geoRedundantBackup
    }
    createMode: 'Create'
    highAvailability: {
      mode: highAvailability
    }
    maintenanceWindow: {
      customWindow: 'Enabled'
      dayOfWeek: maintenanceDay
      startHour: maintenanceHour
      startMinute: 0
    }
    network: {
      delegatedSubnetResourceId: delegatedSubnetId
      privateDnsZoneArmResourceId: privateDnsZoneId
      publicNetworkAccess: 'Disabled'
    }
    storage: {
      autoGrow: 'Enabled'
      storageSizeGB: storageSizeGB
      type: 'Premium_LRS'
    }
    version: '17'
  }
}

resource administrator 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2025-08-01' = {
  parent: server
  name: administratorObjectId
  properties: {
    principalName: administratorName
    principalType: 'ServicePrincipal'
    tenantId: tenant().tenantId
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2025-08-01' = {
  parent: server
  name: 'logiclab'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
  dependsOn: [
    administrator
  ]
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: server
  properties: {
    workspaceId: logWorkspaceId
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

output host string = server.properties.fullyQualifiedDomainName
output serverName string = server.name
