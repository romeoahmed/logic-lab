# Azure production runbook

> Status: executable procedure; no production deployment or recovery drill has been
> performed by this repository change.

This runbook operates the selected
[production profile](./production-profile.md). The
[qualification record](./qualification.md) owns the evidence required before calling
that profile production-qualified.

## One-time preparation

### Azure scope and identity

1. Select one subscription, region, and existing resource group after recording RTO,
   RPO, data residency, maintenance, cost, and ownership decisions.
2. Register the resource providers used by `infra/foundation.bicep`: `Microsoft.App`,
   `Microsoft.ContainerRegistry`, `Microsoft.DBforPostgreSQL`,
   `Microsoft.Insights`, `Microsoft.ManagedIdentity`, `Microsoft.Network`,
   `Microsoft.OperationalInsights`, and `Microsoft.Storage`.
3. Create a Microsoft Entra application/service principal for GitHub deployment.
   Add a federated credential with subject
   `repo:<owner>/<repository>:environment:production`; do not create a client secret.
4. At the production resource-group scope, grant the deployment principal the
   resource-write and role-assignment permissions required by the Bicep templates.
   A bounded assignment of `Contributor` plus `Role Based Access Control
Administrator` is sufficient; do not grant subscription-wide `Owner` merely for
   convenience.
5. Record both the application client ID and service-principal object ID. The latter
   is `AZURE_DEPLOYMENT_PRINCIPAL_OBJECT_ID` and is not interchangeable with the
   client ID.

The foundation creates distinct Web, Migrator, and database-administrator managed
identities. Database bootstrap creates PostgreSQL roles by exact Entra object ID and
rewrites a stale Entra security label when an identity was replaced under the same
name. It never treats a matching role name alone as proof of identity.

### GitHub production environment

Create an environment named `production`, restrict it to protected version tags,
require an independent reviewer, and prevent self-review where repository policy
supports it. Store these values as environment secrets:

| Secret                  | Meaning                                 |
| ----------------------- | --------------------------------------- |
| `AZURE_CLIENT_ID`       | GitHub deployment application client ID |
| `AZURE_TENANT_ID`       | Microsoft Entra tenant ID               |
| `AZURE_SUBSCRIPTION_ID` | qualified Azure subscription ID         |

Store the non-secret production decisions as environment variables:

| Variable                               | Required decision                                       |
| -------------------------------------- | ------------------------------------------------------- |
| `AZURE_RESOURCE_GROUP`                 | existing bounded deployment scope                       |
| `AZURE_LOCATION`                       | qualified Azure region                                  |
| `AZURE_DEPLOYMENT_PRINCIPAL_OBJECT_ID` | deployment service-principal object ID                  |
| `ALERT_EMAIL`                          | staffed operational alert destination                   |
| `POSTGRES_SKU_NAME`                    | qualified General Purpose compute SKU                   |
| `POSTGRES_HIGH_AVAILABILITY`           | `Disabled`, `SameZone`, or `ZoneRedundant` from RTO/RPO |
| `POSTGRES_BACKUP_RETENTION_DAYS`       | PITR retention from 7 through 35 days                   |
| `POSTGRES_GEO_REDUNDANT_BACKUP`        | `Enabled` or `Disabled` from regional recovery needs    |
| `POSTGRES_STORAGE_SIZE_GB`             | qualified storage size, at least 32 GiB                 |
| `POSTGRES_MAINTENANCE_DAY`             | UTC maintenance day, Sunday `0` through Saturday `6`    |
| `POSTGRES_MAINTENANCE_HOUR`            | UTC maintenance hour, `0` through `23`                  |

`POSTGRES_SERVER_OVERRIDE` is normally absent. Set it only during a documented PITR
cutover and keep it until the recovered server becomes the new managed baseline.

Enable dependency graph, Dependabot alerts and security updates, secret scanning and
push protection. Protect `main` with the ARM build/test, CodeQL C#, and Dependency
Review checks. Protect release-tag creation separately; a tag must not bypass reviewed
code.

## Normal release

Create and push an immutable, reviewed version tag such as `v1.0.0`. Pushing the tag
starts `.github/workflows/release.yml`; manual dispatch accepts an existing tag only.
The production environment approval occurs before the job receives Azure credentials.

The workflow then:

1. proves the checked-out commit matches the semantic version tag;
2. restores locked `linux-x64` graphs and compiles both Bicep layers;
3. signs in with GitHub OIDC;
4. validates, previews, and incrementally deploys the foundation;
5. publishes Web and Migrator through the .NET SDK container target;
6. resolves ACR manifest digests, creates SPDX SBOMs, and publishes GitHub provenance
   and SBOM attestations against those digests;
7. uploads the manifest, compiled templates, dependency locks, and SBOM evidence;
8. previews the complete application change, then updates the Jobs without updating
   Web;
