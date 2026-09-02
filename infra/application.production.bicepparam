using './application.bicep'

param environmentName = 'prod'
param location = readEnvironmentVariable('AZURE_LOCATION')
param webImage = readEnvironmentVariable('WEB_IMAGE')
param migratorImage = readEnvironmentVariable('MIGRATOR_IMAGE')
param deployWeb = bool(readEnvironmentVariable('DEPLOY_WEB'))
param postgresServerName = readEnvironmentVariable('POSTGRES_SERVER_NAME', '')
param webMinReplicas = int(readEnvironmentVariable('WEB_MIN_REPLICAS'))
param webMaxReplicas = int(readEnvironmentVariable('WEB_MAX_REPLICAS'))
