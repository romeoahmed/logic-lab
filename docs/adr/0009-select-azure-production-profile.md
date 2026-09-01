---
status: accepted
date: 2026-09-01
---

# Select the Azure production deployment profile

## Context

Production qualification needs one concrete provider shape that can prove migration,
readiness, restart, key continuity, recovery, upgrade, rollback, telemetry, load, and
security. The initial SQLite process was useful implementation evidence but couples
durable storage to one local filesystem and exposes provider-specific error and
migration behavior. The current Interactive Server host also owns process-local
circuits, Workspaces, export tickets, and rate-limit state, so adding replicas before
externalizing those facts would silently change observable behavior.

## Decision

- Run Web on Azure Container Apps and store images in Azure Container Registry.
- Use Azure Database for PostgreSQL Flexible Server through EF Core and Npgsql as the
  sole production database provider.
- Run one active revision with a production-selected Web replica range. A range that
  permits zero replicas trades idle cost for cold starts; this remains a production
  candidate, not a high-availability claim.
- Select region, service tiers, continuity, maintenance, storage, and scale in the
  protected production environment rather than hard-coding one deployment profile.
- Bootstrap reviewed database principals and apply both Identity and Durable Project
  migrations through separate Container Apps Jobs before Web readiness. Web,
  Migrator, and database administrator use distinct least-privilege Microsoft Entra
  identities and database roles.
- Persist ASP.NET Core Data Protection keys in Azure Blob Storage. Export staging
  remains process-local until multi-replica or cross-revision continuity is required
  by an accepted product or reliability target.
- Publish the baseline framework-dependent, untrimmed, JIT Web and Migrator artifacts
  as `linux/amd64` OCI images with exact version tags and immutable digests.
- Define Azure resources in Bicep and release from GitHub Actions through workload
  identity federation. No long-lived Azure credential is stored in GitHub.
- Export redacted OpenTelemetry data through the Azure Monitor distribution.
- Defer Azure SignalR, multiple active revisions, multiple replicas, Front Door, and
  distributed Workspace ownership until SLO, threat, or load evidence requires them.

The [production deployment profile](../deployment/production-profile.md) owns the
selected resources and qualification boundary. The
[Delivery](../delivery.md#production-qualification) alone owns
completion; this decision does not mark item `42` or `43` complete.

## Consequences

PostgreSQL migrations replace the SQLite migration sets rather than introducing a
runtime provider switch. If production SQLite data is discovered, it requires a
separate one-time transfer and verification plan.

Container Apps single-revision readiness preserves ingress while a new revision
starts, but scale-to-zero, when selected, and revision replacement do not preserve a
Blazor circuit or process-local Workspace. Upgrade, cold-start, and reconnect
behavior therefore remain explicit item `43` evidence. Any cost-first database
profile requires explicit acceptance of its recovery boundary.

## Rejected alternatives

- **Keep SQLite on persistent volume.** It retains single-writer and provider-specific
  operational constraints without improving the selected managed-service boundary.
- **Use a runtime database provider switch.** No supported production deployment needs
  two providers, so the switch would be a compatibility layer without a consumer.
- **Start with multiple replicas or Azure SignalR.** Neither persists the current
  process-owned Workspace and export state; both would make behavior harder to prove.
- **Use database passwords or GitHub client secrets.** Managed identity and workload
  identity federation provide the required credential-free boundaries.
- **Adopt Kubernetes or a custom Dockerfile.** The app needs neither cluster ownership
  nor container build customization; Container Apps and .NET SDK container publish
  express the selected runtime directly.

## Sources

- [Azure Container Apps revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions)
- [Azure Container Apps container requirements](https://learn.microsoft.com/en-us/azure/container-apps/containers)
- [Azure Database for PostgreSQL Flexible Server](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/overview)
- [Microsoft Entra authentication for PostgreSQL](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-azure-ad-authentication)
- [ASP.NET Core Data Protection key storage providers](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0)
- [.NET SDK container publishing](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish)
