# Azure infrastructure

The executable templates implement the selected
[production profile](../docs/deployment/production-profile.md); the
[runbook](../docs/deployment/runbook.md) owns deployment and recovery procedures.
This repository change does not deploy Azure resources.

| File | Responsibility |
| --- | --- |
| `foundation.bicep` | long-lived network, identity, data, registry, monitoring, and Container Apps environment resources |
| `modules/postgres.bicep` | private PostgreSQL server, database, Entra administrator, and diagnostics |
| `application.bicep` | bootstrap and migration Jobs, digest-pinned Web revision, probes, and application alerts |

The foundation is deployed before image publication. The application template can
then deploy Jobs without changing Web (`deployWeb=false`) and deploy Web only after
database preparation succeeds. `postgresServerName` is empty for the foundation
server and explicit only during a verified recovery cutover.

Compile both entry points with the Bicep version selected in the release workflow:

```bash
az bicep install --version v0.46.1
az bicep build --file infra/foundation.bicep --outfile /tmp/logic-lab-foundation.json
az bicep build --file infra/application.bicep --outfile /tmp/logic-lab-application.json
```

Azure-backed `validate` and `what-if` require the qualified production scope and
values; the release workflow runs both before mutation. Do not track credentials or
production parameter values.
