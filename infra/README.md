# Azure Infrastructure

The executable templates implement the selected
[production profile](../docs/deployment/production-profile.md); the
[runbook](../docs/deployment/runbook.md) owns deployment and recovery procedures.
This repository change does not deploy Azure resources.

| File                                   | Responsibility                                                                                     |
| -------------------------------------- | -------------------------------------------------------------------------------------------------- |
| `foundation.bicep`                     | long-lived network, identity, data, registry, monitoring, and Container Apps environment resources |
| `foundation.production.bicepparam`     | production foundation profile sourced from the GitHub Environment                                  |
| `modules/postgres.bicep`               | private PostgreSQL server, database, Entra administrator, and diagnostics                          |
| `application.bicep`                    | bootstrap and migration Jobs, digest-pinned Web revision, probes, and application alerts           |
| `application.production.bicepparam`    | production application profile sourced from release outputs and the GitHub Environment              |

The foundation is deployed before image publication. The application template can
then deploy Jobs without changing Web (`deployWeb=false`) and deploy Web only after
database preparation succeeds. `postgresServerName` is empty for the foundation
server and explicit only during a verified recovery cutover.

The GitHub `production` Environment stores private identifiers and destinations as
secrets, and non-sensitive service policy as variables. The `.bicepparam` files read
those values at compile time, perform typed conversion, and remain free of live
production values. The release workflow uses the official `azure/bicep-deploy`
action for validation, what-if, deployment, and template outputs.

The CI workflow is the executable static gate: it formats every Bicep source, lints
all templates, compiles both production parameter files with non-production fixtures,
and checks deployment scripts with Bash and ShellCheck. Simulated Azure and HTTP
responses verify that release checks reject an old ready revision, a wrong image,
or loss of readiness during the stability interval. For a focused local edit:

```bash
az bicep format --file infra/foundation.bicep
az bicep lint --file infra/foundation.bicep
```

Azure-backed `validate` and `what-if` require the qualified production scope and
values; the release workflow runs both through `azure/bicep-deploy` before mutation.
Do not compile production parameter files into tracked JSON or track credentials and
production values.
