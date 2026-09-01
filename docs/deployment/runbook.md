# Production Runbook

> Status: executable procedure; qualification evidence remains open.

This runbook operates the selected [production profile](./production-profile.md).
The [qualification record](./qualification.md) owns required decisions and drill
evidence. The release workflow and Bicep templates are executable sources of exact
commands and parameters; this document owns operator intent and stop conditions.

## One-time preparation

1. Confirm the selected subscription, resource group, and region can deploy the
   qualified profile. Record the owner, available budget, accepted RTO/RPO,
   data-residency decision, quota, and reachability evidence outside the repository.
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

| Secret                                 | Meaning                                      |
| -------------------------------------- | -------------------------------------------- |
| `AZURE_CLIENT_ID`                      | deployment application client ID             |
| `AZURE_TENANT_ID`                      | Microsoft Entra tenant ID                    |
| `AZURE_SUBSCRIPTION_ID`                | qualified subscription ID                    |
| `AZURE_RESOURCE_GROUP`                 | qualified production resource group          |
| `AZURE_DEPLOYMENT_PRINCIPAL_OBJECT_ID` | deployment service-principal object ID        |
| `ALERT_EMAIL`                          | staffed alert destination                    |
| `POSTGRES_SERVER_OVERRIDE`             | optional verified recovery target server name |

Environment variables:

| Variable                          | Meaning                              |
| --------------------------------- | ------------------------------------ |
| `AZURE_LOCATION`                  | qualified deployment region          |
| `CONTAINER_REGISTRY_SKU_NAME`     | selected registry service tier       |
| `POSTGRES_TIER`                   | selected PostgreSQL compute tier     |
| `POSTGRES_SKU_NAME`               | selected PostgreSQL compute SKU      |
| `POSTGRES_HIGH_AVAILABILITY`      | accepted PostgreSQL HA mode          |
| `POSTGRES_BACKUP_RETENTION_DAYS`  | accepted PITR retention              |
| `POSTGRES_GEO_REDUNDANT_BACKUP`   | accepted geo-backup posture          |
| `POSTGRES_STORAGE_SIZE_GB`        | selected PostgreSQL storage capacity |
| `POSTGRES_MAINTENANCE_DAY`        | selected UTC maintenance day         |
| `POSTGRES_MAINTENANCE_HOUR`       | selected UTC maintenance hour        |
| `WEB_MIN_REPLICAS`                | selected minimum Web replicas        |
| `WEB_MAX_REPLICAS`                | selected maximum Web replicas        |

Leave `POSTGRES_SERVER_OVERRIDE` absent during normal operation. Set it only for a
documented PITR cutover, and retain it until the recovered server becomes the managed
baseline.

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
4. **Database:** preview application resources, deploy Jobs without Web, record the
   pre-migration PITR boundary, converge principals, migrate, and converge grants
   again.
5. **Web:** deploy the exact digest, wait for startup/readiness and external stability,
   then record the endpoint and digests.

If recovery-boundary capture, bootstrap, migration, or readiness fails, stop. Preserve
deployment, Job, Application Insights, and Log Analytics evidence. Diagnose the root
cause and choose a reviewed roll-forward or the recovery path; do not edit production
schema manually or rerun unrelated substeps.

## Post-release verification

- Confirm tag, commit, endpoint, and both digest-qualified images in the release
  evidence. Record the selected PostgreSQL server only in restricted operations
  evidence.
- Verify `/health/live` and `/health/ready` externally over HTTPS.
- Confirm one active Container Apps revision and the selected Web replica range.
- Verify redacted request, dependency, migration, readiness, and alert telemetry.
- Confirm Data Protection key continuity through one controlled Web restart.
- Verify and retain OCI attestations and release evidence under the accepted policy.
- Record reviewer, observation interval, alerts, and deviations.

## Cost control and offline periods

Create an Azure Cost Management budget scoped to the production resource group. Base
its amount on the available budget and notify the operational owner at 50%, 80%, and
100% actual cost, plus 80% forecast cost. Budget alerts are delayed notifications;
they do not stop resources. Review actual cost after the first 24–48 hours.

For an offline period shorter than seven days, stop PostgreSQL and allow Web to scale
to zero. Azure automatically starts a stopped Flexible Server after seven days. For a
longer offline period, export and verify required data, retain it outside the resource
group, and delete the group only after explicit owner approval.

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
- [PostgreSQL business continuity](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/concepts-business-continuity)
- [PostgreSQL point-in-time restore](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/how-to-restore-custom-restore-point)
- [Stop and start PostgreSQL](https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/how-to-stop-server)
- [PostgreSQL Entra roles](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/security-manage-entra-users)
- [Azure Cost Management budgets](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets)
- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
