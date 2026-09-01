# Production Profile

> Status: selected V1 profile with executable implementation; qualification is incomplete.
>
> Qualification owner: [Delivery items 34–43](../delivery.md#production-qualification).

This document owns the selected Azure provider shape and its operational boundaries.
[Architecture](../architecture.md) owns module and dependency seams, [Web Host](../specs/web-host.md)
owns observable host behavior, and executable Bicep owns deployed resource values.

## Runtime shape

```text
GitHub Actions via OIDC
  -> Azure Container Registry
       -> Container Apps Bootstrap and Migration Jobs
       -> Container Apps Web
            -> PostgreSQL Flexible Server
            -> Data Protection Blob
            -> Azure Monitor / Application Insights
```

Web uses Container Apps single-revision mode with exactly one replica. The profile
does not promise application high availability. Startup and readiness probes gate
ingress cutover, but active Interactive Server circuits and process-local Workspaces
can require reload or authorized recovery when a revision is replaced.

The public boundary is Container Apps HTTPS ingress with one configured origin and
host. PostgreSQL and the Data Protection storage account use private connectivity.
The first profile has no public database endpoint, Front Door, public API, custom
SignalR Hub, Azure SignalR Service, or multiple active revision path.

## Artifact profile

- Web and Migrator target .NET 10 and `linux/amd64`.
- Publication remains framework-dependent, untrimmed, JIT-compiled, and
  globalization-capable.
- Release tags derive the application version; deployed resources use immutable OCI
  manifest digests rather than `latest`.
- Release evidence contains compiled templates, exact dependency locks, the release
  commit and image digests, SBOMs, and provenance attestations.

Azure Container Apps accepts Linux x86-64 images, so the ARM CI runner cross-publishes
for `linux-x64` ([container requirements](https://learn.microsoft.com/en-us/azure/container-apps/containers),
[SDK container publish](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish)).

## Identity and data

| Principal                       | Allowed responsibility                                                                                     | Explicit exclusion                                   |
| ------------------------------- | ---------------------------------------------------------------------------------------------------------- | ---------------------------------------------------- |
| GitHub production environment   | federated Azure deployment within its assigned scope                                                       | no stored client secret; no PR deployment permission |
| Web managed identity            | ACR pull, Data Protection Blob data access, Application Insights telemetry publish, PostgreSQL runtime DML | no schema migration or broad resource ownership      |
| Migrator managed identity       | ACR pull and reviewed PostgreSQL DDL migration                                                             | no Web request handling or unrelated Azure mutation  |
| Database-admin managed identity | bootstrap application principals and grants                                                                | no application runtime use                           |

Azure RBAC governs Azure resources. PostgreSQL 18 authentication uses Microsoft Entra
tokens, while database roles and grants govern schema and data access. The Web
connection contains no password and refreshes its token before expiry
([managed-identity connection](https://learn.microsoft.com/en-us/azure/postgresql/security/security-connect-with-managed-identity),
[Npgsql token rotation](https://www.npgsql.org/doc/security.html)).

Database bootstrap converges each PostgreSQL role to the exact managed-identity
object ID, including after an identity is replaced under the same name. Identity and
Durable Project migrations use separate history tables in a Migrator-owned
`migrations` schema and run in a deterministic order through the Migration Job. Web
has read-only access to those tables for readiness, never calls `Migrate()`, and
never becomes ready against an unexpected migration set.
The release backs up the selected active server before schema mutation.

Data Protection uses one stable application discriminator and a Blob-backed key ring
with least-privilege access. Losing access to the key ring blocks readiness rather
than silently creating a replacement production identity boundary.

## Host configuration

Production configuration fixes:

- public HTTPS origin and allowed host;
- the public host and the single isolated Container Apps ingress hop;
- PostgreSQL endpoint, database, Entra principal, and expected migration IDs;
- Data Protection Blob URI and application discriminator;
- Application Insights connection configuration;
- calibrated project, transfer, Workspace, scheduling, and browser policies.

Missing, wildcard, contradictory, or development-only values fail startup. Liveness
reports only process responsiveness. Readiness verifies configuration, database
reachability, schema compatibility, Data Protection access, and Work Coordinator
admission without exposing addresses, counts, secrets, or exceptions.

## Infrastructure and release

Bicep declares ACR, monitoring, PostgreSQL, storage, networking, private DNS,
Container Apps environment, Web, bootstrap and Migration Jobs, managed identities,
least-privilege assignments, probes, scaling, diagnostics, alerts, and tags. Templates
and tracked files contain no credentials.

The release workflow:

1. receives production-environment approval and verifies the release tag;
2. restores the locked production graphs and authenticates through GitHub OIDC;
3. validates, previews, and deploys the foundation;
4. publishes versioned Web and Migrator images and records their digests;
5. emits SBOM, provenance, lock, template, and release-manifest evidence;
6. previews the application, deploys the Jobs without updating Web, and establishes a
   database backup;
7. runs principal/grant bootstrap, migration, and grant convergence in order;
8. deploys Web by exact digest and waits for readiness and external stability.

Rollback redeploys the previous known-good digest; it never rebuilds an old commit.
Database changes follow expand/contract compatibility. Logical data recovery restores
PostgreSQL to a new server at a verified point in time before connection cutover.

## Qualification boundary

Before item `42` can close, the profile still requires concrete region, origin/domain,
resource sizes, owner assignments, RTO, RPO, maintenance window, PostgreSQL HA mode,
backup retention, geo-recovery posture, alert destinations, and calibrated policies.

Item `43` requires recorded drills for migration, backup/restore, Data Protection key
continuity, upgrade, rollback, telemetry, load, security, and the complete runbooks.
No workflow, Bicep validation, or successful build alone proves production
qualification.

The [runbook](./runbook.md) owns the operating procedure and the
[qualification record](./qualification.md) lists the evidence still required.

## Sources

- [Azure Container Apps revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions)
- [Azure Container Apps health probes](https://learn.microsoft.com/en-us/azure/container-apps/health-probes)
- [Managed identities in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity)
- [Azure Database for PostgreSQL Flexible Server](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/overview)
- [Azure Database for PostgreSQL version policy](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-version-policy)
- [PostgreSQL business continuity](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/concepts-business-continuity)
- [ASP.NET Core Data Protection key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0)
- [EF Core production migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [EF Core migration history](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/history-table)
- [Application Insights Microsoft Entra authentication](https://learn.microsoft.com/en-us/azure/azure-monitor/app/azure-ad-authentication)
- [Bicep deployment with Azure CLI](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deploy-cli)
- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
