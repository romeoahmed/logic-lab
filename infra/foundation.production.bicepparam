using './foundation.bicep'

param environmentName = 'prod'
param location = readEnvironmentVariable('AZURE_LOCATION')
param deploymentPrincipalObjectId = readEnvironmentVariable('AZURE_DEPLOYMENT_PRINCIPAL_OBJECT_ID')
param alertEmail = readEnvironmentVariable('ALERT_EMAIL')
param containerRegistrySkuName = readEnvironmentVariable('CONTAINER_REGISTRY_SKU_NAME')
param postgresSkuName = readEnvironmentVariable('POSTGRES_SKU_NAME')
param postgresTier = readEnvironmentVariable('POSTGRES_TIER')
param postgresHighAvailability = readEnvironmentVariable('POSTGRES_HIGH_AVAILABILITY')
param postgresBackupRetentionDays = int(readEnvironmentVariable('POSTGRES_BACKUP_RETENTION_DAYS'))
param postgresGeoRedundantBackup = readEnvironmentVariable('POSTGRES_GEO_REDUNDANT_BACKUP')
param postgresStorageSizeGB = int(readEnvironmentVariable('POSTGRES_STORAGE_SIZE_GB'))
param postgresMaintenanceDay = int(readEnvironmentVariable('POSTGRES_MAINTENANCE_DAY'))
param postgresMaintenanceHour = int(readEnvironmentVariable('POSTGRES_MAINTENANCE_HOUR'))