9. creates an on-demand backup of the selected PostgreSQL server, including the
   explicit recovery override when one is active;
10. converges principals and the Migrator-owned migration schema, runs the two
    isolated migration histories, then converges runtime grants over newly created
    objects; Web receives read-only migration-history access for readiness, never DDL;
11. deploys Web by exact digest; Container Apps keeps the prior single revision on
    traffic until startup and readiness probes succeed;
12. checks external readiness repeatedly and records both deployed digests and the
    endpoint in the workflow summary.

If a bootstrap, backup, migration, or readiness step fails, stop. Do not rerun random
substeps or edit the database manually. Preserve the Job execution, deployment,
Application Insights, and Log Analytics evidence, identify the root cause, and either
roll forward or follow the recovery procedure.

## Post-release checks

- Confirm the release summary contains the intended tag, commit, PostgreSQL server,
  and two digest-qualified images.
- Verify `/health/live` and `/health/ready` externally over HTTPS.
- Confirm the new Container Apps revision is the sole active revision and uses one
  replica.
- Confirm request and dependency telemetry arrives without secrets, connection
  strings, personal data, or unbounded identifiers.
- Confirm the Data Protection Blob has a current version and that authentication
  cookies survive one controlled Web restart.
- Verify the ACR attestations and retain the release-evidence artifact according to
  the qualification retention policy.
- Record the release, reviewer, observation interval, alerts, and any deviations in
  the qualification evidence store.

## Application rollback

Rollback is a new deployment of the previous known-good digest pair. Retrieve the Web
and Migrator references from that release's immutable evidence artifact. Confirm its
schema is compatible with the current database before proceeding.

Preview and deploy `infra/application.bicep` with the previous exact references,
`deployWeb=true`, and the currently selected PostgreSQL server. Do not run the
migration Job for an ordinary application rollback. Container Apps creates a new
revision and keeps the current revision on traffic until the rollback revision is
ready.

Never rebuild the old commit, move `latest`, retag an unknown manifest, or execute an
EF down migration against production. A destructive schema contraction is allowed
only after its preceding release has proved N/N-1 compatibility and the recorded
rollback window has closed.

## Failed migration or data incident

If migration fails before Web deployment, the old Web revision remains active. Keep
it active, retain the failed execution logs, and prefer a reviewed forward migration.
Use PITR when data or schema state itself is unsafe; do not guess an inverse migration.

For PITR:

1. Record the incident time in UTC, source server, selected restore time, active image
   digests, and latest accepted data point.
2. Restore Flexible Server to a new, uniquely named server with
   `az postgres flexible-server restore --source-server ... --restore-time ...`.
   Never restore over the source server.
3. Verify private networking, DNS, PostgreSQL 18, Entra-only authentication, backup
   policy, diagnostics, and the `logiclab` database before connection cutover.
4. Configure the existing database-administrator managed identity as the restored
   server's Microsoft Entra administrator with
   `az postgres flexible-server microsoft-entra-admin create`.
5. Choose the known-good Web/Migrator digest pair compatible with the restore point.
   Deploy the Jobs against `postgresServerName=<restored-server>`, run bootstrap,
   migration only if explicitly required by that release, and bootstrap again.
6. Validate durable-project counts and representative authorized reads in isolation.
7. Deploy Web against the restored server, wait for readiness, and perform the normal
   observation checks.
8. Set GitHub production variable `POSTGRES_SERVER_OVERRIDE` to the restored server
   before any later release. This prevents an ordinary release from silently cutting
   back to the incident source.
9. Retain the source server and evidence until incident acceptance and retention rules
   authorize removal.

The restored server is an operational recovery target, not automatically the new
foundation-owned database. Adopt it into the long-term IaC baseline in a reviewed
follow-up change.

## Data Protection recovery

The storage account disables public and shared-key access and enables Blob versioning
and soft delete. If the key-ring blob is damaged or deleted, stop Web rollout, recover
the last accepted Blob version through Entra-authenticated storage tooling, and verify
cookie continuity before resuming. Do not create a fresh production key ring to make
readiness green: that invalidates existing protected cookies and is a security-boundary
change requiring incident approval.

## Sources

- [Azure Login with GitHub OIDC](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect)
- [Azure Container Apps revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions)
- [Azure Container Apps Jobs](https://learn.microsoft.com/en-us/azure/container-apps/jobs)
- [PostgreSQL on-demand backups](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/how-to-perform-backups)
- [PostgreSQL point-in-time restore](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/how-to-restore-custom-restore-point)
- [Manage PostgreSQL Entra roles](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/security-manage-entra-users)
- [Application Insights Microsoft Entra authentication](https://learn.microsoft.com/en-us/azure/azure-monitor/app/azure-ad-authentication)
- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
