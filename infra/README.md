# Azure infrastructure

These templates implement the selected
[Azure production profile](../docs/deployment/production-profile.md). They have
been compiled locally, but this repository change does not deploy Azure resources.
The [production runbook](../docs/deployment/runbook.md) owns setup, release, and
recovery procedures.

## Templates

| File | Responsibility |
| --- | --- |
| `foundation.bicep` | network, identities, registry, PostgreSQL, Blob, monitoring, and the Container Apps environment |
| `modules/postgres.bicep` | private PostgreSQL server, database, Entra administrator, and diagnostics |
| `application.bicep` | bootstrap and migration Jobs, Web revision, probes, and application alerts |

The split is intentional. A first deployment must create ACR and managed identities
before it can publish images. A release then deploys `application.bicep` with
`deployWeb=false`, runs database bootstrap and migrations, and only after success
deploys the Web revision with `deployWeb=true`.

`postgresServerName` normally resolves to the foundation server. It is overridden
only after a verified point-in-time recovery so subsequent releases keep using the
recovery server.

## Local static validation

Use the same Bicep version selected by the release workflow:

```bash
az bicep install --version v0.46.1
az bicep build --file infra/foundation.bicep --outfile /tmp/logic-lab-foundation.json
az bicep build --file infra/application.bicep --outfile /tmp/logic-lab-application.json
```

Azure-backed validation and preview require the qualified subscription, existing
resource group, region, recovery objectives, and alert owner. The release workflow
runs `az deployment group validate` and `what-if` with those approved values before
either deployment layer is changed. No credential or production value belongs in a
tracked parameter file.

## Fixed boundaries

- Web is single revision and exactly one replica; this is not an HA claim.
- PostgreSQL and Data Protection Blob traffic use the managed-environment VNet.
- PostgreSQL has Microsoft Entra authentication only and no public endpoint.
- ACR permits authenticated public data-plane access so GitHub-hosted runners can
  push images; admin and anonymous access are disabled.
- Web, Migrator, database administrator, and GitHub deployment identities are
  separate and least-privileged for their responsibilities.
- Application images are exact OCI digest references, never `latest`.

Changing these boundaries is an architecture and qualification change, not a local
template tweak.
