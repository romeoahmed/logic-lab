# Production Qualification

> Status: open. Executable implementation is present; no Azure deployment or drill
> evidence has been recorded.

This record prevents infrastructure code from being mistaken for production proof.
The [Delivery record](../delivery.md#production-qualification) remains
the sole completion ledger. The [production runbook](./runbook.md) defines procedures;
this document identifies the evidence those procedures must produce.

## Item 42 decision record

Record an owner, review date, chosen value, and evidence link for every row before
closing item `42`.

| Decision                 | Current executable boundary                               | Qualification evidence still required                                            |
| ------------------------ | --------------------------------------------------------- | -------------------------------------------------------------------------------- |
| Azure scope              | one existing resource group and one region                | subscription, region, residency, quota, and owner approval                       |
| public origin            | one ACA-managed HTTPS origin                              | accepted hostname/TLS ownership and external reachability evidence               |
| application availability | single revision, one replica                              | explicit acceptance that V1 does not promise HA                                  |
| compute                  | Web and each Job use 0.5 CPU and 1 GiB                    | representative load, migration duration, throttling, and cost evidence           |
| PostgreSQL               | version 18, General Purpose, private VNet, Entra-only     | SKU, storage, IOPS, connection, maintenance, and capacity evidence               |
| continuity               | configurable HA, 7–35 day PITR, configurable geo backup   | signed RTO/RPO and restore-region decision                                       |
| Data Protection          | private Standard ZRS Blob, versioning, 30-day soft delete | key continuity and key-loss owner acceptance                                     |
| release identity         | GitHub environment OIDC at resource-group scope           | federated subject, approver, RBAC, and access-review evidence                    |
| runtime identity         | separate Web, Migrator, and database-admin identities     | Azure RBAC and PostgreSQL grant review                                           |
| artifact                 | .NET 10 `linux/amd64`, version tag plus OCI digest        | SBOM, provenance, dependency locks, retention, and verification owner            |
| telemetry                | Log Analytics, Application Insights, two failure alerts   | redaction review, staffed destination, useful thresholds, and retention approval |
| maintenance              | UTC day/hour parameters                                   | business window, owner, notice, and conflict procedure                           |
| recovery cutover         | explicit PostgreSQL server override                       | incident authority and IaC adoption procedure                                    |

The calibrated policy and Web/security evidence from items `35`–`41` remains a hard
dependency; infrastructure defaults do not substitute for it.

## Item 43 drill ledger

Each row needs a dated release tag, exact Web/Migrator digests, environment, operator,
result, raw evidence location, and accepted deviations.

| Drill                | Passing evidence                                                                                                         |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| clean migration      | bootstrap/migration/bootstrap Jobs succeed and Web identity cannot perform DDL                                           |
| failed migration     | nonzero Job blocks Web deployment while the prior revision remains ready                                                 |
| backup               | on-demand backup succeeds before mutation and is visible under the configured retention                                  |
| PITR                 | restore to a new server, isolated verification, explicit cutover, and retained source                                    |
| key continuity       | authentication survives controlled restart/revision replacement using the same Blob key ring                             |
| key recovery         | accepted Blob version restores readiness and protected-cookie continuity                                                 |
| upgrade              | reviewed version passes probes, representative workflows, and observation window                                         |
| application rollback | prior digest pair deploys without rebuild and preserves current data                                                     |
| schema compatibility | supported N/N-1 application pair works throughout the recorded rollback window                                           |
| telemetry            | requests, dependencies, migration logs, readiness, and alerts arrive redacted and actionable                             |
| load                 | qualified circuit, browser, workspace, transfer, and database corpora remain within accepted envelopes                   |
| security             | authentication, authorization concealment, antiforgery, CSP, TLS/proxy trust, secret scanning, and redaction fail closed |
| runbook              | an operator unfamiliar with the implementation completes release and recovery from this documentation                    |

Only after every applicable row passes and the owning plan dependencies are complete
may item `43` authorize the phrase “production-qualified.”
