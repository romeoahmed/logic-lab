targetScope = 'resourceGroup'

@minLength(2)
@maxLength(12)
@description('Short deployment environment name, such as prod.')
param environmentName string

@description('Immutable Web image reference, including its sha256 digest.')
param webImage string

@description('Immutable database migrator image reference, including its sha256 digest.')
param migratorImage string

@description('Deploy the Web revision after database preparation succeeds.')
param deployWeb bool = true

@description('PostgreSQL server name. Override only for a verified recovery cutover.')
param postgresServerName string = ''

param location string = resourceGroup().location
param tags object = {}

var normalizedEnvironment = toLower(replace(environmentName, '_', '-'))
var suffix = take(uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName), 8)
var baseName = 'll-${normalizedEnvironment}-${suffix}'
var registryName = 'll${uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName, 'registry')}'
var storageName = 'll${uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName, 'storage')}'
var webName = '${baseName}-web'
var databaseName = 'logiclab'
var selectedPostgresServerName = empty(postgresServerName) ? '${baseName}-postgres' : postgresServerName
var commonTags = union(tags, {
  application: 'logic-lab'
  environment: environmentName
  'managed-by': 'bicep'
})
var databaseHost = postgres.properties.fullyQualifiedDomainName
var applicationOrigin = 'https://${webName}.${containerAppsEnvironment.properties.defaultDomain}'
var webConnectionString = 'Host=${databaseHost};Port=5432;Database=${databaseName};Username=${webIdentity.name};SSL Mode=VerifyFull'
var migratorConnectionString = 'Host=${databaseHost};Port=5432;Database=${databaseName};Username=${migratorIdentity.name};SSL Mode=VerifyFull'
var administratorConnectionString = 'Host=${databaseHost};Port=5432;Database=postgres;Username=${databaseAdminIdentity.name};SSL Mode=VerifyFull'

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: registryName
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2026-01-01' existing = {
  name: '${baseName}-aca'
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: '${baseName}-insights'
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' existing = {
  name: '${baseName}-operations'
}

resource dataProtectionStorage 'Microsoft.Storage/storageAccounts@2025-08-01' existing = {
  name: storageName
}

resource webIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: '${baseName}-web'
}

resource migratorIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: '${baseName}-migrator'
}

resource databaseAdminIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: '${baseName}-db-admin'
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' existing = {
  name: selectedPostgresServerName
}

resource databaseBootstrapJob 'Microsoft.App/jobs@2026-01-01' = {
  name: '${baseName}-init'
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${databaseAdminIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: databaseAdminIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'bootstrap'
          image: migratorImage
          args: [
            'bootstrap'
          ]
          env: [
            {
              name: 'ConnectionStrings__LogicLab'
              value: administratorConnectionString
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: databaseAdminIdentity.properties.clientId
            }
            {
              name: 'Database__Name'
              value: databaseName
            }
            {
              name: 'Database__WebPrincipalName'
              value: webIdentity.name
            }
            {
              name: 'Database__WebPrincipalObjectId'
              value: webIdentity.properties.principalId
            }
            {
              name: 'Database__MigratorPrincipalName'
              value: migratorIdentity.name
            }
            {
              name: 'Database__MigratorPrincipalObjectId'
              value: migratorIdentity.properties.principalId
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
}

resource databaseMigrationJob 'Microsoft.App/jobs@2026-01-01' = {
  name: '${baseName}-mig'
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migratorIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: migratorIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: migratorImage
          env: [
            {
              name: 'ConnectionStrings__LogicLab'
              value: migratorConnectionString
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: migratorIdentity.properties.clientId
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
}

resource web 'Microsoft.App/containerApps@2026-01-01' = if (deployWeb) {
  name: webName
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${webIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      maxInactiveRevisions: 3
      ingress: {
        external: true
        allowInsecure: false
        targetPort: 8080
        transport: 'Auto'
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: webIdentity.id
        }
      ]
      secrets: [
        {
          name: 'application-insights'
          value: applicationInsights.properties.ConnectionString
        }
      ]
    }
    template: {
      terminationGracePeriodSeconds: 60
      containers: [
        {
          name: 'web'
          image: webImage
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: webIdentity.properties.clientId
            }
            {
              name: 'Azure__ManagedIdentityClientId'
              value: webIdentity.properties.clientId
            }
            {
              name: 'Azure__PublicOrigin'
              value: applicationOrigin
            }
            {
              name: 'Azure__DataProtectionBlobUri'
              value: '${dataProtectionStorage.properties.primaryEndpoints.blob}data-protection/keys.xml'
            }
            {
              name: 'ConnectionStrings__LogicLab'
              value: webConnectionString
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              secretRef: 'application-insights'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 1
              periodSeconds: 5
              timeoutSeconds: 3
              failureThreshold: 30
              successThreshold: 1
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 3
              successThreshold: 1
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 1
              periodSeconds: 5
              timeoutSeconds: 3
              failureThreshold: 6
              successThreshold: 1
            }
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

resource failedRequestAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: '${baseName}-failed-requests'
  location: location
  tags: commonTags
  properties: {
    displayName: 'Logic Lab production failed requests'
    description: 'More than five failed requests were observed in five minutes.'
    enabled: true
    evaluationFrequency: 'PT5M'
    severity: 2
    scopes: [
      applicationInsights.id
    ]
    targetResourceTypes: [
      'Microsoft.Insights/components'
    ]
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: 'requests | where success == false | summarize AggregatedValue = count()'
          metricMeasureColumn: 'AggregatedValue'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
          failingPeriods: {
            minFailingPeriodsToAlert: 1
            numberOfEvaluationPeriods: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

resource dependencyFailureAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: '${baseName}-failed-dependencies'
  location: location
  tags: commonTags
  properties: {
    displayName: 'Logic Lab production failed dependencies'
    description: 'More than five failed dependencies were observed in five minutes.'
    enabled: true
    evaluationFrequency: 'PT5M'
    severity: 2
    scopes: [
      applicationInsights.id
    ]
    targetResourceTypes: [
      'Microsoft.Insights/components'
    ]
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: 'dependencies | where success == false | summarize AggregatedValue = count()'
          metricMeasureColumn: 'AggregatedValue'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
          failingPeriods: {
            minFailingPeriodsToAlert: 1
            numberOfEvaluationPeriods: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

output bootstrapJobName string = databaseBootstrapJob.name
output migrationJobName string = databaseMigrationJob.name
output webName string = webName
output webFqdn string = deployWeb ? web!.properties.configuration.ingress.fqdn : ''
