# Documentation Map

Read the shallowest document that answers the question. Target behavior, current
delivery, rationale, evidence, and executable configuration are different facts.

## Start here

| Question                               | Owner                                                    |
| -------------------------------------- | -------------------------------------------------------- |
| What is Logic Lab?                     | [Product and Workbench](./product.md)                    |
| Where does code or a fact belong?      | [Architecture](./architecture.md)                        |
| What does a domain term mean?          | [Domain Map](./domain/README.md)                         |
| What is complete and what comes next?  | [Delivery](./delivery.md)                                |
| How is the repository engineered?      | [Engineering](./engineering.md)                          |
| How is production shaped and operated? | [Production Profile](./deployment/production-profile.md) |

Before editing, read the target, its tests, one analogous implementation, and
the directly governing interface or rule. Do not preload the whole tree.

## Find the owner

| Fact                                                                                      | Owner                                                    |
| ----------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| system shape, dependency direction, module ownership                                      | [Architecture](./architecture.md)                        |
| visible behavior, layout, interaction, and recovery                                       | [Product and Workbench](./product.md)                    |
| domain language and translations                                                          | [Domain Map](./domain/README.md) and its glossaries      |
| observable authoring, compiler, runtime, format, presentation, host, and browser behavior | [Specifications](./specs/)                               |
| values exchanged at Application, browser, and HTTP seams                                  | [Contracts](./contracts/)                                |
| semantic, format, provisional, and measured limits                                        | [Policies](./policies.md)                                |
| build, dependency, language, test, and publication rules                                  | [Engineering](./engineering.md)                          |
| selected Azure shape and qualification boundary                                           | [Production Profile](./deployment/production-profile.md) |
| release, rollback, and recovery procedure                                                 | [Production Runbook](./deployment/runbook.md)            |
| unresolved production decisions and drill evidence                                        | [Qualification Record](./deployment/qualification.md)    |
| delivery order, completion, and future capability order                                   | [Delivery](./delivery.md)                                |
| hard-to-reverse rationale                                                                 | [ADRs](./adr/README.md)                                  |
| derivations, external sources, and dated measurements                                     | [Research](./research/README.md)                         |

Executable files own live SDK, package, project, workflow, infrastructure, and
configuration values. Prose explains those values without copying inventories.
If two owners conflict, fix the conflict; there is no generic “narrower file wins”
rule.

## Specifications

- [Circuit Authoring](./specs/circuit-authoring.md)
- [Component Contract Catalog V1](./specs/component-contract-catalog-v1.md)
- [Compiler](./specs/compiler.md)
- [Simulation Runtime](./specs/simulation-runtime.md)
- [`.logiclab` Project Package V1](./specs/project-package-v1.md)
- [Project Document JSON V1](./specs/project-document-json-v1.md)
- [Diagnostics V1](./specs/diagnostics-v1.md)
- [Diagram Presentation](./specs/diagram-presentation.md)
- [Web Host](./specs/web-host.md)
- [Browser Runtime](./specs/browser-runtime.md)

Boolean explanation and simplification are not V1. Their design is consolidated
in the non-normative [Boolean Analysis proposal](./future/boolean-analysis.md).

## Contracts

- [Editor Workspace](./contracts/editor-workspace.md)
- [Durable Project Catalog](./contracts/durable-project-catalog.md)
- [Browser Adapters](./contracts/browser-adapters.md)
- [HTTP Boundary](./contracts/http-boundary.md)

## Executable sources

| Fact                    | Source                                                    |
| ----------------------- | --------------------------------------------------------- |
| SDK and test runner     | [`global.json`](../global.json)                           |
| direct package versions | [`Directory.Packages.props`](../Directory.Packages.props) |
| shared build policy     | [`Directory.Build.props`](../Directory.Build.props)       |
| project graph           | [`logic-lab.slnx`](../logic-lab.slnx)                     |
| resolved dependencies   | application-root `packages.lock.json` files               |
| CI and release behavior | [`.github/workflows/`](../.github/workflows/)             |
| Azure resources         | [`infra/`](../infra/)                                     |

## Maintenance

- Define each fact once and link to its owner.
- Keep delivery state out of target specifications.
- Keep implementation detail out of domain language.
- Classify every number as semantic, format, provisional, or measured.
- Cite primary evidence at the supported claim.
- Remove superseded snapshots instead of layering disclaimers over them.
- Update indexes, links, and supersession metadata with every move.
