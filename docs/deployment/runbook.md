# Production Runbook

> Status: executable procedure; qualification evidence remains open.

This runbook operates the selected [production profile](./production-profile.md).
The [qualification record](./qualification.md) owns required decisions and drill
evidence. The release workflow and Bicep templates are executable sources of exact
commands and parameters; this document owns operator intent and stop conditions.

## One-time preparation

1. Select and record the subscription, region, resource group, public origin, RTO,
   RPO, residency, maintenance, cost, alert owner, and operational owner.
2. Register the providers used by `infra/foundation.bicep`: `Microsoft.App`,
   `Microsoft.ContainerRegistry`, `Microsoft.DBforPostgreSQL`, `Microsoft.Insights`,
   `Microsoft.ManagedIdentity`, `Microsoft.Network`, `Microsoft.OperationalInsights`,
   and `Microsoft.Storage`.
3. Create a Microsoft Entra application and service principal for deployment. Add a
   GitHub federated credential with subject
   `repo:<owner>/<repository>:environment:production`; create no client secret.
4. Grant only the resource-group permissions required to deploy resources and assign
   template-owned roles. Do not use subscription-wide Owner for convenience.
5. Record the application client ID and service-principal object ID separately. The
   latter supplies `AZURE_DEPLOYMENT_PRINCIPAL_OBJECT_ID`.

The foundation creates separate Web, Migrator, and database-administrator managed
identities. Database bootstrap binds PostgreSQL roles to exact Entra object IDs; a
matching role name alone is not identity proof.

## GitHub production environment

Create `production`, restrict it to protected version tags, require an independent
reviewer, and prevent self-review where available.

Environment secrets:

| Secret                  | Meaning                          |
| ----------------------- | -------------------------------- |
| `AZURE_CLIENT_ID`       | deployment application client ID |
| `AZURE_TENANT_ID`       | Microsoft Entra tenant ID        |
| `AZURE_SUBSCRIPTION_ID` | qualified subscription ID        |

Environment variables:

| Variable                               | Decision                                   |
| -------------------------------------- | ------------------------------------------ |
| `AZURE_RESOURCE_GROUP`                 | bounded deployment scope                   |
| `AZURE_LOCATION`                       | qualified Azure region                     |
| `AZURE_DEPLOYMENT_PRINCIPAL_OBJECT_ID` | deployment service-principal object ID     |
| `ALERT_EMAIL`                          | staffed alert destination                  |
| `POSTGRES_SKU_NAME`                    | qualified General Purpose SKU              |
| `POSTGRES_HIGH_AVAILABILITY`           | `Disabled`, `SameZone`, or `ZoneRedundant` |
| `POSTGRES_BACKUP_RETENTION_DAYS`       | PITR retention, 7–35 days                  |
| `POSTGRES_GEO_REDUNDANT_BACKUP`        | `Enabled` or `Disabled`                    |
| `POSTGRES_STORAGE_SIZE_GB`             | qualified storage, at least 32 GiB         |
| `POSTGRES_MAINTENANCE_DAY`             | UTC day, Sunday `0` through Saturday `6`   |
| `POSTGRES_MAINTENANCE_HOUR`            | UTC hour, `0` through `23`                 |

`POSTGRES_SERVER_OVERRIDE` is normally absent. Set it only for a documented PITR
cutover, and retain it until the recovered server becomes the managed baseline.

Protect `main` with CI, CodeQL C#, and Dependency Review. Enable dependency and secret
security features. Protect release-tag creation separately so tags cannot bypass
reviewed code.

## Release

Push an immutable reviewed semantic-version tag. Manual dispatch accepts an existing
tag only. Production approval must happen before Azure credentials are issued.

The workflow then performs five ordered phases:

1. **Verify:** match tag to commit, restore locked graphs, and compile Bicep.
2. **Foundation:** authenticate through OIDC, validate and preview the change, then
   deploy long-lived resources.
3. **Artifacts:** publish Web and Migrator OCI images, resolve exact digests, and
   record locks, templates, SBOMs, provenance, and manifest evidence.
4. **Database:** preview application resources, deploy Jobs without Web, back up the
   active server, converge principals, migrate, and converge grants again.
5. **Web:** deploy the exact digest, wait for startup/readiness and external stability,
   then record the endpoint and digests.

If backup, bootstrap, migration, or readiness fails, stop. Preserve deployment, Job,
Application Insights, and Log Analytics evidence. Diagnose the root cause and choose
a reviewed roll-forward or the recovery path; do not edit production schema manually
or rerun unrelated substeps.

## Post-release verification

- Confirm tag, commit, selected PostgreSQL server, endpoint, and both digest-qualified
  images in the release evidence.
- Verify `/health/live` and `/health/ready` externally over HTTPS.
- Confirm one active Container Apps revision and one Web replica.
- Verify redacted request, dependency, migration, readiness, and alert telemetry.
- Confirm Data Protection key continuity through one controlled Web restart.
- Verify and retain OCI attestations and release evidence under the accepted policy.
- Record reviewer, observation interval, alerts, and deviations.

## Application rollback

Rollback deploys the previous known-good Web and Migrator digests from immutable
release evidence. Confirm N/N-1 schema compatibility, preview `application.bicep`, and
deploy Web against the currently selected PostgreSQL server. Do not run migration for
an ordinary application rollback.

Never rebuild an old commit, move `latest`, retag an unknown image, or run an EF down
migration against production. Contract schema only after the accepted rollback window
has closed.

## Failed migration or data incident

If migration fails before Web deployment, keep the previous Web revision active,
retain the failed Job evidence, and prefer a reviewed forward migration. Use PITR when
schema or data is unsafe; do not guess an inverse migration.

PITR procedure:

1. Record UTC incident time, source server, selected restore point, active digests,
   and latest accepted data point.
2. Restore Azure Database for PostgreSQL Flexible Server to a new uniquely named
   server. Never overwrite the source.
3. Verify private networking, DNS, PostgreSQL 18, Entra-only authentication, backup,
   diagnostics, and the `logiclab` database.
4. Assign the existing database-administrator managed identity as Entra administrator.
5. Select a digest pair compatible with the restore point. Deploy bootstrap and
   migration Jobs against the restored server; migrate only when that release requires
   it, then bootstrap grants again.
6. Validate durable-project counts and representative authorized reads in isolation.
7. Deploy Web against the restored server and complete normal observation checks.
8. Set `POSTGRES_SERVER_OVERRIDE` before any later release to prevent accidental
   cutback to the incident source.
9. Retain the source server and evidence until incident acceptance permits removal.

A restored server is an operational recovery target, not automatically the new
foundation resource. Adopt it into Bicep in a reviewed follow-up.

## Data Protection recovery

If the key-ring Blob is damaged or deleted, stop rollout, recover the last accepted
Blob version through Entra-authenticated storage tooling, and verify cookie continuity.
Do not create a fresh production key ring merely to make readiness green; that
invalidates existing protected cookies and requires incident approval.

## Sources

- [Azure Login with GitHub OIDC](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect)
- [Azure Container Apps revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions)
- [Azure Container Apps Jobs](https://learn.microsoft.com/en-us/azure/container-apps/jobs)
- [PostgreSQL backups](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/how-to-perform-backups)
- [PostgreSQL point-in-time restore](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/how-to-restore-custom-restore-point)
- [PostgreSQL Entra roles](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/security-manage-entra-users)
- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
